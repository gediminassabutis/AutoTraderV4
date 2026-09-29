using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace AutoTraderV4.WindowsDesktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    private static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        Console.WriteLine($"[{timestamp}] {message}");
        Debug.WriteLine($"[{timestamp}] {message}");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AllocConsole();
        Log("WPF app starting.");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log($"Unhandled domain exception: {args.ExceptionObject}");

        DispatcherUnhandledException += (_, args) =>
        {
            Log($"Unhandled UI exception: {args.Exception}");
            args.Handled = false;
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("WPF app shutting down.");
        base.OnExit(e);
    }
}

