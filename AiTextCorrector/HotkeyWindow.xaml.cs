using System;
using System.Windows;
using System.Windows.Input;
using AiTextCorrector.Models;

namespace AiTextCorrector;

public partial class HotkeyWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Func<HotkeyConfig, bool> _tryApply;
    private bool _ctrl;
    private bool _shift;
    private bool _alt;
    private bool _win;
    private Key _key = Key.None;

    public HotkeyWindow(HotkeyConfig current, Func<HotkeyConfig, bool> tryApply)
    {
        InitializeComponent();
        _tryApply = tryApply;

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

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // On ignore les touches de modificateur appuyées seules.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        var mods = Keyboard.Modifiers;
        _ctrl = mods.HasFlag(ModifierKeys.Control);
        _shift = mods.HasFlag(ModifierKeys.Shift);
        _alt = mods.HasFlag(ModifierKeys.Alt);
        _win = mods.HasFlag(ModifierKeys.Windows);
        _key = key;

        ErrorTextBlock.Visibility = Visibility.Collapsed;
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

    private void UpdateDisplay() => CaptureBox.Text = BuildConfig().Describe();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_key == Key.None)
        {
            ShowError("Appuie d'abord sur une touche.");
            return;
        }

        if (!_ctrl && !_shift && !_alt && !_win)
        {
            ShowError("Ajoute au moins un modificateur (Ctrl, Shift, Alt ou Win).");
            return;
        }

        if (_tryApply(BuildConfig()))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            ShowError("Cette combinaison est déjà utilisée par une autre application. Essaie-en une autre.");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
