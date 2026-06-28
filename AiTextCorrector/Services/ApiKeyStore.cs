using System;
using Microsoft.Win32;

namespace AiTextCorrector.Services;

/// <summary>
/// Stocke les clés API dans le registre utilisateur (HKCU\Software\AiTextCorrector).
/// Propre à l'utilisateur courant, aucun privilège administrateur requis, et évite
/// de laisser des clés en clair dans appsettings.json (versionné/partageable).
///
/// Une variable d'environnement, si elle est définie, reste prioritaire : elle
/// permet à un poste partagé ou à un script de surcharger la clé sans toucher au
/// registre.
/// </summary>
public class ApiKeyStore
{
    private const string KeyPath = @"Software\AiTextCorrector";

    private const string OpenAiValueName = "OpenAiApiKey";
    private const string AnthropicValueName = "AnthropicApiKey";
    private const string GlmValueName = "GlmApiKey";
    private const string GroqValueName = "GroqApiKey";

    private const string OpenAiEnvVar = "OPENAI_API_KEY";
    private const string AnthropicEnvVar = "AITEXTCORRECTOR_ANTHROPIC_API_KEY";
    private const string GlmEnvVar = "AITEXTCORRECTOR_GLM_API_KEY";
    private const string GroqEnvVar = "AITEXTCORRECTOR_GROQ_API_KEY";

    public string? GetOpenAiKey() => Resolve(OpenAiEnvVar, OpenAiValueName);

    public string? GetAnthropicKey() => Resolve(AnthropicEnvVar, AnthropicValueName);

    public string? GetGlmKey() => Resolve(GlmEnvVar, GlmValueName);

    public string? GetGroqKey() => Resolve(GroqEnvVar, GroqValueName);

    /// <summary>Renvoie la clé configurée (registre ou variable d'env) pour un provider donné.</summary>
    public string? GetKeyFor(string provider) => provider.ToUpperInvariant() switch
    {
        "OPENAI" => GetOpenAiKey(),
        "ANTHROPIC" => GetAnthropicKey(),
        "GLM" => GetGlmKey(),
        "GROQ" => GetGroqKey(),
        _ => null
    };

    /// <summary>Vrai si une clé est configurée pour ce provider.</summary>
    public bool HasKeyFor(string provider) => !string.IsNullOrWhiteSpace(GetKeyFor(provider));

    /// <summary>Lit la clé OpenAI uniquement depuis le registre (ignore la variable d'env).</summary>
    public string? GetOpenAiKeyFromRegistry() => ReadRegistry(OpenAiValueName);

    /// <summary>Lit la clé Anthropic uniquement depuis le registre (ignore la variable d'env).</summary>
    public string? GetAnthropicKeyFromRegistry() => ReadRegistry(AnthropicValueName);

    /// <summary>Lit la clé GLM uniquement depuis le registre (ignore la variable d'env).</summary>
    public string? GetGlmKeyFromRegistry() => ReadRegistry(GlmValueName);

    /// <summary>Lit la clé Groq uniquement depuis le registre (ignore la variable d'env).</summary>
    public string? GetGroqKeyFromRegistry() => ReadRegistry(GroqValueName);

    public void SetOpenAiKey(string? key) => Write(OpenAiValueName, key);

    public void SetAnthropicKey(string? key) => Write(AnthropicValueName, key);

    public void SetGlmKey(string? key) => Write(GlmValueName, key);

    public void SetGroqKey(string? key) => Write(GroqValueName, key);

    /// <summary>Vrai si la clé OpenAI provient d'une variable d'environnement (donc non modifiable depuis l'UI).</summary>
    public bool OpenAiFromEnvironment => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OpenAiEnvVar));

    /// <summary>Vrai si la clé Anthropic provient d'une variable d'environnement.</summary>
    public bool AnthropicFromEnvironment => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AnthropicEnvVar));

    /// <summary>Vrai si la clé GLM provient d'une variable d'environnement.</summary>
    public bool GlmFromEnvironment => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GlmEnvVar));

    /// <summary>Vrai si la clé Groq provient d'une variable d'environnement.</summary>
    public bool GroqFromEnvironment => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GroqEnvVar));

    private static string? Resolve(string envVar, string valueName)
    {
        var fromEnv = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        return ReadRegistry(valueName);
    }

    private static string? ReadRegistry(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static void Write(string valueName, string? value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(valueName, value.Trim(), RegistryValueKind.String);
            }
        }
        catch
        {
            // Écriture impossible (registre verrouillé…) : on n'interrompt pas l'appli.
        }
    }
}
