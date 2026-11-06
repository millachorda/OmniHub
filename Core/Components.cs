using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;

namespace OmniHub.Core;

/// <summary>Componentes externos gratuitos y de código abierto que OmniHub usa como "motores".</summary>
public class Component
{
    public string Id = "";
    public string Name = "";
    public string Purpose = "";
    public string Homepage = "";
    public Func<bool> IsInstalled = () => false;
    public Func<IProgress<string>, IProgress<double>, CancellationToken, Task> Install = (_, _, _) => Task.CompletedTask;
}

public static class Components
{
    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(10) };
        // User-Agent de cliente no-navegador: SourceForge redirige directo al archivo
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"OmniHub/{AppInfo.Version}");
        return h;
    }

    public static readonly Component PawnIO = new()
    {
        Id = "pawnio",
        Name = "PawnIO (driver de sensores)",
        Purpose = "Leer temperaturas y controlar ventiladores de la placa base (cualquier marca). También lo usa OpenRGB para RGB de placa/RAM.",
        Homepage = "https://pawnio.eu",
        IsInstalled = () => Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO") != null,
        Install = async (log, prog, ct) =>
        {
            var url = await GitHubLatestAsset("namazso/PawnIO.Setup", a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), ct)
                      ?? "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
            var file = await Download(url, "PawnIO_setup.exe", log, prog, ct);
            log.Report("Abriendo el instalador de PawnIO… acepta los pasos en su ventana.");
            await RunAndWait(file, "", ct);
        }
    };

    public static readonly Component OpenRGB = new()
    {
        Id = "openrgb",
        Name = "OpenRGB (motor de iluminación)",
        Purpose = "Controla el RGB de placas, RAM, GPU, ratones, teclados, auriculares y ventiladores de casi todas las marcas.",
        Homepage = "https://openrgb.org",
        IsInstalled = () => FindOpenRgb() != null,
        Install = async (log, prog, ct) =>
        {
            string? url = null;
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://codeberg.org/api/v1/repos/OpenRGB/OpenRGB/releases/latest", ct));
                foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                {
                    var n = a.GetProperty("name").GetString() ?? "";
                    if (n.Contains("Windows_64") && n.EndsWith(".zip")) { url = a.GetProperty("browser_download_url").GetString(); break; }
                }
            }
            catch (Exception e) { log.Report("No se pudo consultar la última versión (" + e.Message + "), uso la 1.1."); }
            url ??= "https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.1/OpenRGB_1.1_Windows_64_8e16de2.zip";
            var zip = await Download(url, "OpenRGB.zip", log, prog, ct);
            var dest = Path.Combine(AppInfo.ComponentsDir, "OpenRGB");
            log.Report("Descomprimiendo OpenRGB…");
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);
            ZipFile.ExtractToDirectory(zip, dest, true);
            log.Report(FindOpenRgb() != null ? "OpenRGB listo." : "Aviso: no encuentro OpenRGB.exe dentro del zip.");
        }
    };

    public static readonly Component EqualizerApo = new()
    {
        Id = "eqapo",
        Name = "Equalizer APO (ecualizador del sistema)",
        Purpose = "Ecualizador para cualquier auricular o altavoz. En su instalador marca tus auriculares en la lista de dispositivos.",
        Homepage = "https://sourceforge.net/projects/equalizerapo/",
        IsInstalled = () => EqApoConfigDir() != null,
        Install = async (log, prog, ct) =>
        {
            var file = await Download("https://sourceforge.net/projects/equalizerapo/files/latest/download", "EqualizerAPO-setup.exe", log, prog, ct);
            log.Report("Abriendo el instalador de Equalizer APO… al final marca tus auriculares en el Configurator.");
            await RunAndWait(file, "", ct);
        }
    };

    public static readonly Component[] All = { PawnIO, OpenRGB, EqualizerApo };

    // ---------- utilidades ----------

    public static string? FindOpenRgb()
    {
        var cands = new List<string>();
        if (!string.IsNullOrEmpty(Settings.Current.OpenRgbPath)) cands.Add(Settings.Current.OpenRgbPath!);
        try
        {
            var dir = Path.Combine(AppInfo.ComponentsDir, "OpenRGB");
            if (Directory.Exists(dir)) cands.AddRange(Directory.GetFiles(dir, "OpenRGB.exe", SearchOption.AllDirectories));
        }
        catch { }
        cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenRGB", "OpenRGB.exe"));
        return cands.FirstOrDefault(File.Exists);
    }

    public static string? EqApoConfigDir()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO");
            if (k?.GetValue("ConfigPath") is string c && Directory.Exists(c)) return c;
            if (k?.GetValue("InstallPath") is string i && Directory.Exists(Path.Combine(i, "config"))) return Path.Combine(i, "config");
        }
        catch { }
        var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO", "config");
        return Directory.Exists(def) ? def : null;
    }

    static async Task<string?> GitHubLatestAsset(string repo, Func<string, bool> match, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest", ct));
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                if (match(a.GetProperty("name").GetString() ?? "")) return a.GetProperty("browser_download_url").GetString();
        }
        catch { }
        return null;
    }

    static async Task<string> Download(string url, string fileName, IProgress<string> log, IProgress<double> prog, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "OmniHub-setup");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        log.Report($"Descargando {fileName}…");
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(path))
        {
            var buf = new byte[81920];
            long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) prog.Report((double)done / total);
            }
        }
        // Comprobación básica: debe ser un ejecutable (MZ) o un zip (PK), nunca una página web
        var head = new byte[2];
        await using (var f = File.OpenRead(path)) _ = await f.ReadAsync(head, ct);
        bool ok = (head[0] == 'M' && head[1] == 'Z') || (head[0] == 'P' && head[1] == 'K');
        if (!ok) throw new InvalidDataException($"La descarga de {fileName} no es un archivo válido. Descárgalo a mano desde su web oficial.");
        log.Report($"Descargado {fileName} ({SystemTools.Fmt(new FileInfo(path).Length)}).");
        return path;
    }

    static async Task RunAndWait(string exe, string args, CancellationToken ct)
    {
        var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        if (p != null) await p.WaitForExitAsync(ct);
    }
}
