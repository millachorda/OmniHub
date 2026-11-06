using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OmniHub.Core;

public class InstallOptions
{
    public string Dir = AppInfo.DefaultInstallDir;
    public bool DesktopShortcut = true;
    public bool StartWithWindows;
    public bool CloseVendorApps;
    public List<Component> Components = new();
}

public static class Installer
{
    public static async Task InstallAsync(InstallOptions o, IProgress<string> log, IProgress<double> prog, CancellationToken ct)
    {
        log.Report($"Instalando en {o.Dir}");
        Directory.CreateDirectory(o.Dir);

        // Si la app instalada está abierta, la cerramos para poder sobrescribirla
        foreach (var p in Process.GetProcessesByName("OmniHub"))
            if (p.Id != Environment.ProcessId) { try { p.Kill(); p.WaitForExit(3000); } catch { } }

        var target = Path.Combine(o.Dir, "OmniHub.exe");
        // La instalación completa sale del repositorio de GitHub (última release).
        // Si no hay conexión o no hay release, se usa este mismo ejecutable.
        bool fromGitHub = false;
        log.Report($"Buscando la última versión en {Updater.RepoUrl} …");
        var rel = await Updater.LatestAsync(ct);
        if (rel != null && rel.Version >= Updater.Current)
        {
            try
            {
                log.Report($"Descargando OmniHub {rel.Tag} desde GitHub…");
                await Updater.DownloadAsync(rel, target, prog, ct);
                fromGitHub = true;
                log.Report($"OmniHub {rel.Tag} descargado.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { log.Report("No se pudo descargar de GitHub (" + e.Message + "); uso este instalador."); }
        }
        else log.Report(rel == null ? "Sin conexión con GitHub; uso este instalador." : "Este instalador ya es la última versión.");
        if (!fromGitHub && !string.Equals(Path.GetFullPath(AppInfo.ExePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(AppInfo.ExePath, target, true);
        log.Report("Programa instalado.");

        // Registro en "Aplicaciones instaladas" de Windows
        using (var k = Registry.LocalMachine.CreateSubKey(AppInfo.UninstallKey))
        {
            k.SetValue("DisplayName", "OmniHub");
            k.SetValue("DisplayVersion", AppInfo.Version);
            k.SetValue("Publisher", "OmniHub");
            k.SetValue("DisplayIcon", target);
            k.SetValue("InstallLocation", o.Dir);
            k.SetValue("UninstallString", $"\"{target}\" --uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(target).Length / 1024), RegistryValueKind.DWord);
        }

        // Accesos directos
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "OmniHub.lnk");
        SystemTools.CreateShortcut(startMenu, target);
        if (o.DesktopShortcut)
            SystemTools.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "OmniHub.lnk"), target);
        log.Report("Accesos directos creados.");

        var s = Settings.Current;
        s.StartWithWindows = o.StartWithWindows;
        s.CloseVendorApps = o.CloseVendorApps;
        s.Save();
        SystemTools.SetStartup(o.StartWithWindows, target);

        // Componentes: tienen que ir dentro de la carpeta instalada
        int i = 0;
        foreach (var c in o.Components)
        {
            ct.ThrowIfCancellationRequested();
            log.Report($"── {c.Name}");
            try
            {
                if (c.IsInstalled()) log.Report("Ya estaba instalado.");
                else await c.Install(log, prog, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                log.Report($"⚠ No se pudo instalar {c.Name}: {e.Message}. Puedes reintentarlo desde el launcher o descargarlo de {c.Homepage}");
            }
            i++;
            prog.Report((double)i / Math.Max(1, o.Components.Count));
        }
        log.Report("Instalación terminada.");
    }

    public static void Uninstall()
    {
        var dir = AppInfo.InstallDir;
        SystemTools.SetStartup(false, "");
        foreach (var lnk in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "OmniHub.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "OmniHub.lnk"),
        })
            try { File.Delete(lnk); } catch { }
        try { Registry.LocalMachine.DeleteSubKeyTree(AppInfo.UninstallKey, false); } catch { }
        // No podemos borrar el .exe mientras corre: lo hace cmd cuando salimos
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{dir}\"")
        { CreateNoWindow = true, UseShellExecute = false });
    }
}
