using System.Windows;
using AiTextCorrector.Services;

namespace AiTextCorrector;

public partial class ApiKeysWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ApiKeyStore _keyStore;

    // Vrai si le champ contenait déjà une clé à l'ouverture. Sert à ne vider
    // qu'une fois au premier focus, et à protéger contre un effacement accidentel.
    private bool _openAiHadKey;
    private bool _anthropicHadKey;
    private bool _openAiCleared;
    private bool _anthropicCleared;

    public ApiKeysWindow(ApiKeyStore keyStore)
    {
        InitializeComponent();
        _keyStore = keyStore;

        // Affiche les clés enregistrées dans le registre (pas la valeur d'une
        // éventuelle variable d'environnement : on ne veut pas exposer ni écraser
        // celle-ci, et le champ édite bien la clé du registre).
        OpenAiKeyBox.Password = _keyStore.GetOpenAiKeyFromRegistry() ?? string.Empty;
        AnthropicKeyBox.Password = _keyStore.GetAnthropicKeyFromRegistry() ?? string.Empty;

        _openAiHadKey = OpenAiKeyBox.Password.Length > 0;
        _anthropicHadKey = AnthropicKeyBox.Password.Length > 0;

        // Les champs restent toujours éditables. Si une variable d'environnement
        // est active, elle reste prioritaire à l'exécution : on en avertit
        // simplement l'utilisateur.
        var warnings = new System.Collections.Generic.List<string>();
        if (_keyStore.OpenAiFromEnvironment)
        {
            warnings.Add("OPENAI_API_KEY");
        }
        if (_keyStore.AnthropicFromEnvironment)
        {
            warnings.Add("AITEXTCORRECTOR_ANTHROPIC_API_KEY");
        }

        if (warnings.Count > 0)
        {
            ErrorTextBlock.Text =
                $"Note : la variable d'environnement {string.Join(" et ", warnings)} est définie et reste prioritaire à l'exécution tant qu'elle existe.";
            ErrorTextBlock.Visibility = Visibility.Visible;
        }
    }

    // Au premier focus, on vide le champ pour saisir directement la nouvelle clé
    // (plutôt que d'avoir à effacer manuellement les étoiles).
    private void OpenAiKeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_openAiCleared && _openAiHadKey)
        {
            OpenAiKeyBox.Clear();
            _openAiCleared = true;
        }
    }

    private void AnthropicKeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_anthropicCleared && _anthropicHadKey)
        {
            AnthropicKeyBox.Clear();
            _anthropicCleared = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Règle simple et prévisible : on enregistre un champ uniquement si
        // l'utilisateur l'a touché (focus). Un champ jamais cliqué conserve donc
        // sa clé d'origine ; un champ cliqué enregistre exactement son contenu
        // actuel (vide compris, pour permettre la suppression volontaire).
        if (_openAiCleared || !_openAiHadKey)
        {
            _keyStore.SetOpenAiKey(OpenAiKeyBox.Password);
        }

        if (_anthropicCleared || !_anthropicHadKey)
        {
            _keyStore.SetAnthropicKey(AnthropicKeyBox.Password);
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
