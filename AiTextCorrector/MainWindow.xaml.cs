using System;
using System.ComponentModel;
using System.Linq;
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

    // Suivi du presse-papiers : dernier texte copié et l'instant de sa copie.
    // Sert au « collage intelligent » : si l'UI est ouverte au raccourci moins de
    // 3 s après une copie de texte, ce texte est injecté automatiquement dans l'input.
    private string? _lastClipboardText;
    private DateTime _lastClipboardTextTime = DateTime.MinValue;
    private static readonly TimeSpan AutoInjectWindow = TimeSpan.FromSeconds(3);

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

    // Tous les providers gérés par l'application, dans l'ordre d'affichage.
    private static readonly string[] AllProviders = { "OpenAI", "Anthropic", "GLM", "Groq" };

    private void ApiButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ApiKeysWindow(_keyStore) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            StatusTextBlock.Text = "Clés API enregistrées dans le registre utilisateur.";
            // Les clés ont pu changer : on rafraîchit la liste des providers.
            InitializeProviderSelection();
        }
    }

    private void InitializeProviderSelection()
    {
        // On ne propose que les providers pour lesquels une clé est configurée.
        var available = AllProviders.Where(p => _keyStore.HasKeyFor(p)).ToArray();

        ProviderComboBox.Items.Clear();
        foreach (var p in available)
        {
            ProviderComboBox.Items.Add(new ComboBoxItem { Content = p });
        }

        if (available.Length == 0)
        {
            // Aucune clé : rien à sélectionner. On invite à en configurer une.
            ProviderComboBox.IsEnabled = false;
            StatusTextBlock.Text = "Aucune clé API configurée. Clique sur « API » pour en ajouter une.";
            return;
        }

        ProviderComboBox.IsEnabled = true;

        // On resélectionne le provider courant s'il est encore disponible, sinon
        // on bascule sur le premier disponible (et on mémorise ce choix).
        var index = Array.FindIndex(available, p => string.Equals(p, _config.Provider, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = 0;
            _config.Provider = available[0];
            _configService.Save(_config);
        }

        ProviderComboBox.SelectedIndex = index;
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

        // Surveillance continue du presse-papiers via les notifications Windows.
        NativeMethods.AddClipboardFormatListener(helper.Handle);
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
            StatusTextBlock.Text = _configService.Save(_config)
                ? $"Raccourci défini : {hk.Describe()}."
                : $"Raccourci défini : {hk.Describe()} — mais il ne sera pas conservé au prochain lancement.";
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
        NativeMethods.RemoveClipboardFormatListener(helper.Handle);
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
        else if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            OnClipboardUpdated();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // Appelé à chaque changement du presse-papiers : si du texte y est présent,
    // on le mémorise avec l'instant de l'événement.
    private void OnClipboardUpdated()
    {
        var text = _clipboardService.TryReadText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // On ignore les changements provoqués par l'application elle-même (copie
        // automatique du résultat) : sans ce filtre, appuyer sur le raccourci juste
        // après une correction réinjecterait le texte déjà corrigé en entrée.
        if (string.Equals(text, _clipboardService.LastWrittenText, StringComparison.Ordinal))
        {
            return;
        }

        _lastClipboardText = text;
        _lastClipboardTextTime = DateTime.UtcNow;
    }

    private void OpenFromHotkey()
    {
        _lastForegroundWindow = NativeMethods.GetForegroundWindow();

        // Collage intelligent : un texte copié il y a moins de 3 s est chargé dans le
        // champ source, en remplaçant son contenu précédent. Le remplacement est
        // indispensable : le champ n'étant jamais vidé automatiquement, une condition
        // « seulement si le champ est vide » ne se vérifiait qu'au tout premier usage
        // de la session, puis plus jamais.
        if (_lastClipboardText is { Length: > 0 } &&
            DateTime.UtcNow - _lastClipboardTextTime <= AutoInjectWindow)
        {
            InputTextBox.Text = _lastClipboardText;
            InputTextBox.CaretIndex = InputTextBox.Text.Length;
            StatusTextBlock.Text = "Texte du presse-papiers chargé automatiquement.";
        }

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
                StatusTextBlock.Text = _clipboardService.WritePlainText(finalText)
                    ? $"Correction terminée via {_config.Provider}. Résultat copié dans le presse-papiers."
                    : $"Correction terminée via {_config.Provider}. Échec de la copie dans le presse-papiers.";
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
        // Gestionnaire async void : une exception non rattrapée ici terminerait le
        // processus au lieu de remonter à l'appelant.
        try
        {
            await PasteBackInternalAsync();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Impossible de recoller le texte : " + ex.Message;
        }
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

        // Pendant un repeuplement de la liste (Clear/Add), SelectedItem passe par
        // null : on ignore ces transitions pour ne pas écraser la config.
        var selected = (ProviderComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrEmpty(selected)) return;

        _config.Provider = selected;
        StatusTextBlock.Text = _configService.Save(_config)
            ? $"Provider actif : {selected}."
            : $"Provider actif : {selected} — mais la configuration n'a pas pu être enregistrée.";
    }
}
