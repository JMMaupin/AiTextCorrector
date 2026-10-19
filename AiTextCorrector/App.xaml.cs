using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AiTextCorrector.Setup;

namespace AiTextCorrector;

public partial class App : Application
{
    // Noms uniques au niveau machine pour identifier l'application.
    private const string InstanceMutexName = @"Local\AiTextCorrector_SingleInstance_Mutex";
    private const string ShowEventName = @"Local\AiTextCorrector_SingleInstance_Show";

    // Demande de fermeture, envoyée par l'installeur avant de remplacer l'exe.
    internal const string ExitEventName = @"Local\AiTextCorrector_SingleInstance_Exit";

    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _exitEvent;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Sans ces gardes, la moindre exception non gérée termine le processus en
        // silence. C'est ce qui donnait l'impression que l'application « ne se
        // lançait pas » au démarrage de Windows : aucune fenêtre, aucun message.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        base.OnStartup(e);
        Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();

        // Installation, mise à jour, désinstallation : AVANT le contrôle d'instance
        // unique. L'exe lancé n'est pas forcément l'exe installé, et doit pouvoir
        // fermer l'instance en cours pour la remplacer.
        try
        {
            if (SetupFlow.HandleStartup(e.Args))
            {
                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            ReportError(ex, "Installation check failed.");
        }

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            // Une instance tourne déjà : on lui demande de s'afficher, puis on quitte.
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch
            {
                // Si on ne peut pas signaler l'autre instance, on quitte quand même.
            }

            Shutdown();
            return;
        }

        // L'événement est créé avant la fenêtre : une seconde instance lancée
        // pendant l'initialisation doit pouvoir nous signaler, sinon elle se
        // termine sans rien afficher.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        StartInstanceRequestListener();

        // Première instance : crée la fenêtre.
        try
        {
            _mainWindow = new MainWindow();

            // L'UI est visible à chaque lancement. La réduction (–) ou la croix (✕)
            // l'envoie dans la zone de notification ; relancer l'application la
            // réaffiche. Show() force aussi le cycle Loaded qui enregistre le hotkey.
            _mainWindow.Show();

            // Lancée par l'installeur : confirmation discrète de l'opération.
            if (e.Args.Contains(SetupFlow.InstalledArgument, StringComparer.OrdinalIgnoreCase))
            {
                _mainWindow.NotifySetupCompleted(isUpdate: false);
            }
            else if (e.Args.Contains(SetupFlow.UpdatedArgument, StringComparer.OrdinalIgnoreCase))
            {
                _mainWindow.NotifySetupCompleted(isUpdate: true);
            }
        }
        catch (Exception ex)
        {
            // Échec d'initialisation : mieux vaut un message explicite qu'un
            // processus fantôme sans fenêtre ni icône de notification.
            ReportError(ex, "Impossible de démarrer AI Text Corrector.");
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // On signale l'erreur et on garde l'application en vie : une exception dans
        // un gestionnaire d'événement ne doit pas faire disparaître l'icône systray.
        e.Handled = true;
        ReportError(e.Exception, "Une erreur inattendue s'est produite.");
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            ReportError(ex, "Une erreur inattendue s'est produite.");
        }
    }

    private static void ReportError(Exception ex, string title)
    {
        try
        {
            MessageBox.Show($"{title}\n\n{ex.GetType().Name} : {ex.Message}",
                "AI Text Corrector", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            // Impossible d'afficher un message (session en fermeture) : on abandonne
            // silencieusement plutôt que de relancer une exception depuis un handler.
        }
    }

    // Écoute les demandes des autres processus : réafficher la fenêtre (second
    // lancement) ou quitter (installeur sur le point de remplacer l'exe).
    private void StartInstanceRequestListener()
    {
        var handles = new WaitHandle[] { _showEvent!, _exitEvent! };
        var thread = new Thread(() =>
        {
            while (true)
            {
                int signaled;
                try
                {
                    signaled = WaitHandle.WaitAny(handles);
                }
                catch
                {
                    break; // handles disposés à la fermeture de l'application
                }

                if (signaled == 0)
                {
                    Dispatcher.Invoke(() => _mainWindow?.ShowFromAnotherInstance());
                }
                else
                {
                    Dispatcher.Invoke(() => _mainWindow?.ExitFromAnotherInstance());
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstanceRequestListener"
        };
        thread.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();
        _exitEvent?.Dispose();

        if (_ownsMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); } catch { }
        }
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
