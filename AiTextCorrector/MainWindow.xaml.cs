using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AiTextCorrector.Interop;
using AiTextCorrector.Models;
using AiTextCorrector.Services;
using Forms = System.Windows.Forms;

namespace AiTextCorrector;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private const int HotkeyId = 9001;
    private readonly ConfigService _configService = new();
    private readonly ClipboardService _clipboardService = new();
    private readonly TextTransformService _textTransformService = new();
    private readonly StartupService _startupService = new();
    private readonly ApiKeyStore _keyStore = new();
    private readonly MultiProviderCorrectionService _aiService;
    private readonly Forms.NotifyIcon _notifyIcon;
    private Forms.ToolStripMenuItem? _runWithWindowsItem;
    private readonly AppConfig _config;
    private IntPtr _lastForegroundWindow = IntPtr.Zero;
    // Vrai uniquement quand l'utilisateur choisit « Quitter » dans le menu du systray.
    // Tant qu'il est faux, la croix (✕) se contente de masquer la fenêtre.
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        // Suit en direct les changements de thème clair/sombre de Windows.
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
        _config = _configService.Load();
        _aiService = new MultiProviderCorrectionService(_config, _keyStore);
        _notifyIcon = CreateNotifyIcon();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        InitializeProviderSelection();
    }

    private void ApiButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ApiKeysWindow(_keyStore) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            StatusTextBlock.Text = "Clés API enregistrées dans le registre utilisateur.";
        }
    }

    private void InitializeProviderSelection()
    {
        ProviderComboBox.SelectedIndex = string.Equals(_config.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var icon = new Forms.NotifyIcon
        {
            Text = "AI Text Corrector V3",
            Visible = true,
            Icon = LoadTrayIcon()
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir", null, (_, _) => ShowMainWindow());

        _runWithWindowsItem = new Forms.ToolStripMenuItem("Run with Windows")
        {
            CheckOnClick = true,
            Checked = _startupService.IsEnabled()
        };
        _runWithWindowsItem.Click += (_, _) =>
        {
            // SetEnabled renvoie l'état réel après l'opération : on s'aligne dessus
            // pour que la coche reflète l'état effectif, même en cas d'échec.
            _runWithWindowsItem.Checked = _startupService.SetEnabled(_runWithWindowsItem.Checked);
        };
        menu.Items.Add(_runWithWindowsItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitApplication());
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => ShowMainWindow();
        return icon;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            // On charge l'icône depuis la ressource embarquée (pack URI) plutôt que
            // depuis un fichier sur le disque : ça fonctionne aussi en publication
            // single-file, où il n'y a pas de dossier Resources/ à côté de l'exe.
            var uri = new Uri("pack://application:,,,/Resources/AICorrector.ico", UriKind.Absolute);
            var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream is not null)
            {
                using (stream)
                {
                    return new System.Drawing.Icon(stream);
                }
            }
        }
        catch
        {
            // Ressource d'icône manquante ou invalide : on retombe sur l'icône système.
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        var source = HwndSource.FromHwnd(helper.Handle);
        source?.AddHook(WndProc);

        if (!RegisterHotkey(helper.Handle, _config.Hotkey))
        {
            StatusTextBlock.Text = $"Impossible d'enregistrer le raccourci ({_config.Hotkey.Describe()}) — déjà utilisé ? Change-le via « Raccourci… ».";
        }
    }

    private static (uint Modifiers, uint VirtualKey) ToWin32(HotkeyConfig hk)
    {
        uint mods = NativeMethods.MOD_NOREPEAT;
        if (hk.Ctrl) mods |= NativeMethods.MOD_CONTROL;
        if (hk.Shift) mods |= NativeMethods.MOD_SHIFT;
        if (hk.Alt) mods |= NativeMethods.MOD_ALT;
        if (hk.Win) mods |= NativeMethods.MOD_WIN;

        var key = System.Windows.Input.Key.Space;
        try
        {
            key = (System.Windows.Input.Key)Enum.Parse(typeof(System.Windows.Input.Key), hk.Key, ignoreCase: true);
        }
        catch
        {
            // Nom de touche invalide : on retombe sur Espace.
        }

        return (mods, (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key));
    }

    private bool RegisterHotkey(IntPtr handle, HotkeyConfig hk)
    {
        var (mods, vk) = ToWin32(hk);
        return NativeMethods.RegisterHotKey(handle, HotkeyId, mods, vk);
    }

    // Tente d'appliquer un nouveau raccourci. Renvoie false si la combinaison est déjà
    // prise par une autre application (l'ancien raccourci est alors restauré).
    public bool TryApplyHotkey(HotkeyConfig hk)
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.UnregisterHotKey(handle, HotkeyId);

        if (RegisterHotkey(handle, hk))
        {
            _config.Hotkey = hk;
            _configService.Save(_config);
            StatusTextBlock.Text = $"Raccourci défini : {hk.Describe()}.";
            return true;
        }

        // Échec : on restaure l'ancien raccourci.
        RegisterHotkey(handle, _config.Hotkey);
        return false;
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var current = new HotkeyConfig
        {
            Ctrl = _config.Hotkey.Ctrl,
            Shift = _config.Hotkey.Shift,
            Alt = _config.Hotkey.Alt,
            Win = _config.Hotkey.Win,
            Key = _config.Hotkey.Key
        };

        var dialog = new HotkeyWindow(current, TryApplyHotkey) { Owner = this };
        dialog.ShowDialog();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Clic sur la croix (✕) : on ne ferme pas réellement l'application, on la
        // masque dans la zone de notification. Le raccourci global et l'icône
        // systray restent actifs pour pouvoir la rouvrir.
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Sortie réelle demandée via « Quitter » : on libère les ressources.
        var helper = new WindowInteropHelper(this);
        NativeMethods.UnregisterHotKey(helper.Handle, HotkeyId);
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
        Application.Current.Shutdown();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            OpenFromHotkey();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OpenFromHotkey()
    {
        _lastForegroundWindow = NativeMethods.GetForegroundWindow();
        ShowMainWindow();
    }

    // Appelée lorsqu'un second lancement de l'appli demande à réafficher l'instance existante.
    public void ShowFromAnotherInstance() => ShowMainWindow();

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Focus();
        InputTextBox.Focus();
    }

    private async Task RunPromptAsync(string prompt)
    {
        var input = InputTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            StatusTextBlock.Text = "Aucun texte à corriger.";
            return;
        }

        try
        {
            SetBusy(true, $"Correction en cours via {_config.Provider}...");
            var result = await _aiService.CorrectAsync(input, prompt);
            var finalText = AsciiOnlyCheckBox.IsChecked == true
                ? _textTransformService.ToAsciiPlainText(result)
                : _textTransformService.ToPlainUnicodeText(result);

            OutputTextBox.Text = finalText;

            if (AutoCopyCheckBox.IsChecked == true)
            {
                _clipboardService.WritePlainText(finalText);
                StatusTextBlock.Text = $"Correction terminée via {_config.Provider}. Résultat copié dans le presse-papiers.";
            }
            else
            {
                StatusTextBlock.Text = $"Correction terminée via {_config.Provider}.";
            }

            if (AutoPasteBackCheckBox.IsChecked == true)
            {
                await PasteBackInternalAsync();
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Erreur API: " + ex.Message;
        }
        finally
        {
            SetBusy(false, StatusTextBlock.Text);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        StrictCorrectionButton.IsEnabled = !busy;
        RewriteLightButton.IsEnabled = !busy;
        ProfessionalButton.IsEnabled = !busy;
        CustomButton.IsEnabled = !busy;
        PasteClipboardButton.IsEnabled = !busy;
        CopyResultButton.IsEnabled = !busy;
        PasteBackButton.IsEnabled = !busy;
        ClearInputButton.IsEnabled = !busy;
        ClearOutputButton.IsEnabled = !busy;
        AutoCopyCheckBox.IsEnabled = !busy;
        AsciiOnlyCheckBox.IsEnabled = !busy;
        AutoPasteBackCheckBox.IsEnabled = !busy;
        ProviderComboBox.IsEnabled = !busy;
        HotkeyButton.IsEnabled = !busy;
        StatusTextBlock.Text = status;
    }

    private async void StrictCorrectionButton_Click(object sender, RoutedEventArgs e)
        => await RunPromptAsync(_config.Prompts.StrictCorrection);

    private async void RewriteLightButton_Click(object sender, RoutedEventArgs e)
        => await RunPromptAsync(_config.Prompts.CorrectionAndRewrite);

    private async void ProfessionalButton_Click(object sender, RoutedEventArgs e)
        => await RunPromptAsync(_config.Prompts.ProfessionalStyle);

    private async void CustomButton_Click(object sender, RoutedEventArgs e)
        => await RunPromptAsync(_config.Prompts.CustomPrompt);

    private void PasteClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        InputTextBox.Text = _clipboardService.TryReadText();
        StatusTextBlock.Text = "Texte chargé depuis le presse-papiers.";
        InputTextBox.Focus();
    }

    private void CopyResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputTextBox.Text))
        {
            StatusTextBlock.Text = "Aucun résultat à copier.";
            return;
        }

        var text = AsciiOnlyCheckBox.IsChecked == true
            ? _textTransformService.ToAsciiPlainText(OutputTextBox.Text)
            : _textTransformService.ToPlainUnicodeText(OutputTextBox.Text);

        _clipboardService.WritePlainText(text);
        StatusTextBlock.Text = "Résultat copié dans le presse-papiers.";
    }

    private async void PasteBackButton_Click(object sender, RoutedEventArgs e)
    {
        await PasteBackInternalAsync();
    }

    private async Task PasteBackInternalAsync()
    {
        if (string.IsNullOrWhiteSpace(OutputTextBox.Text))
        {
            StatusTextBlock.Text = "Aucun résultat à recoller.";
            return;
        }

        var text = AsciiOnlyCheckBox.IsChecked == true
            ? _textTransformService.ToAsciiPlainText(OutputTextBox.Text)
            : _textTransformService.ToPlainUnicodeText(OutputTextBox.Text);

        _clipboardService.WritePlainText(text);
        Hide();
        await Task.Delay(180);

        if (_lastForegroundWindow != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_lastForegroundWindow);
            await Task.Delay(120);
            _clipboardService.SimulatePaste();
            StatusTextBlock.Text = "Texte recollé dans l'application d'origine.";
        }
        else
        {
            StatusTextBlock.Text = "Fenêtre d'origine introuvable, mais le texte est dans le presse-papiers.";
        }
    }

    private void ClearInputButton_Click(object sender, RoutedEventArgs e)
    {
        InputTextBox.Clear();
        StatusTextBlock.Text = "Texte source vidé.";
    }

    private void ClearOutputButton_Click(object sender, RoutedEventArgs e)
    {
        OutputTextBox.Clear();
        StatusTextBlock.Text = "Texte corrigé vidé.";
    }

    private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var selected = (ProviderComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "OpenAI";
        _config.Provider = selected;
        _configService.Save(_config);
        StatusTextBlock.Text = $"Provider actif : {selected}.";
    }
}
