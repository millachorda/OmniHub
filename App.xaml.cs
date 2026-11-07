using System.Threading;
using System.Windows;
using OmniHub.Core;
using OmniHub.Services;
using OmniHub.UI;

namespace OmniHub;

/// <summary>
/// Un solo .exe con tres modos:
///  · fuera de la carpeta instalada  → instalador (escanea hardware y descarga lo necesario)
///  · instalado                      → busca actualizaciones → app
///  · --uninstall                    → desinstalador
/// </summary>
public partial class App : Application
{
    static Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash(ex.Exception);
            MessageBox.Show(ex.Exception.Message, "OmniHub", MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => LogCrash(ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => { LogCrash(ex.Exception); ex.SetObserved(); };

        var args = e.Args.Select(a => a.ToLowerInvariant()).ToArray();

        // Sin permisos de administrador no se puede instalar ni controlar ventiladores: nos relanzamos elevados
        if (!IsAdmin() && !args.Contains("--no-elevate"))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppInfo.ExePath, string.Join(" ", e.Args))
                { UseShellExecute = true, Verb = "runas" });
            }
            catch
            {
                MessageBox.Show("OmniHub necesita permisos de administrador para instalarse y controlar el hardware.",
                    "OmniHub", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        if (args.Contains("--uninstall"))
        {
            if (MessageBox.Show("¿Desinstalar OmniHub?", "Desinstalar OmniHub", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var modules = MessageBox.Show(
                    "¿Quitar también los módulos de OmniHub (sensores, iluminación y audio)?\n\nSe eliminan en segundo plano. Recomendado salvo que los uses con otros programas.",
                    "Desinstalar OmniHub", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                var wipe = MessageBox.Show("¿Borrar también tus ajustes y perfiles?", "Desinstalar OmniHub",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                Installer.Uninstall(modules);
                if (wipe) try { System.IO.Directory.Delete(AppInfo.DataDir, true); } catch { }
                MessageBox.Show(modules ? "OmniHub y sus módulos se han desinstalado. Reinicia el PC para terminar de quitar los drivers." : "OmniHub se ha desinstalado.", "OmniHub");
            }
            Shutdown();
            return;
        }

        if (!AppInfo.RunningInstalled && !args.Contains("--portable"))
        {
            new InstallerWindow().Show();
            return;
        }

        Hub.TestMode = args.Contains("--test");
        // Una sola instancia siempre (también en modo prueba). Si ya hay una, se trae al frente y esta se cierra.
        bool first;
        var devTest = Hub.TestMode && Environment.GetEnvironmentVariable("OMNIHUB_DEVTEST") == "1"; // solo pruebas de desarrollo
        try { _single = new Mutex(true, devTest ? "OmniHub.DevTest" : "OmniHub.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; } // la abierta tiene más permisos: también cuenta
        if (!first)
        {
            try { using var show = EventWaitHandle.OpenExisting(devTest ? "OmniHub.DevTest.Show" : "OmniHub.Show"); show.Set(); } catch { }
            Shutdown();
            return;
        }
        // Escucha "muéstrate" de una segunda apertura
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, devTest ? "OmniHub.DevTest.Show" : "OmniHub.Show");
        new Thread(() =>
        {
            while (showEvent.WaitOne())
                Dispatcher.BeginInvoke(() =>
                {
                    if (Hub.ShowMain != null) Hub.ShowMain();
                    else foreach (Window w in Windows) { w.Show(); w.Activate(); }
                });
        }) { IsBackground = true, Name = "SingleInstance" }.Start();

        if (args.Contains("--tray"))
        {
            var main = new MainWindow();
            main.StartHidden();
            return;
        }

        // Sin launcher: al abrir solo se buscan actualizaciones (en la copia instalada) y se abre la app
        if (AppInfo.RunningInstalled && !Hub.TestMode) new UpdateWindow().Show();
        else
        {
            var main = new MainWindow();
            if (Settings.Current.StartMinimized && !Hub.TestMode) main.StartHidden(); else main.Show();
        }
    }

    /// <summary>Registro de errores en %AppData%\OmniHub\errores.log para poder diagnosticar.</summary>
    static void LogCrash(Exception? e)
    {
        if (e == null) return;
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppInfo.DataDir, "errores.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] v{AppInfo.Version}\n{e}\n\n"); } catch { }
    }

    static bool IsAdmin() =>
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    protected override void OnExit(ExitEventArgs e)
    {
        Hub.Shutdown();
        Settings.Current.Save();
        base.OnExit(e);
    }
}
