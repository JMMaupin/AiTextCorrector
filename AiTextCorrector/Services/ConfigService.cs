using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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

        // Matérialise la configuration (et la migration depuis l'ancien emplacement)
        // dans le dossier utilisateur. Un échec n'est pas bloquant : on continue en
        // mémoire plutôt que d'empêcher le démarrage.
        Save(config);
        return config;
    }

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
