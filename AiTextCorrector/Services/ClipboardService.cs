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
                return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            }
            catch (ExternalException)
            {
                // Presse-papiers momentanément verrouillé par une autre application.
                Thread.Sleep(RetryDelayMs);
            }
            catch
            {
                return string.Empty;
            }
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
