using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AiTextCorrector.Services;

namespace AiTextCorrector;

public partial class ApiKeysWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ApiKeyStore _keyStore;

    // Providers dont l'utilisateur a explicitement demandé la suppression via le
    // bouton « Effacer ». C'est le seul chemin qui détruit une clé enregistrée.
    private readonly HashSet<string> _markedForRemoval = new(StringComparer.OrdinalIgnoreCase);

    public ApiKeysWindow(ApiKeyStore keyStore)
    {
        InitializeComponent();
        _keyStore = keyStore;

        // Les champs restent volontairement vides : on n'affiche jamais une clé
        // enregistrée, et un champ vide signifie « ne pas toucher ». L'ancienne
        // version pré-remplissait les champs puis les vidait au passage du focus,
        // si bien que tabuler dans la fenêtre suffisait à effacer les clés.
        RefreshStatuses();

        var warnings = new List<string>();
        if (_keyStore.OpenAiFromEnvironment)
        {
            warnings.Add("OPENAI_API_KEY");
        }
        if (_keyStore.AnthropicFromEnvironment)
        {
            warnings.Add("AITEXTCORRECTOR_ANTHROPIC_API_KEY");
        }
        if (_keyStore.GlmFromEnvironment)
        {
            warnings.Add("AITEXTCORRECTOR_GLM_API_KEY");
        }
        if (_keyStore.GroqFromEnvironment)
        {
            warnings.Add("AITEXTCORRECTOR_GROQ_API_KEY");
        }

        if (warnings.Count > 0)
        {
            ErrorTextBlock.Text =
                $"Note : la variable d'environnement {string.Join(" et ", warnings)} est définie et reste prioritaire à l'exécution tant qu'elle existe.";
            ErrorTextBlock.Visibility = Visibility.Visible;
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
        var hasKey = !string.IsNullOrWhiteSpace(storedKey);

        if (_markedForRemoval.Contains(provider))
        {
            status.Text = "— sera supprimée à l'enregistrement";
            clearButton.Content = "Rétablir";
            clearButton.IsEnabled = true;
            return;
        }

        status.Text = hasKey ? "— clé enregistrée" : "— aucune clé enregistrée";
        clearButton.Content = "Effacer";
        clearButton.IsEnabled = hasKey;
    }

    // La suppression n'est effective qu'à l'enregistrement, et reste réversible
    // tant que la fenêtre est ouverte (« Rétablir », ou « Annuler »).
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Apply("OpenAI", OpenAiKeyBox, _keyStore.SetOpenAiKey);
        Apply("Anthropic", AnthropicKeyBox, _keyStore.SetAnthropicKey);
        Apply("GLM", GlmKeyBox, _keyStore.SetGlmKey);
        Apply("Groq", GroqKeyBox, _keyStore.SetGroqKey);

        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Champ vide = clé existante conservée. Saisie = remplacement. Suppression
    /// uniquement si elle a été demandée explicitement.
    /// </summary>
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

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
