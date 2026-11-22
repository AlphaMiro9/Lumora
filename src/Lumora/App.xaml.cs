using System.Windows;
using Lumora.Shell;

namespace Lumora;

public partial class App : Application
{
    public App()
    {
        AppLog.Init(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumora", "logs"));
        AppLog.Info("Lumora " + UpdateService.Current + " startet");

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Unbehandelter Fehler", args.Exception);
            MessageBox.Show("Es ist ein unerwarteter Fehler aufgetreten:\n\n" + args.Exception.Message +
                            "\n\nDetails stehen im Protokoll (Einstellungen → Protokoll-Ordner öffnen).",
                "Lumora", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Error("Fataler Fehler", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { AppLog.Error("Fehler in einer Hintergrundaufgabe", args.Exception); args.SetObserved(); };
    }
}
