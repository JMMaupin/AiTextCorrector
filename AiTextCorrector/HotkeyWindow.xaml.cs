using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AiTextCorrector.Interop;
using AiTextCorrector.Models;

namespace AiTextCorrector;

public partial class HotkeyWindow : Wpf.Ui.Controls.FluentWindow
{
    private const int ProbeHotkeyId = 9100;

    private readonly Func<HotkeyConfig, bool> _tryApply;
    private readonly HotkeyConfig _current;
    private bool _ctrl;
    private bool _shift;
    private bool _alt;
    private bool _win;
    private Key _key = Key.None;
    private bool _isAvailable;

    public HotkeyWindow(HotkeyConfig current, Func<HotkeyConfig, bool> tryApply)
    {
        InitializeComponent();
        _tryApply = tryApply;
        _current = current;

        _ctrl = current.Ctrl;
        _shift = current.Shift;
        _alt = current.Alt;
        _win = current.Win;
        try
        {
            _key = (Key)Enum.Parse(typeof(Key), current.Key, ignoreCase: true);
        }
        catch
        {
            _key = Key.Space;
        }

        UpdateDisplay();
        Loaded += (_, _) => CaptureBox.Focus();
    }

    private void CaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Quand Alt est enfoncé, WPF rapporte Key.System et place la vraie touche
        // dans SystemKey. Idem pour ImeProcessed / DeadCharProcessed (AltGr FR/EU).
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };

        // On ignore les touches de modificateur appuyées seules.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or Key.System or Key.None)
        {
            return;
        }

        // On lit l'état réel des touches plutôt que Keyboard.Modifiers : sur les
        // claviers FR/EU, AltGr lève à la fois Ctrl et Alt, et Keyboard.Modifiers
        // peut les rapporter de façon incohérente.
        _ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        _shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        _alt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
        _win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        _key = key;

        UpdateDisplay();
    }

    private HotkeyConfig BuildConfig() => new()
    {
        Ctrl = _ctrl,
        Shift = _shift,
        Alt = _alt,
        Win = _win,
        Key = _key.ToString()
    };

    private void UpdateDisplay()
    {
        CaptureBox.Text = BuildConfig().Describe();

        // Pas encore de combinaison complète : état neutre.
        if (_key == Key.None || (!_ctrl && !_shift && !_alt && !_win))
        {
            _isAvailable = false;
            SetNeutral();
            return;
        }

        // Si l'utilisateur re-choisit le raccourci déjà actif de l'app, il est
        // forcément disponible (c'est l'app elle-même qui le détient).
        _isAvailable = IsSameAsCurrent() || ProbeAvailable();
        SetAvailability(_isAvailable);
    }

    private bool IsSameAsCurrent() =>
        _ctrl == _current.Ctrl && _shift == _current.Shift &&
        _alt == _current.Alt && _win == _current.Win &&
        string.Equals(_key.ToString(), _current.Key, StringComparison.OrdinalIgnoreCase);

    // Teste réellement si la combinaison peut être enregistrée comme hotkey global,
    // en l'enregistrant temporairement puis en la libérant aussitôt.
    private bool ProbeAvailable()
    {
        uint mods = NativeMethods.MOD_NOREPEAT;
        if (_ctrl) mods |= NativeMethods.MOD_CONTROL;
        if (_shift) mods |= NativeMethods.MOD_SHIFT;
        if (_alt) mods |= NativeMethods.MOD_ALT;
        if (_win) mods |= NativeMethods.MOD_WIN;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(_key);

        if (NativeMethods.RegisterHotKey(IntPtr.Zero, ProbeHotkeyId, mods, vk))
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, ProbeHotkeyId);
            return true;
        }

        return false;
    }

    private static readonly Color GreenColor = Color.FromRgb(0x22, 0xC5, 0x5E);
    private static readonly Color RedColor = Color.FromRgb(0xEF, 0x44, 0x44);

    private void SetNeutral()
    {
        CaptureBorder.BorderBrush = Brushes.Transparent;
        StatusBorder.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = false;
    }

    private void SetAvailability(bool available)
    {
        var color = available ? GreenColor : RedColor;

        // Bordure colorée autour du champ + bandeau de statut au fond teinté.
        CaptureBorder.BorderBrush = new SolidColorBrush(color);

        StatusBorder.Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
        StatusTextBlock.Foreground = new SolidColorBrush(color);
        StatusTextBlock.Text = available
            ? "✓ Disponible"
            : "✗ Déjà utilisé par une autre application ou réservé par Windows. Choisis une autre combinaison.";

        StatusBorder.Visibility = Visibility.Visible;
        SaveButton.IsEnabled = available;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAvailable)
        {
            return;
        }

        if (_tryApply(BuildConfig()))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            // Course rare : la combinaison a été prise entre la sonde et l'application.
            SetAvailability(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
