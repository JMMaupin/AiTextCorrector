using System;
using System.Runtime.InteropServices;
using System.Threading;
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
