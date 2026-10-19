using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AiTextCorrector.Services;

public class ClipboardService
{
    // Le presse-papiers Win32 est une ressource globale qu'un seul processus peut
    // verrouiller à la fois. L'application qui vient de copier le tient encore
    // ouvert au moment où l'on est notifié du changement : sans réessais, une
    // lecture sur deux échoue et la capture est perdue en silence.
    private const int MaxAttempts = 10;
    private const int RetryDelayMs = 40;

    /// <summary>
    /// Dernier texte écrit par l'application elle-même. Permet d'ignorer les
    /// notifications de changement que l'on a soi-même provoquées (copie
    /// automatique du résultat), qui sinon seraient réinjectées en entrée.
    /// </summary>
    public string? LastWrittenText { get; private set; }

    public string TryReadText()
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    // Aucun texte du tout (image, fichiers…) : inutile d'insister.
                    return string.Empty;
                }

                var text = Clipboard.GetText();
                if (text.Length > 0)
                {
                    return text;
                }

                // ContainsText() annonce du texte mais GetText() rend une chaîne vide :
                // la donnée n'est pas encore disponible (rendu différé du presse-papiers).
                // C'est ce cas, non rattrapé, qui faisait échouer le collage automatique.
            }
            catch (ExternalException)
            {
                // Presse-papiers momentanément verrouillé par une autre application.
            }
            catch
            {
                return string.Empty;
            }

            // Temporisation commune aux deux causes de réessai (verrou et rendu
            // différé) : la placer dans le seul catch laissait la boucle tourner
            // à vide sur le second cas.
            Thread.Sleep(RetryDelayMs);
        }

        return string.Empty;
    }

    /// <summary>
    /// Lit le texte juste après une copie dont on sait qu'elle a eu lieu, en
    /// insistant tant qu'il est absent, jusqu'à <paramref name="timeout"/>.
    ///
    /// Contrairement à <see cref="TryReadText"/>, une absence de texte n'est pas
    /// définitive : Windows signale le changement dès que l'application source VIDE
    /// le presse-papiers, avant qu'elle y dépose son texte. Lu à cet instant, il
    /// paraît vide — et la sélection, pourtant bien copiée, était perdue.
    /// </summary>
    public async Task<string> ReadTextAfterCopyAsync(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true)
        {
            try
            {
                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (text.Length > 0)
                {
                    return text;
                }
            }
            catch (ExternalException)
            {
                // Presse-papiers encore verrouillé par l'application source.
            }
            catch
            {
                return string.Empty;
            }

            if (Environment.TickCount64 >= deadline)
            {
                return string.Empty;
            }

            await Task.Delay(RetryDelayMs);
        }
    }

    /// <summary>Écrit du texte brut dans le presse-papiers. Renvoie false en cas d'échec.</summary>
    public bool WritePlainText(string? text)
    {
        var value = text ?? string.Empty;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                if (value.Length == 0)
                {
                    // Clipboard.SetText refuse une chaîne vide.
                    Clipboard.Clear();
                }
                else
                {
                    Clipboard.SetText(value, TextDataFormat.UnicodeText);
                }

                LastWrittenText = value;
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(RetryDelayMs);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    public void SimulatePaste()
    {
        SendKeys.SendWait("^v");
        Thread.Sleep(60);
    }
}
