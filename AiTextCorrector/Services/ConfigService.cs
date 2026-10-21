using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AiTextCorrector.Localization;
using AiTextCorrector.Models;

namespace AiTextCorrector.Services;

public class ConfigService
{
    private const string FileName = "appsettings.json";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Dossier de configuration inscriptible, propre à l'utilisateur.
    /// Un chemin relatif ne convient pas : lancée depuis HKCU\...\Run, l'application
    /// démarre avec C:\Windows\System32 comme répertoire courant, et le dossier
    /// d'installation (Program Files) n'est de toute façon pas inscriptible.
    /// </summary>
    internal static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AiTextCorrector");

    private static string ConfigPath => Path.Combine(ConfigDirectory, FileName);

    /// <summary>
    /// Ancien emplacement (à côté de l'exécutable). Lu une seule fois, pour récupérer
    /// la configuration existante lors de la première exécution après mise à jour.
    /// </summary>
    private static string? LegacyConfigPath
    {
        get
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            return string.IsNullOrEmpty(exeDir) ? null : Path.Combine(exeDir, FileName);
        }
    }

    /// <summary>
    /// Modèles Groq retirés (https://console.groq.com/docs/deprecations) et leur
    /// remplaçant. Le nom du modèle est matérialisé dans appsettings.json : sans
    /// cette migration, une config existante continuerait d'appeler un modèle coupé
    /// (l'appel échoue alors que la clé est valide).
    /// </summary>
    private static readonly Dictionary<string, string> RetiredGroqModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["llama-3.3-70b-versatile"] = "openai/gpt-oss-120b",
        ["llama-3.1-8b-instant"] = "openai/gpt-oss-20b",
        ["qwen/qwen3-32b"] = "openai/gpt-oss-120b",
        ["qwen/qwen3.6-27b"] = "openai/gpt-oss-120b",
        ["meta-llama/llama-4-scout-17b-16e-instruct"] = "openai/gpt-oss-120b",
        ["meta-llama/llama-4-maverick-17b-128e-instruct"] = "openai/gpt-oss-120b",
        ["moonshotai/kimi-k2-instruct-0905"] = "openai/gpt-oss-120b",
    };

    public AppConfig Load()
    {
        var config = ReadFrom(ConfigPath) ?? ReadFrom(LegacyConfigPath) ?? new AppConfig();

        if (RetiredGroqModels.TryGetValue(config.GroqModel ?? string.Empty, out var replacement))
        {
            config.GroqModel = replacement;
        }

        UpgradeLegacyDefaultPrompts(config.Prompts ??= new PromptSet());

        if (string.IsNullOrWhiteSpace(config.UiLanguage))
        {
            config.UiLanguage = Localizer.SystemDefaultLanguage;
        }

        // Matérialise la configuration (et la migration depuis l'ancien emplacement)
        // dans le dossier utilisateur. Un échec n'est pas bloquant : on continue en
        // mémoire plutôt que d'empêcher le démarrage.
        Save(config);
        return config;
    }

    /// <summary>
    /// Langue de l'interface enregistrée, ou null. Lecture seule, contrairement à
    /// <see cref="Load"/> : l'installeur s'en sert avant que rien ne soit écrit sur le poste.
    /// </summary>
    public static string? PeekUiLanguage() => (ReadFrom(ConfigPath) ?? ReadFrom(LegacyConfigPath))?.UiLanguage;

    /// <summary>
    /// Anciens prompts par défaut, en français (avec ou sans la mention « Aucun '—' »
    /// du appsettings.json du projet). Un prompt resté identique à l'un d'eux n'a
    /// jamais été personnalisé : il reçoit le nouveau prompt par défaut, en anglais.
    /// Un prompt modifié par l'utilisateur est conservé tel quel.
    /// </summary>
    private static void UpgradeLegacyDefaultPrompts(PromptSet prompts)
    {
        var defaults = new PromptSet();

        prompts.StrictCorrection = Upgrade(prompts.StrictCorrection, defaults.StrictCorrection,
            "Corrige uniquement l'orthographe, la grammaire, la ponctuation et les accords. Ne reformule pas. Conserve strictement le sens, le ton, les listes, les retours ligne et les noms propres. Retourne uniquement le texte corrigé, sans commentaire ni guillemets.",
            "Corrige uniquement l'orthographe, la grammaire, la ponctuation et les accords. Ne reformule pas. Conserve strictement le sens, le ton, les listes, les retours ligne et les noms propres. Retourne uniquement le texte corrigé, sans commentaire ni guillemets, Aucun '—'.");

        prompts.CorrectionAndRewrite = Upgrade(prompts.CorrectionAndRewrite, defaults.CorrectionAndRewrite,
            "Corrige l'orthographe, la grammaire et la ponctuation puis reformule légèrement pour rendre le texte plus fluide et naturel. Conserve le sens d'origine. Retourne uniquement le texte final, sans commentaire ni guillemets.",
            "Corrige l'orthographe, la grammaire et la ponctuation puis reformule légèrement pour rendre le texte plus fluide et naturel. Conserve le sens d'origine. Retourne uniquement le texte final, sans commentaire ni guillemets, Aucun '—'.");

        prompts.ProfessionalStyle = Upgrade(prompts.ProfessionalStyle, defaults.ProfessionalStyle,
            "Corrige puis reformule dans un style professionnel, clair, direct et concis. Conserve les informations importantes et la langue d'origine. Retourne uniquement le texte final, sans commentaire ni guillemets.",
            "Corrige puis reformule dans un style professionnel, clair, direct et concis. Conserve les informations importantes et la langue d'origine. Retourne uniquement le texte final, sans commentaire ni guillemets, Aucun '—'.");

        prompts.CustomPrompt = Upgrade(prompts.CustomPrompt, defaults.CustomPrompt,
            "Corrige ce texte en conservant sa langue d'origine. Retourne uniquement le texte final.",
            "Corrige ce texte en conservant sa langue d'origine. Retourne uniquement le texte final, Aucun '—'.");

        prompts.Translation = Upgrade(prompts.Translation, defaults.Translation,
            "Traduis ce texte vers la langue suivante : {language}. Conserve le sens, le ton, la mise en forme (listes, retours ligne) et les noms propres. Retourne uniquement la traduction, sans commentaire ni guillemets, Aucun '—'.");
    }

    // Prompt absent ou égal à un ancien défaut (espaces de bord ignorés) : nouveau défaut.
    private static string Upgrade(string? current, string newDefault, params string[] legacyDefaults) =>
        string.IsNullOrWhiteSpace(current) || legacyDefaults.Contains(current.Trim(), StringComparer.Ordinal)
            ? newDefault
            : current;

    private static AppConfig? ReadFrom(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), ReadOptions);
        }
        catch
        {
            // Fichier illisible ou JSON corrompu : on repart des valeurs par défaut
            // plutôt que d'empêcher l'application de démarrer.
            return null;
        }
    }

    /// <summary>Enregistre la configuration. Renvoie false si l'écriture a échoué.</summary>
    public bool Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);

            // Écriture atomique : un arrêt brutal en cours d'écriture ne peut pas
            // laisser un appsettings.json tronqué qui bloquerait le démarrage suivant.
            var temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, WriteOptions));
            File.Move(temp, ConfigPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
