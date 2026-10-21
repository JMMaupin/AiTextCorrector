using System;
using System.Windows;
using AiTextCorrector.Localization;
using AiTextCorrector.Models;
using AiTextCorrector.Services;

namespace AiTextCorrector.Settings;

/// <summary>
/// Réglages regroupés en onglets (raccourci, clés API, prompts, langue), enregistrés
/// ensemble par un seul bouton : rien n'est écrit tant qu'un onglet est invalide.
/// Seule exception, une traduction de l'interface est enregistrée dès qu'elle est
/// faite : elle a coûté un appel à l'IA, et ne change rien tant qu'elle n'est pas choisie.
/// </summary>
public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppConfig _config;
    private readonly ConfigService _configService;
    private readonly Func<HotkeyConfig, bool> _tryApplyHotkey;

    public SettingsWindow(AppConfig config, ConfigService configService, ApiKeyStore keyStore,
                          MultiProviderCorrectionService aiService, Func<HotkeyConfig, bool> tryApplyHotkey)
    {
        InitializeComponent();
        _config = config;
        _configService = configService;
        _tryApplyHotkey = tryApplyHotkey;

        HotkeyPanel.Load(config.Hotkey);
        KeysPanel.Load(keyStore);
        PromptPanel.Load(config.Prompts);
        LanguagePanel.Load(config, keyStore, aiService);
    }

    /// <summary>Faux si la configuration n'a pas pu être écrite sur le disque.</summary>
    public bool ConfigSaved { get; private set; }

    // Pas d'enregistrement pendant une traduction : la langue choisie serait celle
    // d'avant, et la nouvelle n'apparaîtrait qu'à la prochaine ouverture.
    private void LanguagePanel_BusyChanged(object? sender, EventArgs e) =>
        SaveButton.IsEnabled = !LanguagePanel.IsBusy;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Contrôles d'abord, écritures ensuite : on montre l'onglet fautif.
        if (!HotkeyPanel.IsValid)
        {
            Tabs.SelectedItem = HotkeyTab;
            return;
        }

        if (!PromptPanel.Validate())
        {
            Tabs.SelectedItem = PromptsTab;
            return;
        }

        // Le raccourci en premier : c'est la seule opération qui peut encore échouer.
        if (HotkeyPanel.IsChanged && !_tryApplyHotkey(HotkeyPanel.Selected))
        {
            HotkeyPanel.MarkUnavailable();
            Tabs.SelectedItem = HotkeyTab;
            return;
        }

        KeysPanel.Apply();
        PromptPanel.StoreIn(_config.Prompts);
        _config.UiLanguage = LanguagePanel.Selected;
        ConfigSaved = _configService.Save(_config);

        // Toujours réappliquée : une traduction complétée dans l'onglet doit apparaître
        // même si la langue n'a pas changé.
        Localizer.Instance.SetLanguage(_config.UiLanguage);

        DialogResult = true;
    }
}
