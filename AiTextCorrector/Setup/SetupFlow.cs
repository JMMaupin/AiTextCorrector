using System;
using System.Linq;
using System.Windows;
using AiTextCorrector.Localization;

namespace AiTextCorrector.Setup;

/// <summary>
/// Décide, au lancement, si l'exe doit s'installer, mettre à jour la version
/// installée, se désinstaller, ou simplement démarrer.
/// </summary>
internal static class SetupFlow
{
    // Arguments passés à l'exe installé qu'on lance, pour qu'il confirme l'opération.
    public const string InstalledArgument = "--installed";
    public const string UpdatedArgument = "--updated";

    /// <summary>
    /// Renvoie true si le processus doit s'arrêter là : opération d'installation
    /// traitée (la version installée a pris le relais) ou abandonnée.
    /// </summary>
    public static bool HandleStartup(string[] args)
    {
        if (args.Contains(Installer.UninstallArgument, StringComparer.OrdinalIgnoreCase))
        {
            RunUninstall();
            return true;
        }

        if (!Installer.IsPublishedExecutable || Installer.IsRunningInstalledCopy)
        {
            return false;
        }

        var installedVersion = Installer.GetInstalledVersion();
        if (installedVersion is null)
        {
            return OfferInstall();
        }

        // Plus récent, ou même numéro mais autre build : on met à jour. Jamais de
        // retour en arrière : une version installée plus récente est conservée.
        var comparison = CompareVersions(AppInfo.Version, installedVersion);
        if (comparison > 0 || (comparison == 0 && !Installer.InstalledCopyIsIdentical()))
        {
            return Apply(() => Installer.Install(startWithWindows: null), UpdatedArgument, Loc.Text("Setup.UpdateFailed"));
        }

        // Déjà installé à l'identique (ou plus récent) : on passe la main à la version
        // installée, qui réaffiche l'instance en cours s'il y en a une.
        Installer.LaunchInstalled();
        return true;
    }

    private static bool OfferInstall()
    {
        var window = new SetupWindow(AppInfo.Version, Installer.InstallDirectory, Installer.LegacyInstallPath);
        window.ShowDialog();

        return window.Choice switch
        {
            SetupChoice.Install => Apply(() => Installer.Install(window.StartWithWindows), InstalledArgument, Loc.Text("Setup.InstallFailed")),
            SetupChoice.RunWithoutInstalling => false,
            _ => true
        };
    }

    // Exécute l'opération puis lance la version installée. En cas d'échec, l'erreur
    // est montrée et l'on s'arrête : rien ne démarre dans un état incertain.
    private static bool Apply(Action operation, string launchArgument, string failureMessage)
    {
        try
        {
            operation();
            Installer.LaunchInstalled(launchArgument);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{failureMessage}\n\n{ex.Message}", AppInfo.Product,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return true;
    }

    private static void RunUninstall()
    {
        if (MessageBox.Show(Loc.Format("Setup.ConfirmUninstall", AppInfo.Product), AppInfo.Product,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        // Défaut sur « No » et conséquence explicite : la suppression est définitive,
        // les clés API devront être ressaisies (voire régénérées chez le fournisseur).
        var removeUserData = MessageBox.Show(
            Loc.Text("Setup.DeleteUserData"),
            AppInfo.Product, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        try
        {
            Installer.Uninstall(removeUserData);
            MessageBox.Show(Loc.Format("Setup.Uninstalled", AppInfo.Product), AppInfo.Product,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc.Text("Setup.UninstallFailed")}\n\n{ex.Message}", AppInfo.Product,
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // En tout dernier : le dossier n'est effacé qu'une fois ce processus terminé.
        Installer.ScheduleInstallDirectoryRemoval();
    }

    /// <summary>
    /// Compare deux versions SemVer (« 1.2.0 », « 1.3.0-beta.2 ») : partie numérique
    /// d'abord, puis une version publiée l'emporte sur ses pré-versions
    /// (1.2.0-beta &lt; 1.2.0), enfin les identifiants de pré-version un à un.
    /// </summary>
    internal static int CompareVersions(string a, string b)
    {
        var (coreA, preA) = Parse(a);
        var (coreB, preB) = Parse(b);

        var core = coreA.CompareTo(coreB);
        if (core != 0) return core;

        return (preA.Length == 0, preB.Length == 0) switch
        {
            (true, true) => 0,
            (true, false) => 1,
            (false, true) => -1,
            _ => ComparePrerelease(preA, preB)
        };
    }

    private static (Version Core, string Prerelease) Parse(string version)
    {
        var parts = version.Split('+')[0].Split('-', 2);
        var core = System.Version.TryParse(parts[0], out var parsed) ? parsed : new Version(0, 0);
        return (core, parts.Length > 1 ? parts[1] : string.Empty);
    }

    // Règles SemVer : identifiants numériques comparés en nombres et placés avant
    // les alphanumériques ; à égalité de préfixe, la liste la plus courte est la plus ancienne.
    private static int ComparePrerelease(string a, string b)
    {
        var idsA = a.Split('.');
        var idsB = b.Split('.');

        for (var i = 0; i < Math.Min(idsA.Length, idsB.Length); i++)
        {
            var isNumA = long.TryParse(idsA[i], out var numA);
            var isNumB = long.TryParse(idsB[i], out var numB);

            var result = (isNumA, isNumB) switch
            {
                (true, true) => numA.CompareTo(numB),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(idsA[i], idsB[i])
            };

            if (result != 0) return result;
        }

        return idsA.Length.CompareTo(idsB.Length);
    }
}
