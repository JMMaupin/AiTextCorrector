using System;
using System.Windows;
using AiTextCorrector.Models;
using AiTextCorrector.Services;

namespace AiTextCorrector.Settings;

/// <summary>
/// Réglages regroupés en onglets (raccourci, clés API, prompts), enregistrés
/// ensemble par un seul bouton : rien n'est écrit tant qu'un onglet est invalide.
/// </summary>
public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppConfig _config;
    private readonly ConfigService _configService;
    private readonly Func<HotkeyConfig, bool> _tryApplyHotkey;

    public SettingsWindow(AppConfig config, ConfigService configService, ApiKeyStore keyStore,
                          Func<HotkeyConfig, bool> tryApplyHotkey)
    {
        InitializeComponent();
        _config = config;
        _configService = configService;
        _tryApplyHotkey = tryApplyHotkey;

        HotkeyPanel.Load(config.Hotkey);
        KeysPanel.Load(keyStore);
        PromptPanel.Load(config.Prompts);
    }

    /// <summary>Faux si la configuration n'a pas pu être écrite sur le disque.</summary>
    public bool ConfigSaved { get; private set; }

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
        ConfigSaved = _configService.Save(_config);

        DialogResult = true;
    }
}
