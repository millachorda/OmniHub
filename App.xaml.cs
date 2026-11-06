using System.Threading;
using System.Windows;
using OmniHub.Core;
using OmniHub.Services;
using OmniHub.UI;

namespace OmniHub;

/// <summary>
/// Un solo .exe con tres modos:
///  · fuera de la carpeta instalada  → instalador (escanea hardware y descarga lo necesario)
///  · instalado                      → launcher → app
///  · --uninstall                    → desinstalador
/// </summary>
public partial class App : Application
{
    static Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message, "OmniHub", MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };

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
            if (MessageBox.Show("¿Desinstalar OmniHub y sus accesos directos?\n(Los componentes como Equalizer APO o PawnIO se desinstalan aparte desde Windows.)",
                    "Desinstalar OmniHub", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Installer.Uninstall();
                MessageBox.Show("OmniHub se ha desinstalado.", "OmniHub");
            }
            Shutdown();
            return;
        }

        if (!AppInfo.RunningInstalled && !args.Contains("--portable"))
        {
            new InstallerWindow().Show();
            return;
        }

        _single = new Mutex(true, "OmniHub.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("OmniHub ya está abierto (mira en la bandeja del sistema).", "OmniHub");
            Shutdown();
            return;
        }

        if (args.Contains("--tray"))
        {
            var main = new MainWindow();
            main.StartHidden();
            return;
        }

        if (Settings.Current.SkipLauncher) new MainWindow().Show();
        else new LauncherWindow().Show();
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
