using System;
using Microsoft.Win32;

namespace AiTextCorrector.Services;

/// <summary>
/// Gère le démarrage automatique de l'application à l'ouverture de session Windows.
/// Utilise la clé de registre HKCU\...\Run, qui ne nécessite aucun privilège
/// administrateur (contrairement à HKLM ou au dossier Démarrage commun).
/// </summary>
public class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AiTextCorrector";

    // Chemin complet de l'exécutable actuel, entre guillemets au cas où il
    // contiendrait des espaces.
    private static string ExecutablePath => "\"" + Environment.ProcessPath + "\"";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var current = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(current);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Active ou désactive le lancement au démarrage. Renvoie l'état effectif
    /// après l'opération (utile pour rafraîchir la coche du menu).
    /// </summary>
    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return IsEnabled();
            }

            if (enabled)
            {
                key.SetValue(ValueName, ExecutablePath, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // En cas d'échec (droits, registre verrouillé…), on ne casse rien :
            // on renvoie simplement l'état réel.
        }

        return IsEnabled();
    }
}
