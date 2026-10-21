using System.Windows;
using AiTextCorrector.Localization;

namespace AiTextCorrector.Setup;

public enum SetupChoice
{
    // Fenêtre fermée par la croix : l'utilisateur renonce à tout.
    Cancel,
    Install,
    RunWithoutInstalling
}

/// <summary>Proposition d'installation, au lancement de l'exe sur un poste où il n'est pas installé.</summary>
public partial class SetupWindow : Wpf.Ui.Controls.FluentWindow
{
    public SetupWindow(string version, string installDirectory, string? legacyInstallPath)
    {
        InitializeComponent();

        Title = Loc.Format("Setup.Title", AppInfo.Product);
        SetupTitleBar.Title = Title;
        HeadingText.Text = Loc.Format("Setup.NotInstalled", AppInfo.Product, version);
        InstallPathText.Text = installDirectory;

        if (legacyInstallPath is not null)
        {
            LegacyInstallText.Text = Loc.Format("Setup.LegacyCopy", legacyInstallPath);
            LegacyInstallText.Visibility = Visibility.Visible;
        }
    }

    public SetupChoice Choice { get; private set; } = SetupChoice.Cancel;

    public bool StartWithWindows => StartWithWindowsCheckBox.IsChecked == true;

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        Choice = SetupChoice.Install;
        Close();
    }

    private void RunWithoutInstalling_Click(object sender, RoutedEventArgs e)
    {
        Choice = SetupChoice.RunWithoutInstalling;
        Close();
    }
}
