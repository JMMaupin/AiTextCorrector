using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AiTextCorrector.Services;

namespace AiTextCorrector.Settings;

/// <summary>Onglet « API keys » : saisie, remplacement et suppression des clés.</summary>
public partial class ApiKeysPanel : UserControl
{
    private ApiKeyStore _keyStore = new();

    // Providers dont l'utilisateur a explicitement demandé la suppression via le
    // bouton « Clear ». C'est le seul chemin qui détruit une clé enregistrée.
    private readonly HashSet<string> _markedForRemoval = new(StringComparer.OrdinalIgnoreCase);

    public ApiKeysPanel()
    {
        InitializeComponent();
    }

    public void Load(ApiKeyStore keyStore)
    {
        _keyStore = keyStore;

        // Les champs restent volontairement vides : on n'affiche jamais une clé
        // enregistrée, et un champ vide signifie « ne pas toucher ». Une ancienne
        // version pré-remplissait les champs puis les vidait au passage du focus,
        // si bien que tabuler dans la fenêtre suffisait à effacer les clés.
        RefreshStatuses();

        var overridden = new List<string>();
        if (_keyStore.OpenAiFromEnvironment) overridden.Add("OPENAI_API_KEY");
        if (_keyStore.AnthropicFromEnvironment) overridden.Add("AITEXTCORRECTOR_ANTHROPIC_API_KEY");
        if (_keyStore.GlmFromEnvironment) overridden.Add("AITEXTCORRECTOR_GLM_API_KEY");
        if (_keyStore.GroqFromEnvironment) overridden.Add("AITEXTCORRECTOR_GROQ_API_KEY");

        if (overridden.Count > 0)
        {
            EnvironmentNoteText.Text =
                $"Note: the environment variable {string.Join(" and ", overridden)} is defined and takes precedence as long as it exists.";
            EnvironmentNoteText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Champ vide = clé existante conservée. Saisie = remplacement. Suppression
    /// uniquement si elle a été demandée explicitement.
    /// </summary>
    public void Apply()
    {
        Apply("OpenAI", OpenAiKeyBox, _keyStore.SetOpenAiKey);
        Apply("Anthropic", AnthropicKeyBox, _keyStore.SetAnthropicKey);
        Apply("GLM", GlmKeyBox, _keyStore.SetGlmKey);
        Apply("Groq", GroqKeyBox, _keyStore.SetGroqKey);
    }

    private void Apply(string provider, PasswordBox box, Action<string?> setter)
    {
        if (_markedForRemoval.Contains(provider))
        {
            setter(null);
            return;
        }

        var typed = box.Password;
        if (!string.IsNullOrWhiteSpace(typed))
        {
            setter(typed);
        }
    }

    private void RefreshStatuses()
    {
        SetStatus("OpenAI", OpenAiStatusText, OpenAiClearButton, _keyStore.GetOpenAiKeyFromRegistry());
        SetStatus("Anthropic", AnthropicStatusText, AnthropicClearButton, _keyStore.GetAnthropicKeyFromRegistry());
        SetStatus("GLM", GlmStatusText, GlmClearButton, _keyStore.GetGlmKeyFromRegistry());
        SetStatus("Groq", GroqStatusText, GroqClearButton, _keyStore.GetGroqKeyFromRegistry());
    }

    private void SetStatus(string provider, Wpf.Ui.Controls.TextBlock status,
                           Wpf.Ui.Controls.Button clearButton, string? storedKey)
    {
        if (_markedForRemoval.Contains(provider))
        {
            status.Text = "— will be deleted on save";
            clearButton.Content = "Restore";
            clearButton.IsEnabled = true;
            return;
        }

        var hasKey = !string.IsNullOrWhiteSpace(storedKey);
        status.Text = hasKey ? "— key saved" : "— no key saved";
        clearButton.Content = "Clear";
        clearButton.IsEnabled = hasKey;
    }

    // La suppression n'est effective qu'à l'enregistrement, et reste réversible
    // tant que la fenêtre est ouverte (« Restore », ou « Cancel »).
    private void ToggleRemoval(string provider, PasswordBox box)
    {
        if (!_markedForRemoval.Add(provider))
        {
            _markedForRemoval.Remove(provider);
        }
        else
        {
            box.Clear();
        }

        RefreshStatuses();
    }

    private void OpenAiClear_Click(object sender, RoutedEventArgs e) => ToggleRemoval("OpenAI", OpenAiKeyBox);

    private void AnthropicClear_Click(object sender, RoutedEventArgs e) => ToggleRemoval("Anthropic", AnthropicKeyBox);

    private void GlmClear_Click(object sender, RoutedEventArgs e) => ToggleRemoval("GLM", GlmKeyBox);

    private void GroqClear_Click(object sender, RoutedEventArgs e) => ToggleRemoval("Groq", GroqKeyBox);
}
