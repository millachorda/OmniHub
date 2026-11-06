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
    public Func<IProgress<string>, Task> Uninstall = _ => Task.CompletedTask;
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

    /// <summary>Los módulos se descargan de la release "components" del propio repositorio de OmniHub.</summary>
    static string Mirror(string file) => $"https://github.com/{Updater.Repo}/releases/download/components/{file}";

    public static readonly Component PawnIO = new()
    {
        Id = "pawnio",
        Name = "Módulo de sensores",
        Purpose = "Temperaturas y control de ventiladores de la placa base (cualquier marca), y RGB de placa y RAM.",
        Homepage = "https://pawnio.eu",
        IsInstalled = () => Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO") != null,
        Install = async (log, prog, ct) =>
        {
            var file = await DownloadAny(new[] { Mirror("PawnIO_setup.exe"), "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe" }, "PawnIO_setup.exe", log, prog, ct);
            // Guardamos el instalador dentro de OmniHub para poder desinstalarlo luego en silencio
            Directory.CreateDirectory(AppInfo.ComponentsDir);
            File.Copy(file, Path.Combine(AppInfo.ComponentsDir, "PawnIO_setup.exe"), true);
            log.Report("Instalando el módulo de sensores en segundo plano…");
            var code = await RunAndWait(file, "-install -silent", ct);
            log.Report(Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO") != null
                ? "Módulo de sensores listo." : $"El módulo de sensores terminó con código {code}; puede que necesite reiniciar.");
        },
        Uninstall = async log =>
        {
            var setup = Path.Combine(AppInfo.ComponentsDir, "PawnIO_setup.exe");
            if (!File.Exists(setup))
                setup = await DownloadAny(new[] { Mirror("PawnIO_setup.exe") }, "PawnIO_setup.exe", log, new Progress<double>(), CancellationToken.None);
            await RunAndWait(setup, "-uninstall -silent", CancellationToken.None);
        }
    };

    public static readonly Component OpenRGB = new()
    {
        Id = "openrgb",
        Name = "Módulo de iluminación",
        Purpose = "RGB de placas, RAM, gráficas, ratones, teclados, auriculares y ventiladores de casi todas las marcas. Va dentro de la carpeta de OmniHub y funciona sin ventana.",
        Homepage = "https://openrgb.org",
        IsInstalled = () => FindOpenRgb() != null,
        Install = async (log, prog, ct) =>
        {
            var zip = await DownloadAny(new[] { Mirror("OpenRGB-Windows-64.zip"), "https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.1/OpenRGB_1.1_Windows_64_8e16de2.zip" }, "OpenRGB.zip", log, prog, ct);
            var dest = Path.Combine(AppInfo.ComponentsDir, "OpenRGB");
            log.Report("Preparando el módulo de iluminación…");
            foreach (var p in Process.GetProcessesByName("OpenRGB")) try { p.Kill(); p.WaitForExit(3000); } catch { }
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);
            ZipFile.ExtractToDirectory(zip, dest, true);
            log.Report(FindOpenRgb() != null ? "Módulo de iluminación listo." : "Aviso: el módulo de iluminación no se descomprimió bien.");
        },
        Uninstall = log =>
        {
            foreach (var p in Process.GetProcessesByName("OpenRGB")) try { p.Kill(); p.WaitForExit(3000); } catch { }
            try { Directory.Delete(Path.Combine(AppInfo.ComponentsDir, "OpenRGB"), true); } catch { }
            return Task.CompletedTask;
        }
    };

    public static readonly Component EqualizerApo = new()
    {
        Id = "eqapo",
        Name = "Módulo de audio",
        Purpose = "Ecualizador, corrección de auriculares y audio espacial para cualquier auricular o altavoz.",
        Homepage = "https://sourceforge.net/projects/equalizerapo/",
        IsInstalled = () => EqApoConfigDir() != null,
        Install = async (log, prog, ct) =>
        {
            var file = await DownloadAny(new[] { Mirror("EqualizerAPO-setup.exe"), "https://sourceforge.net/projects/equalizerapo/files/latest/download" }, "EqualizerAPO-setup.exe", log, prog, ct);
            log.Report("Instalando el módulo de audio en segundo plano…");
            await RunAndWait(file, "/S", ct);
            log.Report(EqApoConfigDir() != null ? "Módulo de audio listo." : "El módulo de audio no se pudo instalar.");
            if (EqApoConfigDir() != null && EqApoDevices().Count == 0)
                log.Report("Nota: falta activarlo en tus auriculares. OmniHub te lo pedirá en «Audio» con un solo clic.");
        },
        Uninstall = async log =>
        {
            var dir = EqApoConfigDir();
            var un = dir == null ? null : Path.Combine(Path.GetDirectoryName(dir)!, "Uninstall.exe");
            if (un != null && File.Exists(un)) await RunAndWait(un, "/S", CancellationToken.None);
        }
    };

    static async Task<string> DownloadAny(string[] urls, string fileName, IProgress<string> log, IProgress<double> prog, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var u in urls)
        {
            try { return await Download(u, fileName, log, prog, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { last = e; }
        }
        throw last ?? new Exception("Sin conexión");
    }

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

    /// <summary>Ejecuta un instalador oculto, sin ventanas, y espera a que termine.</summary>
    static async Task<int> RunAndWait(string exe, string args, CancellationToken ct)
    {
        var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        });
        if (p == null) return -1;
        using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
        to.CancelAfter(TimeSpan.FromMinutes(5));
        try { await p.WaitForExitAsync(to.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return -2; }
        return p.ExitCode;
    }

    /// <summary>GUIDs (en minúsculas) de los dispositivos de audio donde Equalizer APO está activo.</summary>
    public static HashSet<string> EqApoDevices()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO\Child APOs");
            if (k != null) foreach (var n in k.GetSubKeyNames()) set.Add(n.Trim('{', '}'));
        }
        catch { }
        return set;
    }

    public static string? EqApoDeviceSelector()
    {
        var dir = EqApoConfigDir();
        var exe = dir == null ? null : Path.Combine(Path.GetDirectoryName(dir)!, "DeviceSelector.exe");
        return exe != null && File.Exists(exe) ? exe : null;
    }
}
