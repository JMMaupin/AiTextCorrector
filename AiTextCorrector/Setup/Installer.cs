using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using AiTextCorrector.Interop;
using AiTextCorrector.Services;
using Microsoft.Win32;

namespace AiTextCorrector.Setup;

/// <summary>
/// Installation, mise à jour et désinstallation de l'exe publié, sans interface.
///
/// Installation par utilisateur, dans %LOCALAPPDATA%\Programs : aucun droit
/// administrateur, donc aucune invite UAC à chaque mise à jour. C'est cohérent
/// avec le reste de l'application, entièrement par utilisateur (configuration dans
/// %APPDATA%, clés API et démarrage automatique dans HKCU).
/// </summary>
internal static class Installer
{
    private const string ExecutableName = "AiTextCorrector.exe";

    // Nom du processus installé, pour retrouver les instances à fermer.
    private const string ProcessName = "AiTextCorrector";

    // Entrée de « Applications installées » (Paramètres Windows).
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AiTextCorrector";

    public const string UninstallArgument = "--uninstall";

    public static string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AiTextCorrector");

    public static string InstalledExePath { get; } = Path.Combine(InstallDirectory, ExecutableName);

    private static string StartMenuShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Product + ".lnk");

    private static string CurrentExePath => Environment.ProcessPath ?? string.Empty;

    /// <summary>
    /// Vrai pour l'exe publié (fichier unique), faux pour les builds de développement
    /// lancés depuis obj\Dev, où l'assembly .dll est posée à côté de l'exe : ceux-là ne
    /// doivent jamais proposer d'installation.
    /// </summary>
    public static bool IsPublishedExecutable =>
        !File.Exists(Path.Combine(AppContext.BaseDirectory, typeof(Installer).Assembly.GetName().Name + ".dll"));

    public static bool IsRunningInstalledCopy =>
        string.Equals(Path.GetFullPath(CurrentExePath), Path.GetFullPath(InstalledExePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ancienne installation manuelle dans Program Files (instructions de publish.cmd
    /// jusqu'à la v1.2.0) : elle n'est plus utilisée, mais sa suppression demande des
    /// droits administrateur, que l'installeur n'a pas.
    /// </summary>
    public static string? LegacyInstallPath
    {
        get
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AiTextCorrector", ExecutableName);
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>Version de l'exe installé, ou null s'il n'y en a pas.</summary>
    public static string? GetInstalledVersion()
    {
        if (!File.Exists(InstalledExePath)) return null;

        var version = FileVersionInfo.GetVersionInfo(InstalledExePath).ProductVersion?.Split('+')[0];
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }

    /// <summary>
    /// Vrai si l'exe installé est octet pour octet celui qui tourne. Deux builds
    /// successifs peuvent porter le même numéro de version : seul le contenu dit
    /// s'il y a quelque chose à mettre à jour.
    /// </summary>
    public static bool InstalledCopyIsIdentical()
    {
        var installed = new FileInfo(InstalledExePath);
        var current = new FileInfo(CurrentExePath);
        if (!installed.Exists || installed.Length != current.Length) return false;

        return HashOf(installed.FullName).AsSpan().SequenceEqual(HashOf(current.FullName));
    }

    /// <summary>
    /// Installe ou met à jour l'exe courant. <paramref name="startWithWindows"/> :
    /// choix fait à l'installation ; null lors d'une mise à jour, où le réglage
    /// existant est conservé.
    /// </summary>
    public static void Install(bool? startWithWindows)
    {
        CloseRunningInstances();

        Directory.CreateDirectory(InstallDirectory);
        CopyExecutable(CurrentExePath, InstalledExePath);

        ShellLink.Create(StartMenuShortcutPath, InstalledExePath, "Correct and translate text with AI");
        RegisterUninstallEntry();

        // Démarrage avec Windows : choix explicite à l'installation. Lors d'une mise
        // à jour, une entrée existante est repointée vers l'exe installé — elle peut
        // viser une ancienne copie (Program Files, version portable).
        var startup = new StartupService();
        var enable = startWithWindows ?? startup.IsEnabled();
        startup.SetEnabled(enable, InstalledExePath);
    }

    /// <summary>Lance l'exe installé, avec un argument éventuel (--installed, --updated).</summary>
    public static void LaunchInstalled(string? argument = null)
    {
        Process.Start(new ProcessStartInfo(InstalledExePath)
        {
            Arguments = argument ?? string.Empty,
            WorkingDirectory = InstallDirectory,
            UseShellExecute = false
        })?.Dispose();
    }

    public static void Uninstall(bool removeUserData)
    {
        CloseRunningInstances();

        new StartupService().SetEnabled(false);
        TryDeleteFile(StartMenuShortcutPath);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

        if (removeUserData)
        {
            // Configuration et clés API : ne pas laisser de secrets derrière soi.
            Registry.CurrentUser.DeleteSubKeyTree(ApiKeyStore.KeyPath, throwOnMissingSubKey: false);
            if (Directory.Exists(ConfigService.ConfigDirectory))
            {
                Directory.Delete(ConfigService.ConfigDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Ferme les autres instances (y compris une ancienne copie lancée depuis un autre
    /// dossier) : elles verrouillent l'exe installé, et l'instance unique empêcherait
    /// la nouvelle version de démarrer.
    /// </summary>
    private static void CloseRunningInstances()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var others = Process.GetProcessesByName(ProcessName)
            .Where(p => p.Id != Environment.ProcessId && p.SessionId == sessionId)
            .ToList();
        if (others.Count == 0) return;

        // Demande polie, à laquelle seules les versions récentes savent répondre ; les
        // plus anciennes, qui l'ignorent, sont arrêtées sans délai.
        var gracefulMs = 0;
        if (EventWaitHandle.TryOpenExisting(App.ExitEventName, out var exitEvent))
        {
            using (exitEvent)
            {
                exitEvent.Set();
            }

            gracefulMs = 5000;
        }

        foreach (var process in others)
        {
            using (process)
            {
                try
                {
                    if (!process.WaitForExit(gracefulMs))
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
                catch
                {
                    // Processus déjà terminé, ou inaccessible : la copie dira s'il gêne.
                }
            }
        }
    }

    // Copie par flux plutôt que File.Copy : la marque « téléchargé depuis Internet »
    // (flux Zone.Identifier) n'est pas reprise, et l'exe installé ne redéclenche pas
    // SmartScreen à chaque lancement. Remplacement final atomique, avec réessais :
    // Windows peut garder l'ancien exe verrouillé un instant après la fin du processus.
    private static void CopyExecutable(string source, string destination)
    {
        var temp = destination + ".new";
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = File.Create(temp))
            {
                input.CopyTo(output);
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temp, destination, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20)
                {
                    Thread.Sleep(250);
                }
            }
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    private static void RegisterUninstallEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true);
        key.SetValue("DisplayName", AppInfo.Product);
        key.SetValue("DisplayVersion", AppInfo.Version);
        key.SetValue("DisplayIcon", InstalledExePath);
        key.SetValue("InstallLocation", InstallDirectory);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("UninstallString", $"\"{InstalledExePath}\" {UninstallArgument}");
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExePath).Length / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Supprime le dossier d'installation une fois le processus terminé. À appeler en
    /// tout dernier, juste avant de quitter : l'exe en cours d'exécution ne peut pas
    /// s'effacer lui-même, un cmd détaché s'en charge après quelques secondes (ping
    /// sert de temporisation, timeout refusant de tourner sans console).
    /// </summary>
    public static void ScheduleInstallDirectoryRemoval()
    {
        Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c ping 127.0.0.1 -n 4 >nul & rd /s /q \"{InstallDirectory}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        })?.Dispose();
    }

    private static byte[] HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Fichier absent ou verrouillé : sans conséquence.
        }
    }
}
