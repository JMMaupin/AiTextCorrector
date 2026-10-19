using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AiTextCorrector.Models;

namespace AiTextCorrector.Services;

/// <summary>
/// Rattache chaque modification trouvée par <see cref="TextDiff"/> à l'explication
/// fournie par le modèle. Le modèle cite parfois un extrait plus large que la seule
/// faute (« les document » pour « les ») : l'appariement tolère l'inclusion.
/// </summary>
public sealed class ExplanationMatcher
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private readonly IReadOnlyList<ErrorExplanation> _errors;
    private readonly bool[] _used;

    public ExplanationMatcher(IReadOnlyList<ErrorExplanation> errors)
    {
        _errors = errors;
        _used = new bool[errors.Count];
    }

    /// <summary>Explication de cette modification, ou null si aucune ne lui correspond.</summary>
    public ErrorExplanation? Find(DiffSegment change)
    {
        var before = Normalize(change.Original);
        var after = Normalize(change.Corrected);

        // Les explications pas encore attribuées d'abord, en suivant l'ordre du texte.
        // Une explication déjà attribuée peut resservir si elle couvre clairement cette
        // modification aussi (une faute qui touche deux mots voisins).
        var best = BestMatch(before, after, used: false, minimumScore: 1)
                   ?? BestMatch(before, after, used: true, minimumScore: 2);
        if (best is null) return null;

        _used[best.Value] = true;
        return _errors[best.Value];
    }

    private int? BestMatch(string before, string after, bool used, int minimumScore)
    {
        int? best = null;
        var bestScore = minimumScore - 1;

        for (var i = 0; i < _errors.Count; i++)
        {
            if (_used[i] != used) continue;

            var score = Score(_errors[i], before, after);
            if (score > bestScore)
            {
                best = i;
                bestScore = score;
            }
        }

        return best;
    }

    // 3 : avant et après identiques ; 2 : les deux contenus dans l'explication ;
    // 1 : un seul des deux ; 0 : sans rapport.
    private static int Score(ErrorExplanation error, string before, string after)
    {
        var original = Normalize(error.Original);
        var correction = Normalize(error.Correction);

        if (string.Equals(original, before, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(correction, after, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        var beforeFound = before.Length > 0 && original.Contains(before, StringComparison.OrdinalIgnoreCase);
        var afterFound = after.Length > 0 && correction.Contains(after, StringComparison.OrdinalIgnoreCase);

        // Côté vide (mot ajouté ou supprimé) : il ne peut pas servir de preuve, l'autre côté décide.
        if ((beforeFound || before.Length == 0) && (afterFound || after.Length == 0)) return 2;
        return beforeFound || afterFound ? 1 : 0;
    }

    private static string Normalize(string text) =>
        Spaces.Replace(text.Replace('’', '\''), " ").Trim();
}
