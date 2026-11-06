using System.Diagnostics;
using System.IO;

namespace OmniHub.Core;

public record VendorApp(string Name, string[] Processes);

/// <summary>Apps de fabricantes que compiten por el mismo hardware (luces/ventiladores/ratón).</summary>
public static class VendorApps
{
    public static readonly VendorApp[] Known =
    {
        new("Logitech G HUB", new[] { "lghub", "lghub_agent", "lghub_system_tray", "lghub_updater" }),
        new("MSI Center", new[] { "MSI Center", "MSI.CentralServer", "MSI_Central_Service", "MSI.TerminalServer", "MysticLight_Service" }),
        new("Sony INZONE Hub", new[] { "INZONEHub" }),
        new("Razer Synapse", new[] { "Razer Synapse 3", "RazerAppEngine", "Razer Synapse Service" }),
        new("Corsair iCUE", new[] { "iCUE", "Corsair.Service" }),
        new("SteelSeries GG", new[] { "SteelSeriesGG", "SteelSeriesEngine" }),
        new("ASUS Armoury Crate", new[] { "ArmouryCrate", "ArmourySocketServer", "LightingService" }),
        new("NZXT CAM", new[] { "NZXT CAM" }),
        new("HyperX NGENUITY", new[] { "NGENUITY" }),
        new("Gigabyte Control Center", new[] { "GCC", "RGBFusion" }),
        new("SignalRGB", new[] { "SignalRgb" }),
    };

    public static IEnumerable<VendorApp> Running()
    {
        var names = new HashSet<string>(Process.GetProcesses().Select(p => p.ProcessName), StringComparer.OrdinalIgnoreCase);
        return Known.Where(v => v.Processes.Any(names.Contains));
    }

    /// <summary>Cierra las apps de usuario (no toca servicios de Windows).</summary>
    public static int CloseAll()
    {
        int n = 0;
        foreach (var v in Known)
            foreach (var pn in v.Processes)
                foreach (var p in Process.GetProcessesByName(pn))
                {
                    try { p.Kill(true); n++; } catch { }
                }
        return n;
    }
}

public static class SystemTools
{
    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    public static int Run(string exe, string args, bool wait = true)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });
            if (p == null) return -1;
            if (!wait) return 0;
            p.WaitForExit(60000);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    /// <summary>Inicio con Windows mediante tarea programada (con privilegios, sin aviso UAC).</summary>
    public static void SetStartup(bool on, string exe)
    {
        if (on)
            Run("schtasks.exe", $"/Create /TN \"OmniHub\" /TR \"\\\"{exe}\\\" --tray\" /SC ONLOGON /RL HIGHEST /F");
        else
            Run("schtasks.exe", "/Delete /TN \"OmniHub\" /F");
    }

    public static void CreateShortcut(string lnkPath, string target, string args = "", string? icon = null)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic sc = shell.CreateShortcut(lnkPath);
            sc.TargetPath = target;
            sc.Arguments = args;
            sc.WorkingDirectory = Path.GetDirectoryName(target);
            sc.IconLocation = (icon ?? target) + ",0";
            sc.Save();
        }
        catch { }
    }

    public static string Fmt(double bytes) =>
        bytes > 1 << 20 ? $"{bytes / (1 << 20):0.0} MB" : $"{bytes / 1024:0} KB";
}
