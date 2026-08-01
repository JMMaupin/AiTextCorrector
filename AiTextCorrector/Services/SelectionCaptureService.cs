using System;
using System.Collections.Generic;
using System.Threading;
using AiTextCorrector.Interop;

namespace AiTextCorrector.Services;

/// <summary>
/// Récupère le texte sélectionné dans l'application au premier plan, en lui
/// envoyant un Ctrl+C simulé.
///
/// L'API d'accessibilité (UI Automation / TextPattern) serait plus propre, mais
/// de nombreux éditeurs ne l'exposent pas — Notepad++, fondé sur Scintilla, ne
/// publie aucun élément textuel. Le Ctrl+C simulé, lui, fonctionne partout où la
/// copie fonctionne.
/// </summary>
public sealed class SelectionCaptureService
{
    // Délai laissé à l'utilisateur pour relâcher naturellement les modificateurs
    // du raccourci avant qu'on les relâche à sa place.
    private const int ModifierReleaseTimeoutMs = 250;

    // Temps accordé à l'application source pour honorer la copie.
    private const int CopyTimeoutMs = 400;

    private const int PollIntervalMs = 15;

    /// <summary>
    /// Tente de copier la sélection courante. Renvoie true si le presse-papiers a
    /// effectivement changé — c'est-à-dire s'il y avait bien une sélection.
    /// </summary>
    public bool TryCopySelection()
    {
        // Le compteur de séquence dit si une copie a eu lieu, sans qu'on ait à
        // comparer les contenus : si rien n'était sélectionné, Ctrl+C ne fait rien
        // et le presse-papiers conserve son contenu précédent, qu'il ne faudrait
        // surtout pas prendre pour la sélection de l'utilisateur.
        var sequenceBefore = NativeMethods.GetClipboardSequenceNumber();

        ReleaseHotkeyModifiers();
        SendCopy();

        var deadline = Environment.TickCount64 + CopyTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (NativeMethods.GetClipboardSequenceNumber() != sequenceBefore)
            {
                return true;
            }

            Thread.Sleep(PollIntervalMs);
        }

        return false;
    }

    /// <summary>
    /// Au déclenchement du raccourci, l'utilisateur tient encore Ctrl et Alt.
    /// Envoyer Ctrl+C dans cet état produirait Ctrl+Alt+C, qui ne copie pas.
    /// </summary>
    private static void ReleaseHotkeyModifiers()
    {
        var deadline = Environment.TickCount64 + ModifierReleaseTimeoutMs;
        while (Environment.TickCount64 < deadline && AnyModifierHeld())
        {
            Thread.Sleep(PollIntervalMs);
        }

        if (!AnyModifierHeld())
        {
            return;
        }

        // Toujours enfoncés : on les relâche nous-mêmes. Ctrl est maintenu appuyé
        // pendant l'opération, car relâcher Alt ou Windows isolément activerait la
        // barre de menus de l'application ou le menu Démarrer.
        var inputs = new List<NativeMethods.INPUT>();
        var ctrlAlreadyDown = NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL);

        if (!ctrlAlreadyDown)
        {
            inputs.Add(NativeMethods.KeyInput(NativeMethods.VK_CONTROL, keyUp: false));
        }

        foreach (var key in new[] { NativeMethods.VK_MENU, NativeMethods.VK_SHIFT,
                                    NativeMethods.VK_LWIN, NativeMethods.VK_RWIN })
        {
            if (NativeMethods.IsKeyDown(key))
            {
                inputs.Add(NativeMethods.KeyInput(key, keyUp: true));
            }
        }

        if (!ctrlAlreadyDown)
        {
            inputs.Add(NativeMethods.KeyInput(NativeMethods.VK_CONTROL, keyUp: true));
        }

        NativeMethods.SendKeys(inputs.ToArray());
        Thread.Sleep(PollIntervalMs);
    }

    private static bool AnyModifierHeld() =>
        NativeMethods.IsKeyDown(NativeMethods.VK_MENU) ||
        NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT) ||
        NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) ||
        NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);

    private static void SendCopy()
    {
        // Si l'utilisateur maintient déjà Ctrl, on ne le réappuie pas : on se
        // contente de la touche C, sinon on encadre par Ctrl bas / Ctrl haut.
        var ctrlAlreadyDown = NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL);
        var inputs = new List<NativeMethods.INPUT>();

        if (!ctrlAlreadyDown)
        {
            inputs.Add(NativeMethods.KeyInput(NativeMethods.VK_CONTROL, keyUp: false));
        }

        inputs.Add(NativeMethods.KeyInput(NativeMethods.VK_C, keyUp: false));
        inputs.Add(NativeMethods.KeyInput(NativeMethods.VK_C, keyUp: true));

        if (!ctrlAlreadyDown)
        {
            inputs.Add(NativeMethods.KeyInput(NativeMethods.VK_CONTROL, keyUp: true));
        }

        NativeMethods.SendKeys(inputs.ToArray());
    }
}
