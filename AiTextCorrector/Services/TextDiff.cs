using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AiTextCorrector.Services;

/// <summary>Portion de texte issue de la comparaison : inchangée, ou modifiée (avant → après).</summary>
public sealed record DiffSegment(string Original, string Corrected, bool IsChange);

/// <summary>
/// Compare un texte et sa version corrigée, mot à mot.
///
/// Les positions des fautes sont calculées ici plutôt que demandées au modèle :
/// les modèles de langue se trompent souvent sur les positions en caractères,
/// alors que la comparaison donne des emplacements exacts.
/// </summary>
public static class TextDiff
{
    // Au-delà, les deux textes sont trop différents pour une annotation lisible
    // (et la mémoire du calcul croît avec le carré de ce nombre).
    private const int MaxEdits = 600;

    // Un mot (apostrophes et traits d'union internes compris : « m'avez »,
    // « aujourd'hui », « peut-être »), une suite d'espaces, ou un autre caractère.
    private static readonly Regex TokenPattern = new(
        @"[\p{L}\p{M}\p{N}]+(?:['’\-][\p{L}\p{M}\p{N}]+)*|\s+|.",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Segments du texte d'origine, dans l'ordre, chacun inchangé ou modifié. Les
    /// changements qui ne touchent que les espaces sont tenus pour inchangés.
    /// Renvoie null si les textes diffèrent trop.
    /// </summary>
    public static IReadOnlyList<DiffSegment>? Compare(string original, string corrected)
    {
        var a = Tokenize(original);
        var b = Tokenize(corrected);
        var ops = ShortestEditScript(a, b);
        if (ops is null) return null;

        var segments = new List<DiffSegment>();
        var equal = new StringBuilder();
        var removed = new StringBuilder();
        var inserted = new StringBuilder();

        void FlushEqual()
        {
            if (equal.Length == 0) return;
            segments.Add(new DiffSegment(equal.ToString(), equal.ToString(), false));
            equal.Clear();
        }

        void FlushChange()
        {
            if (removed.Length == 0 && inserted.Length == 0) return;
            var before = removed.ToString();
            var after = inserted.ToString();
            removed.Clear();
            inserted.Clear();

            // Espaces seuls (fins de ligne, double espace…) : rien à apprendre, on
            // affiche simplement le texte d'origine.
            if (string.IsNullOrWhiteSpace(before) && string.IsNullOrWhiteSpace(after))
            {
                equal.Append(before);
                return;
            }

            segments.Add(new DiffSegment(before, after, true));
        }

        foreach (var (op, index) in ops)
        {
            switch (op)
            {
                case EditOp.Equal:
                    FlushChange();
                    equal.Append(a[index]);
                    break;
                case EditOp.Delete:
                    FlushEqual();
                    removed.Append(a[index]);
                    break;
                case EditOp.Insert:
                    FlushEqual();
                    inserted.Append(b[index]);
                    break;
            }
        }

        FlushChange();
        FlushEqual();
        return segments;
    }

    private static string[] Tokenize(string text) =>
        TokenPattern.Matches(text).Select(m => m.Value).ToArray();

    // Apostrophe droite ou typographique : même mot. Sans cela, un modèle qui
    // remplace ' par ’ ferait apparaître chaque élision comme une faute.
    private static bool TokensEqual(string x, string y) =>
        string.Equals(x, y, StringComparison.Ordinal) ||
        string.Equals(x.Replace('’', '\''), y.Replace('’', '\''), StringComparison.Ordinal);

    private enum EditOp
    {
        Equal,
        Delete,
        Insert
    }

    /// <summary>
    /// Algorithme de Myers (« An O(ND) Difference Algorithm ») : plus court script
    /// d'édition, en temps proportionnel au nombre de différences — idéal pour une
    /// correction où l'essentiel du texte est inchangé. Indices : dans a pour
    /// Equal/Delete, dans b pour Insert.
    /// </summary>
    private static List<(EditOp Op, int Index)>? ShortestEditScript(string[] a, string[] b)
    {
        int n = a.Length, m = b.Length;
        int offset = n + m + 1;
        var v = new int[2 * offset + 1];

        // Pour chaque d, copie de la fenêtre [-d-1, d+1] de v avant l'étape d :
        // c'est tout ce dont le retour en arrière a besoin.
        var trace = new List<int[]>();

        for (var d = 0; d <= n + m; d++)
        {
            if (d > MaxEdits) return null;

            var window = new int[2 * d + 3];
            Array.Copy(v, offset - d - 1, window, 0, window.Length);
            trace.Add(window);

            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                var y = x - k;

                while (x < n && y < m && TokensEqual(a[x], b[y]))
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;

                if (x >= n && y >= m)
                {
                    return Backtrack(trace, n, m);
                }
            }
        }

        return null;
    }

    private static List<(EditOp Op, int Index)> Backtrack(List<int[]> trace, int n, int m)
    {
        var ops = new List<(EditOp, int)>();
        int x = n, y = m;

        for (var d = trace.Count - 1; d >= 0; d--)
        {
            var window = trace[d];
            int V(int k) => window[k + d + 1];

            var k = x - y;
            var prevK = k == -d || (k != d && V(k - 1) < V(k + 1)) ? k + 1 : k - 1;
            var prevX = V(prevK);
            var prevY = prevX - prevK;

            while (x > prevX && y > prevY)
            {
                ops.Add((EditOp.Equal, x - 1));
                x--;
                y--;
            }

            if (d > 0)
            {
                ops.Add(x == prevX ? (EditOp.Insert, y - 1) : (EditOp.Delete, x - 1));
            }

            x = prevX;
            y = prevY;
        }

        ops.Reverse();
        return ops;
    }
}
