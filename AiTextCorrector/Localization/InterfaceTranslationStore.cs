using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiTextCorrector.Services;

namespace AiTextCorrector.Localization;

/// <summary>Une phrase traduite, avec le texte anglais dont elle provient.</summary>
public sealed class TranslatedPhrase
{
    // Si le texte anglais change dans une version ultérieure, la traduction est
    // périmée : la phrase repasse en anglais jusqu'à sa retraduction.
    public string Source { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>Traduction de l'interface dans une langue, telle qu'enregistrée sur le disque.</summary>
public sealed class InterfaceTranslation
{
    private static readonly Regex Placeholder = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    public string Language { get; set; } = string.Empty;

    // Clé de EnglishStrings → phrase traduite.
    public Dictionary<string, TranslatedPhrase> Phrases { get; set; } = new();

    /// <summary>Phrases utilisables : traduites depuis le texte anglais actuel.</summary>
    public Dictionary<string, string> UsableStrings() => EnglishStrings.All
        .Where(p => IsUpToDate(p.Key, p.Value))
        .ToDictionary(p => p.Key, p => Phrases[p.Key].Text);

    /// <summary>Phrases anglaises encore à traduire : absentes, périmées ou invalides.</summary>
    public Dictionary<string, string> MissingPhrases() => EnglishStrings.All
        .Where(p => !IsUpToDate(p.Key, p.Value))
        .ToDictionary(p => p.Key, p => p.Value);

    private bool IsUpToDate(string key, string source) =>
        // Entrée « null » possible dans un fichier retouché à la main.
        Phrases.TryGetValue(key, out var phrase) && phrase is not null &&
        string.Equals(phrase.Source, source, StringComparison.Ordinal) &&
        IsValidTranslation(source, phrase.Text);

    /// <summary>
    /// Vrai si <paramref name="text"/> peut remplacer <paramref name="source"/> : non
    /// vide, et avec exactement les mêmes trous ({0}, {language}…), sans quoi
    /// l'affichage perdrait une valeur ou lèverait une exception.
    /// </summary>
    public static bool IsValidTranslation(string source, string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        Placeholders(source).SequenceEqual(Placeholders(text), StringComparer.Ordinal);

    private static IEnumerable<string> Placeholders(string text) =>
        Placeholder.Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal);
}

/// <summary>
/// Traductions de l'interface faites par l'IA, une par fichier JSON dans
/// %APPDATA%\AiTextCorrector\Languages. Les fichiers restent lisibles et
/// modifiables à la main ; les supprimer avec les données utilisateur suffit à
/// tout effacer.
/// </summary>
public static class InterfaceTranslationStore
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    // Caractères non ASCII écrits tels quels : « Einfügen » plutôt que « Einfügen ».
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static string FolderPath => Path.Combine(ConfigService.ConfigDirectory, "Languages");

    /// <summary>Langues déjà traduites sur ce poste, hors langues intégrées.</summary>
    public static IReadOnlyList<string> SavedLanguages()
    {
        if (!Directory.Exists(FolderPath))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(FolderPath, "*.json")
            .Select(Read)
            .Where(t => t is not null && t.Language.Length > 0 && !Localizer.IsBuiltIn(t.Language))
            .Select(t => t!.Language)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Traduction enregistrée pour <paramref name="language"/>, ou null.</summary>
    public static InterfaceTranslation? Load(string language)
    {
        var translation = Read(PathFor(language));
        if (translation is not null && translation.Language.Length == 0)
        {
            translation.Language = language;
        }

        return translation;
    }

    /// <summary>Enregistre la traduction. Renvoie false si l'écriture a échoué.</summary>
    public static bool Save(InterfaceTranslation translation)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);

            // Écriture atomique, comme pour appsettings.json.
            var path = PathFor(translation.Language);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(translation, WriteOptions));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Nom anglais de la langue, débarrassé des caractères interdits dans un nom de
    // fichier (saisie libre).
    private static string PathFor(string language)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(language.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(FolderPath, name + ".json");
    }

    private static InterfaceTranslation? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var translation = JsonSerializer.Deserialize<InterfaceTranslation>(File.ReadAllText(path), ReadOptions);
            if (translation is null) return null;

            translation.Language = translation.Language?.Trim() ?? string.Empty;
            translation.Phrases ??= new();
            return translation;
        }
        catch
        {
            // Fichier illisible ou JSON corrompu : la langue est ignorée, l'interface
            // reste en anglais.
            return null;
        }
    }
}
