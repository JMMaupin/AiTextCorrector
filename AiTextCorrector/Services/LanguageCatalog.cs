using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AiTextCorrector.Services;

/// <summary>Une langue proposée à l'ajout : nom anglais (stocké) et nom natif (aide au choix).</summary>
public sealed class LanguageSuggestion
{
    public LanguageSuggestion(string englishName, string nativeName, string localName)
    {
        EnglishName = englishName;
        NativeName = nativeName;
        NormalizedEnglishName = LanguageCatalog.Normalize(englishName);
        NormalizedOtherNames = new[] { nativeName, localName }
            .Select(LanguageCatalog.Normalize)
            .Distinct()
            .ToArray();
    }

    public string EnglishName { get; }
    public string NativeName { get; }

    // Clés de recherche, normalisées une fois pour toutes au chargement du catalogue.
    internal string NormalizedEnglishName { get; }
    internal string[] NormalizedOtherNames { get; }

    // Texte affiché dans la liste : le nom natif n'est ajouté que s'il apporte
    // quelque chose (« German — Deutsch », mais « English » seul).
    public override string ToString() =>
        string.Equals(NormalizedEnglishName, LanguageCatalog.Normalize(NativeName), StringComparison.Ordinal)
            ? EnglishName
            : $"{EnglishName} — {NativeName}";
}

/// <summary>
/// Catalogue des langues proposées à l'ajout, tiré des cultures neutres connues de
/// Windows (environ 280 langues) : aucune liste à maintenir à la main.
///
/// Chaque langue se trouve par trois noms : anglais (« ger »), natif (« deu » pour
/// Deutsch) et dans la langue des formats régionaux de Windows (« all » pour
/// allemand, avec des formats fr-FR) — on cherche spontanément une langue par son
/// nom dans sa propre langue.
/// </summary>
public static class LanguageCatalog
{
    private static readonly Lazy<IReadOnlyList<LanguageSuggestion>> All = new(Build);

    private static readonly char[] WordSeparators = { ' ', '(', ')', '-', ',', '（', '）' };

    /// <summary>
    /// Langues dont l'un des noms, ou l'un de ses mots, commence par
    /// <paramref name="query"/>. Les noms anglais qui commencent par la saisie
    /// passent en tête. Les langues déjà présentes dans <paramref name="exclude"/>
    /// sont écartées.
    /// </summary>
    public static IReadOnlyList<LanguageSuggestion> Suggest(string query, IEnumerable<string> exclude, int max = 10)
    {
        var q = Normalize(query);
        if (q.Length == 0)
        {
            return Array.Empty<LanguageSuggestion>();
        }

        var excluded = new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase);

        return All.Value
            .Where(l => !excluded.Contains(l.EnglishName))
            .Select(l => (Language: l, Rank: Rank(l, q)))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Language.EnglishName, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Language)
            .ToList();
    }

    /// <summary>
    /// Nom anglais canonique si la saisie désigne exactement une langue connue
    /// (« spanish », « Español », « espagnol »…), sinon null.
    /// </summary>
    public static string? FindExact(string text)
    {
        var q = Normalize(text);
        return All.Value
            .FirstOrDefault(l => l.NormalizedEnglishName == q || l.NormalizedOtherNames.Contains(q))
            ?.EnglishName;
    }

    /// <summary>
    /// Libellé d'une langue pour une liste (« German — Deutsch ») : le nom natif aide
    /// à reconnaître la sienne. Le nom tel quel s'il est inconnu du catalogue.
    /// </summary>
    public static string Describe(string englishName)
    {
        var q = Normalize(englishName);
        return All.Value.FirstOrDefault(l => l.NormalizedEnglishName == q)?.ToString() ?? englishName;
    }

    // Minuscules sans accents, pour que « espa » trouve « español ».
    internal static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // 0 : le nom anglais commence par la saisie ; 1 : un autre nom ; 2 : un mot de
    // l'un des noms ; -1 : aucune correspondance.
    private static int Rank(LanguageSuggestion language, string q)
    {
        if (language.NormalizedEnglishName.StartsWith(q, StringComparison.Ordinal)) return 0;
        if (language.NormalizedOtherNames.Any(n => n.StartsWith(q, StringComparison.Ordinal))) return 1;

        var words = language.NormalizedOtherNames
            .Prepend(language.NormalizedEnglishName)
            .SelectMany(n => n.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries));
        return words.Any(w => w.StartsWith(q, StringComparison.Ordinal)) ? 2 : -1;
    }

    private static IReadOnlyList<LanguageSuggestion> Build()
    {
        var cultures = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(c => c.Name.Length > 0) // écarte la culture invariante
            .GroupBy(c => c.EnglishName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var localNames = GetDisplayNames(cultures, CultureInfo.CurrentCulture);

        return cultures
            .Select(c => new LanguageSuggestion(c.EnglishName, c.NativeName, localNames[c.Name]))
            .ToList();
    }

    // Noms des cultures dans la langue de <paramref name="displayCulture"/>. ICU ne
    // les fournit que dans la langue de la culture d'INTERFACE courante (DisplayName),
    // d'où la bascule temporaire, limitée à ce bloc.
    private static Dictionary<string, string> GetDisplayNames(IEnumerable<CultureInfo> cultures, CultureInfo displayCulture)
    {
        var saved = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = displayCulture;
            // Instances neuves : celles de GetCultures ont pu déjà mémoriser leur nom.
            return cultures.ToDictionary(c => c.Name, c => new CultureInfo(c.Name).DisplayName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = saved;
        }
    }
}
