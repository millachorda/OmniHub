using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace OmniHub.Core;

/// <summary>Instalación y actualizaciones desde las Releases del repositorio público de GitHub.</summary>
public static class Updater
{
    public const string Repo = "millachorda/OmniHub";
    public const string AssetName = "OmniHub.exe";
    public static string RepoUrl => $"https://github.com/{Repo}";

    static readonly HttpClient Http = Make();
    static HttpClient Make()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"OmniHub/{AppInfo.Version}");
        return h;
    }

    public record Release(Version Version, string Tag, string DownloadUrl, string Notes);

    public static async Task<Release?> LatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
                if (string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                    return new Release(v, tag, a.GetProperty("browser_download_url").GetString()!, root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "");
        }
        catch { }
        return null;
    }

    public static Version Current => Version.Parse(AppInfo.Version);

    /// <summary>Descarga el .exe completo de la última release a la ruta indicada.</summary>
    public static async Task DownloadAsync(Release r, string dest, IProgress<double>? prog, CancellationToken ct = default)
    {
        var tmp = dest + ".part";
        using (var resp = await Http.GetAsync(r.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            var buf = new byte[1 << 16]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) prog?.Report((double)done / total);
            }
        }
        var head = new byte[2];
        await using (var f = File.OpenRead(tmp)) _ = await f.ReadAsync(head, ct);
        if (head[0] != 'M' || head[1] != 'Z') { File.Delete(tmp); throw new InvalidDataException("La descarga desde GitHub no es un ejecutable válido."); }
        File.Move(tmp, dest, true);
    }

    /// <summary>Sustituye el .exe instalado por la nueva versión y lo vuelve a abrir.</summary>
    public static async Task SelfUpdateAsync(Release r, IProgress<double>? prog)
    {
        var exe = AppInfo.ExePath;
        var next = exe + ".new";
        await DownloadAsync(r, next, prog);
        // cmd espera a que salgamos, reemplaza el archivo y relanza
        var cmd = $"/c ping 127.0.0.1 -n 3 > nul & move /y \"{next}\" \"{exe}\" & start \"\" \"{exe}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
        System.Windows.Application.Current.Shutdown();
    }
}
