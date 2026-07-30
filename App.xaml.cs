using Small_square_cavity_coating_machine.Services;
using Small_square_cavity_coating_machine.Views;
using System.Windows;

namespace Small_square_cavity_coating_machine;

/// <summary>
/// Interaction logic for App.xaml.
/// </summary>
public partial class App : Application
{
    public ApplicationServices Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Services = new ApplicationServices(Dispatcher);
        _ = Services.StartAsync();

        var mainWindow = new MainView(Services);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.Dispose();
        base.OnExit(e);
    }
}
