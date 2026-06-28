namespace AiTextCorrector.Models;

public class AppConfig
{
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-4.1-mini";
    public string AnthropicModel { get; set; } = "claude-sonnet-4-6";
    public string GlmModel { get; set; } = "glm-4.6";
    public string GroqModel { get; set; } = "llama-3.3-70b-versatile";
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
    public string StrictCorrection { get; set; } = "Corrige uniquement l'orthographe, la grammaire, la ponctuation et les accords. Ne reformule pas. Conserve strictement le sens, le ton, les listes, les retours ligne et les noms propres. Retourne uniquement le texte corrigé, sans commentaire ni guillemets.";
    public string CorrectionAndRewrite { get; set; } = "Corrige l'orthographe, la grammaire et la ponctuation puis reformule légèrement pour rendre le texte plus fluide et naturel. Conserve le sens d'origine. Retourne uniquement le texte final, sans commentaire ni guillemets.";
    public string ProfessionalStyle { get; set; } = "Corrige puis reformule dans un style professionnel, clair, direct et concis. Conserve les informations importantes et la langue d'origine. Retourne uniquement le texte final, sans commentaire ni guillemets.";
    public string CustomPrompt { get; set; } = "Corrige ce texte en conservant sa langue d'origine. Retourne uniquement le texte final.";
}
