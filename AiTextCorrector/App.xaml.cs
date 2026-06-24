using System;
using System.Threading;
using System.Windows;

namespace AiTextCorrector;

public partial class App : Application
{
    // Noms uniques au niveau machine pour identifier l'application.
    private const string InstanceMutexName = @"Local\AiTextCorrector_SingleInstance_Mutex";
    private const string ShowEventName = @"Local\AiTextCorrector_SingleInstance_Show";

    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // Première instance : applique le thème, crée la fenêtre, et écoute les
        // demandes d'affichage envoyées par d'éventuelles instances ultérieures.
        Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
        _mainWindow = new MainWindow();

        // L'UI est visible à chaque lancement. La réduction (–) ou la croix (✕)
        // l'envoie dans la zone de notification ; relancer l'application la
        // réaffiche. Show() force aussi le cycle Loaded qui enregistre le hotkey.
        _mainWindow.Show();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        StartShowRequestListener();
    }

    private void StartShowRequestListener()
    {
        var handle = _showEvent!;
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    handle.WaitOne();
                }
                catch
                {
                    break; // handle disposé à la fermeture de l'application
                }

                Dispatcher.Invoke(() => _mainWindow?.ShowFromAnotherInstance());
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstanceShowListener"
        };
        thread.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();

        if (_ownsMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); } catch { }
        }
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
