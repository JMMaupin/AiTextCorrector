namespace AiTextCorrector.Models;

public class AppConfig
{
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-4.1-mini";
    public string AnthropicModel { get; set; } = "claude-sonnet-4-6";
    public string GlmModel { get; set; } = "glm-4.6";
    public string GroqModel { get; set; } = "openai/gpt-oss-120b";

    // Langues cibles proposées pour la traduction, dans l'ordre d'affichage
    // (noms anglais, transmis tels quels au modèle).
    public List<string> TranslationLanguages { get; set; } = new() { "English", "Spanish" };

    // Dernière langue choisie, resélectionnée au démarrage.
    public string TranslationLanguage { get; set; } = "English";

    // Langue de l'interface (nom anglais). Vide au premier lancement : celle de
    // Windows si elle est intégrée, fixée au chargement de la configuration.
    public string? UiLanguage { get; set; }

    public HotkeyConfig Hotkey { get; set; } = new();
    public PromptSet Prompts { get; set; } = new();
}

public class HotkeyConfig
{
    public bool Ctrl { get; set; } = true;
    public bool Shift { get; set; } = true;
    public bool Alt { get; set; }
    public bool Win { get; set; }

    // Nom d'énumération System.Windows.Input.Key (ex. "Space", "F2", "OemPeriod").
    public string Key { get; set; } = "Space";

    public string Describe()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        parts.Add(string.IsNullOrWhiteSpace(Key) ? "?" : Key);
        return string.Join(" + ", parts);
    }
}

public class PromptSet
{
    // Consignes en anglais, mais la langue du texte est explicitement préservée :
    // sans cette mention, une consigne anglaise pousse le modèle à répondre en anglais.
    public string StrictCorrection { get; set; } = "Fix only spelling, grammar, punctuation and agreement errors. Do not rephrase. Strictly keep the meaning, tone, lists, line breaks and proper nouns. Keep the text in its original language. Return only the corrected text, with no comments or quotation marks. Do not use em dashes (—).";
    public string CorrectionAndRewrite { get; set; } = "Fix spelling, grammar and punctuation, then lightly rephrase the text to make it smoother and more natural. Keep the original meaning and the original language. Return only the final text, with no comments or quotation marks. Do not use em dashes (—).";
    public string ProfessionalStyle { get; set; } = "Correct the text, then rewrite it in a professional, clear, direct and concise style. Keep the important information and the original language. Return only the final text, with no comments or quotation marks. Do not use em dashes (—).";
    public string CustomPrompt { get; set; } = "Correct this text and keep it in its original language. Return only the final text. Do not use em dashes (—).";

    // {language} est remplacé par la langue cible choisie dans l'interface.
    public string Translation { get; set; } = "Translate this text into {language}. Keep the meaning, tone, formatting (lists, line breaks) and proper nouns. Return only the translation, with no comments or quotation marks. Do not use em dashes (—).";
}
