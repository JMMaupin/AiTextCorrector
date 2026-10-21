using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace AiTextCorrector.Localization;

/// <summary>
/// Langue de l'interface. L'anglais est la référence et le français est intégré ;
/// toute autre langue vient d'une traduction faite une fois par l'IA puis
/// enregistrée sur le poste (<see cref="InterfaceTranslationStore"/>). Une phrase
/// absente ou périmée s'affiche en anglais : l'interface n'est jamais vide.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public const string English = "English";
    public const string French = "French";

    public static Localizer Instance { get; } = new();

    private IReadOnlyDictionary<string, string> _strings = EnglishStrings.All;

    private Localizer()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Langues toujours proposées, sans appel à l'IA.</summary>
    public static IReadOnlyList<string> BuiltInLanguages { get; } = new[] { English, French };

    /// <summary>Langue du premier lancement : celle de Windows si elle est intégrée, sinon l'anglais.</summary>
    public static string SystemDefaultLanguage =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? French : English;

    /// <summary>Langue effectivement appliquée (nom anglais, comme dans la configuration).</summary>
    public string Language { get; private set; } = English;

    // Indexeur lu par les liaisons de {loc:Text} : WPF le relit quand « Item[] » change.
    public string this[string key] =>
        _strings.TryGetValue(key, out var text) ? text
        : EnglishStrings.All.TryGetValue(key, out var native) ? native
        : key;

    public static bool IsBuiltIn(string language) =>
        BuiltInLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Applique <paramref name="language"/> à toute l'interface ouverte. Sans langue,
    /// celle de Windows ; sans traduction enregistrée, l'anglais.
    /// </summary>
    public void SetLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            language = SystemDefaultLanguage;
        }

        if (string.Equals(language, French, StringComparison.OrdinalIgnoreCase))
        {
            Apply(French, FrenchStrings.All);
        }
        else if (!string.Equals(language, English, StringComparison.OrdinalIgnoreCase) &&
                 InterfaceTranslationStore.Load(language) is { } saved)
        {
            Apply(saved.Language, saved.UsableStrings());
        }
        else
        {
            Apply(English, EnglishStrings.All);
        }
    }

    // Notification systématique, même à langue égale : une traduction complétée
    // entre-temps doit apparaître.
    private void Apply(string language, IReadOnlyDictionary<string, string> strings)
    {
        _strings = strings;
        Language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }
}

/// <summary>Textes de l'interface depuis le code.</summary>
public static class Loc
{
    public static string Text(string key) => Localizer.Instance[key];

    /// <summary>
    /// Texte à trous ({0}, {1}…). Une traduction aux trous abîmés (fichier retouché à
    /// la main) retombe sur l'anglais plutôt que de lever une exception.
    /// </summary>
    public static string Format(string key, params object?[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, Text(key), args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, EnglishStrings.All.GetValueOrDefault(key, key), args);
        }
    }
}
