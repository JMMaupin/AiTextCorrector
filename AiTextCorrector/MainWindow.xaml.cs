using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AiTextCorrector.Controls;
using AiTextCorrector.Interop;
using AiTextCorrector.Models;
using AiTextCorrector.Services;
using AiTextCorrector.Settings;
using Forms = System.Windows.Forms;

namespace AiTextCorrector;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private const int HotkeyId = 9001;
    private readonly ConfigService _configService = new();
    private readonly ClipboardService _clipboardService = new();
    private readonly SelectionCaptureService _selectionCapture = new();
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

    // Suivi du presse-papiers pour le « collage intelligent ». On ne mémorise que
    // l'INSTANT du dernier changement, jamais le texte : lire le presse-papiers
    // depuis la notification est peu fiable (voir OnClipboardUpdated).
    private DateTime _lastClipboardChangeTime = DateTime.MinValue;
    private static readonly TimeSpan AutoInjectWindow = TimeSpan.FromSeconds(5);

    // Temps accordé à l'application source pour déposer son texte une fois la
    // copie signalée.
    private static readonly TimeSpan SelectionReadTimeout = TimeSpan.FromSeconds(1);

    // Copie tardive : une application lente (Word, Outlook, Teams…) peut honorer
    // le Ctrl+C simulé après le délai de capture. Jusqu'à cette échéance, le
    // prochain changement du presse-papiers est traité comme la sélection.
    private DateTime _lateCaptureDeadline = DateTime.MinValue;
    private static readonly TimeSpan LateCaptureWindow = TimeSpan.FromSeconds(2);

    // Vrai pendant la capture déclenchée par le raccourci : les attentes étant
    // asynchrones, un second appui pourrait sinon lancer une capture concurrente.
    private bool _isCapturing;

    public MainWindow()
    {
        InitializeComponent();
        CharacterUndoHistory.Attach(InputTextBox);

        // Titre unique pour la fenêtre, la barre de titre et la zone de notification.
        Title = AppInfo.DisplayName;
        AppTitleBar.Title = AppInfo.DisplayName;

        // Suit en direct les changements de thème clair/sombre de Windows.
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
        _config = _configService.Load();
        _aiService = new MultiProviderCorrectionService(_config, _keyStore);
        _notifyIcon = CreateNotifyIcon();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        InitializeProviderSelection();
        InitializeLanguageSelection();
    }

    // Tous les providers gérés par l'application, dans l'ordre d'affichage.
    private static readonly string[] AllProviders = { "OpenAI", "Anthropic", "GLM", "Groq" };

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_config, _configService, _keyStore, TryApplyHotkey) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        // Les clés ont pu changer : on rafraîchit la liste des providers. Sans aucune
        // clé, cette méthode affiche sa propre invite, qu'on ne remplace pas.
        InitializeProviderSelection();
        if (ProviderComboBox.IsEnabled)
        {
            StatusTextBlock.Text = dialog.ConfigSaved
                ? "Settings saved."
                : "Settings applied, but they could not be written to disk.";
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
            Text = AppInfo.DisplayName,
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

    // Appelé à chaque changement du presse-papiers.
    //
    // En temps normal on se contente d'horodater l'événement : la lecture a lieu
    // plus tard, à l'appui du raccourci. Seule exception, la copie tardive d'une
    // application lente, lue avec réessais car le texte n'est pas toujours
    // disponible à l'instant de la notification.
    private void OnClipboardUpdated()
    {
        _lastClipboardChangeTime = DateTime.UtcNow;

        if (DateTime.UtcNow <= _lateCaptureDeadline)
        {
            _lateCaptureDeadline = DateTime.MinValue;
            // Le filtre reste actif ici : pendant ce délai, le changement peut aussi
            // venir de nous (bouton de copie du résultat).
            _ = LoadClipboardIntoInputAsync(SelectionReadTimeout, skipOwnOutput: true);
        }
    }

    private async void OpenFromHotkey()
    {
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            _lastForegroundWindow = NativeMethods.GetForegroundWindow();
            _lateCaptureDeadline = DateTime.MinValue;

            // On demande d'abord à l'application source de copier sa sélection, tant
            // qu'elle a encore le focus.
            if (await _selectionCapture.TryCopySelectionAsync())
            {
                _lastClipboardChangeTime = DateTime.UtcNow;

                // La copie vient de NOTRE Ctrl+C : c'est la sélection, même si elle
                // est identique au dernier résultat (texte recollé puis resélectionné
                // pour une autre correction). Le filtre anti-réinjection l'écartait.
                await LoadClipboardIntoInputAsync(SelectionReadTimeout, skipOwnOutput: false);
            }
            else
            {
                // Aucune copie constatée : soit rien n'est sélectionné, soit
                // l'application source est lente. Collage intelligent d'abord : si le
                // presse-papiers a changé récemment (Ctrl+C manuel), on le reprend. Le
                // remplacement est indispensable : le champ n'étant jamais vidé
                // automatiquement, une condition « seulement si le champ est vide » ne
                // se vérifierait qu'au tout premier usage de la session.
                if (DateTime.UtcNow - _lastClipboardChangeTime <= AutoInjectWindow)
                {
                    var text = _clipboardService.TryReadText();

                    // On n'injecte pas notre propre production : après une correction avec
                    // copie automatique, le presse-papiers contient le texte déjà corrigé.
                    if (text.Length > 0 &&
                        !string.Equals(text, _clipboardService.LastWrittenText, StringComparison.Ordinal))
                    {
                        SetInputFromClipboard(text);
                    }
                }

                // Puis on guette une copie tardive, qui remplacera ce contenu.
                _lateCaptureDeadline = DateTime.UtcNow + LateCaptureWindow;
            }

            ShowMainWindow();
        }
        finally
        {
            _isCapturing = false;
        }
    }

    // Lit le presse-papiers après une copie et en remplit le champ source.
    private async Task LoadClipboardIntoInputAsync(TimeSpan timeout, bool skipOwnOutput)
    {
        var text = await _clipboardService.ReadTextAfterCopyAsync(timeout);
        if (text.Length == 0) return;
        if (skipOwnOutput && string.Equals(text, _clipboardService.LastWrittenText, StringComparison.Ordinal)) return;

        SetInputFromClipboard(text);
    }

    private void SetInputFromClipboard(string text)
    {
        InputTextBox.Text = text;
        InputTextBox.CaretIndex = text.Length;
        StatusTextBlock.Text = "Texte du presse-papiers chargé automatiquement.";
    }

    // Appelée lorsqu'un second lancement de l'appli demande à réafficher l'instance existante.
    public void ShowFromAnotherInstance() => ShowMainWindow();

    // Appelée par l'installeur, qui doit remplacer l'exe : sortie propre (raccourci
    // libéré, icône retirée de la zone de notification) plutôt qu'un arrêt forcé.
    public void ExitFromAnotherInstance() => ExitApplication();

    // Bulle de la zone de notification plutôt que la barre d'état : au premier
    // lancement, celle-ci signale l'absence de clé API, message à ne pas masquer.
    public void NotifySetupCompleted(bool isUpdate)
    {
        var text = isUpdate
            ? $"Updated to version {AppInfo.Version}."
            : $"Installed. Open it from any application with {_config.Hotkey.Describe()}.";
        _notifyIcon.ShowBalloonTip(5000, AppInfo.Product, text, Forms.ToolTipIcon.Info);
    }

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
            StatusTextBlock.Text = "No text to correct.";
            return;
        }

        await RunAiAsync(() => _aiService.CorrectAsync(input, prompt),
            $"Correcting via {_config.Provider}…",
            $"Corrected via {_config.Provider}.");
    }

    // Exécute un appel IA puis le traitement commun à la correction et à la
    // traduction : mode ASCII, copie et recollage automatiques.
    private async Task RunAiAsync(Func<Task<string>> call, string busyStatus, string doneStatus)
    {
        try
        {
            SetBusy(true, busyStatus);
            var result = await call();
            var finalText = AsciiOnlyCheckBox.IsChecked == true
                ? _textTransformService.ToAsciiPlainText(result)
                : _textTransformService.ToPlainUnicodeText(result);

            OutputTextBox.Text = finalText;

            if (AutoCopyCheckBox.IsChecked == true)
            {
                StatusTextBlock.Text = _clipboardService.WritePlainText(finalText)
                    ? $"{doneStatus} Result copied to the clipboard."
                    : $"{doneStatus} Copy to the clipboard failed.";
            }
            else
            {
                StatusTextBlock.Text = doneStatus;
            }

            if (AutoPasteBackCheckBox.IsChecked == true)
            {
                await PasteBackInternalAsync();
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "API error: " + ex.Message;
        }
        finally
        {
            SetBusy(false, StatusTextBlock.Text);
        }
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not string language) return;

        // Le texte corrigé tel qu'affiché, retouches manuelles comprises ; à défaut
        // le texte source, pour une traduction directe.
        var source = string.IsNullOrWhiteSpace(OutputTextBox.Text) ? InputTextBox.Text : OutputTextBox.Text;
        if (string.IsNullOrWhiteSpace(source))
        {
            StatusTextBlock.Text = "No text to translate.";
            return;
        }

        // Prompt modifié sans le marqueur {language} : la langue cible est ajoutée à
        // la fin, sinon le modèle ne saurait pas vers quelle langue traduire.
        var template = _config.Prompts.Translation;
        var instruction = template.Contains("{language}", StringComparison.Ordinal)
            ? template.Replace("{language}", language, StringComparison.Ordinal)
            : $"{template}\nTarget language: {language}.";
        await RunAiAsync(() => _aiService.TranslateAsync(source.Trim(), instruction),
            $"Translating into {language} via {_config.Provider}…",
            $"Translated into {language} via {_config.Provider}.");
    }

    private void InitializeLanguageSelection()
    {
        // Liste absente ou vidée à la main dans appsettings.json : on repart des
        // langues par défaut plutôt que d'afficher une liste inutilisable.
        _config.TranslationLanguages ??= new();
        if (_config.TranslationLanguages.Count == 0)
        {
            _config.TranslationLanguages.AddRange(new AppConfig().TranslationLanguages);
        }

        foreach (var language in _config.TranslationLanguages)
        {
            LanguageComboBox.Items.Add(language);
        }

        var index = _config.TranslationLanguages.FindIndex(
            l => string.Equals(l, _config.TranslationLanguage, StringComparison.OrdinalIgnoreCase));
        LanguageComboBox.SelectedIndex = Math.Max(index, 0);

        // Référence vivante : les langues ajoutées disparaissent des suggestions.
        AddLanguageBox.ExcludedLanguages = _config.TranslationLanguages;
        UpdateRemoveLanguageButton();
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        // Pendant une suppression, SelectedItem passe par null : on ignore.
        if (LanguageComboBox.SelectedItem is not string selected) return;

        _config.TranslationLanguage = selected;
        _configService.Save(_config);
    }

    private void AddLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        // Le bouton bascule : un second clic referme la zone de saisie.
        if (AddLanguageBox.Visibility == Visibility.Visible)
        {
            HideAddLanguageBox();
            return;
        }

        LanguageComboBox.Visibility = Visibility.Collapsed;
        AddLanguageBox.Visibility = Visibility.Visible;
        AddLanguageBox.FocusInput();
    }

    private void AddLanguageBox_LanguageSubmitted(object? sender, string language)
    {
        var existing = _config.TranslationLanguages.FirstOrDefault(
            l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _config.TranslationLanguages.Add(language);
            LanguageComboBox.Items.Add(language);
            LanguageComboBox.SelectedItem = language;
            StatusTextBlock.Text = _configService.Save(_config)
                ? $"{language} added to the list."
                : $"{language} added, but it will not be kept after a restart.";
        }
        else
        {
            LanguageComboBox.SelectedItem = existing;
            StatusTextBlock.Text = $"{existing} is already in the list.";
        }

        HideAddLanguageBox();
        UpdateRemoveLanguageButton();

        // Enchaînement au clavier : Entrée pour ajouter, Entrée pour traduire.
        TranslateButton.Focus();
    }

    private void AddLanguageBox_Cancelled(object? sender, EventArgs e)
    {
        HideAddLanguageBox();
        AddLanguageButton.Focus();
    }

    private void HideAddLanguageBox()
    {
        AddLanguageBox.Reset();
        AddLanguageBox.Visibility = Visibility.Collapsed;
        LanguageComboBox.Visibility = Visibility.Visible;
    }

    private void RemoveLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not string selected || _config.TranslationLanguages.Count <= 1) return;

        var index = LanguageComboBox.SelectedIndex;
        _config.TranslationLanguages.RemoveAll(l => string.Equals(l, selected, StringComparison.OrdinalIgnoreCase));
        LanguageComboBox.Items.Remove(selected);
        LanguageComboBox.SelectedIndex = Math.Min(index, LanguageComboBox.Items.Count - 1);

        StatusTextBlock.Text = _configService.Save(_config)
            ? $"{selected} removed from the list."
            : $"{selected} removed, but it will be back after a restart.";
        UpdateRemoveLanguageButton();
    }

    // La dernière langue ne peut pas être supprimée : la traduction n'aurait plus de cible.
    private void UpdateRemoveLanguageButton() =>
        RemoveLanguageButton.IsEnabled = _config.TranslationLanguages.Count > 1;

    private void SetBusy(bool busy, string status)
    {
        StrictCorrectionButton.IsEnabled = !busy;
        RewriteLightButton.IsEnabled = !busy;
        ProfessionalButton.IsEnabled = !busy;
        CustomButton.IsEnabled = !busy;
        TranslateButton.IsEnabled = !busy;
        PasteClipboardButton.IsEnabled = !busy;
        CopyResultButton.IsEnabled = !busy;
        PasteBackButton.IsEnabled = !busy;
        ClearInputButton.IsEnabled = !busy;
        ClearOutputButton.IsEnabled = !busy;
        AutoCopyCheckBox.IsEnabled = !busy;
        AsciiOnlyCheckBox.IsEnabled = !busy;
        AutoPasteBackCheckBox.IsEnabled = !busy;
        ProviderComboBox.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
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
