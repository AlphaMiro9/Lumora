using System.Windows;

namespace Lumora;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("Es ist ein unerwarteter Fehler aufgetreten:\n\n" + args.Exception.Message,
                "Lumora", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
