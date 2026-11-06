using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;
using OmniHub.Core;

namespace OmniHub.Services;

/// <summary>Audio universal: volumen/micro con Core Audio y ecualizador con Equalizer APO.</summary>
public sealed class AudioService
{
    readonly MMDeviceEnumerator _en = new();

    public List<MMDevice> Outputs() => _en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
    public List<MMDevice> Inputs() => _en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();

    public MMDevice? DefaultOutput { get { try { return _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); } catch { return null; } } }
    public MMDevice? DefaultInput { get { try { return _en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); } catch { return null; } } }

    public bool ToggleMicMute()
    {
        var m = DefaultInput;
        if (m == null) return false;
        m.AudioEndpointVolume.Mute = !m.AudioEndpointVolume.Mute;
        return m.AudioEndpointVolume.Mute;
    }

    public void SetDefault(MMDevice d)
    {
        var pc = (IPolicyConfig)new PolicyConfigClient();
        pc.SetDefaultEndpoint(d.ID, 0); // consola
        pc.SetDefaultEndpoint(d.ID, 1); // multimedia
        pc.SetDefaultEndpoint(d.ID, 2); // comunicaciones
    }

    // ---------- Equalizer APO ----------
    public bool EqAvailable => Components.EqApoConfigDir() != null;

    /// <summary>Escribe la EQ en OmniHub.txt y la enlaza desde config.txt.</summary>
    public string ApplyEq(double[] bands, string? autoEqFile = null)
    {
        var dir = Components.EqApoConfigDir();
        if (dir == null) return "Equalizer APO no está instalado";
        var sb = new StringBuilder();
        sb.AppendLine("# Generado por OmniHub — no editar a mano");
        if (!string.IsNullOrEmpty(autoEqFile) && File.Exists(autoEqFile))
        {
            // Perfil AutoEq (formato ParametricEQ.txt de Equalizer APO) + ajustes del usuario encima
            sb.AppendLine(File.ReadAllText(autoEqFile));
        }
        var st = Settings.Current;
        string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        sb.AppendLine("Channel: all");
        var max = bands.Max();
        var preamp = -Math.Max(0, max) - 0.5; // margen anti-saturación
        if (bands.Any(b => Math.Abs(b) > 0.01))
        {
            sb.AppendLine(F($"Preamp: {preamp:0.0} dB"));
            sb.Append("GraphicEQ: ");
            sb.AppendLine(string.Join("; ", Presets.EqFreqs.Select((f, i) => F($"{f} {bands[i]:0.0}"))));
        }
        // Graves / agudos
        if (Math.Abs(st.Bass) > 0.05 || Math.Abs(st.Treble) > 0.05)
        {
            sb.AppendLine(F($"Preamp: {-Math.Max(0, Math.Max(st.Bass, st.Treble)):0.0} dB"));
            if (Math.Abs(st.Bass) > 0.05) sb.AppendLine(F($"Filter: ON LSC Fc 105 Hz Gain {st.Bass:0.0} dB Q 0.70"));
            if (Math.Abs(st.Treble) > 0.05) sb.AppendLine(F($"Filter: ON HSC Fc 8000 Hz Gain {st.Treble:0.0} dB Q 0.70"));
        }
        // Anchura estéreo (mid/side): 0 % = mono, 100 % = normal, 200 % = muy abierto
        var w = st.Width / 100.0;
        if (Math.Abs(w - 1) > 0.01)
        {
            double a = (1 + w) / 2, b = (1 - w) / 2;
            sb.AppendLine(F($"Copy: L={a:0.###}*L+{b:0.###}*R R={a:0.###}*R+{b:0.###}*L"));
            if (w > 1) sb.AppendLine(F($"Preamp: {-20 * Math.Log10(a + Math.Abs(b)):0.0} dB"));
        }
        // Crossfeed / escenario: cada oído oye un poco del otro canal, filtrado y retrasado como en unos altavoces
        if (st.Crossfeed > 0.5)
        {
            var k = st.Crossfeed / 100.0 * 0.6;
            sb.AppendLine("Copy: OHX_L=L OHX_R=R");
            sb.AppendLine("Channel: OHX_L OHX_R");
            sb.AppendLine("Filter: ON LP Fc 700 Hz");
            sb.AppendLine("Delay: 0.3 ms");
            sb.AppendLine("Channel: all");
            sb.AppendLine(F($"Copy: L=L+{k:0.###}*OHX_R R=R+{k:0.###}*OHX_L"));
            sb.AppendLine(F($"Preamp: {-20 * Math.Log10(1 + k):0.0} dB"));
        }
        // Balance
        if (Math.Abs(st.Balance) > 0.5)
        {
            var att = -Math.Abs(st.Balance) / 100.0 * 24; // hasta -24 dB en el lado contrario
            sb.AppendLine(st.Balance > 0 ? "Channel: L" : "Channel: R");
            sb.AppendLine(F($"Preamp: {att:0.0} dB"));
            sb.AppendLine("Channel: all");
        }
        if (!string.IsNullOrWhiteSpace(st.EqExtra))
        {
            sb.AppendLine("# Filtros personalizados del usuario");
            sb.AppendLine(st.EqExtra);
            sb.AppendLine("Channel: all");
        }
        if (Math.Abs(st.OutputGain) > 0.05) sb.AppendLine(F($"Preamp: {st.OutputGain:0.0} dB"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "OmniHub.txt"), sb.ToString());
            var cfg = Path.Combine(dir, "config.txt");
            var text = File.Exists(cfg) ? File.ReadAllText(cfg) : "";
            if (!text.Contains("Include: OmniHub.txt", StringComparison.OrdinalIgnoreCase))
            {
                // Desactivamos los ejemplos por defecto de EAPO para que no se sumen
                text = string.Join(Environment.NewLine, text.Split('\n').Select(l => l.TrimEnd('\r'))
                    .Select(l => l.StartsWith("Include: example", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("GraphicEQ:", StringComparison.OrdinalIgnoreCase) ? "# " + l : l));
                text += Environment.NewLine + "Include: OmniHub.txt" + Environment.NewLine;
                File.WriteAllText(cfg, text);
            }
            return "Ecualizador aplicado";
        }
        catch (Exception e) { return "No se pudo escribir la configuración: " + e.Message; }
    }

    // ---------- Modos espaciales (presets de efectos) ----------
    public static readonly (string name, string glyph, double width, double cross, double bass, double treble, string hint)[] SpatialModes =
    {
        ("Desactivado", "", 100, 0, 0, 0, "Sonido tal cual"),
        ("Juego", "", 135, 25, -1, 2, "Escena amplia para ubicar pasos y disparos"),
        ("Cine", "", 150, 40, 3, 1, "Envolvente, con más cuerpo en explosiones"),
        ("Música", "", 110, 30, 1, 0.5, "Natural, como escuchar unos altavoces"),
    };

    public static void ApplySpatial(string mode)
    {
        var st = Settings.Current;
        st.SpatialMode = mode;
        var m = SpatialModes.FirstOrDefault(x => x.name == mode);
        if (m.name == null) return;
        st.Width = m.width; st.Crossfeed = m.cross; st.Bass = m.bass; st.Treble = m.treble;
    }

    // ---------- AutoEq: corrección medida para miles de auriculares ----------
    public record AutoEqEntry(string Name, string Path, string Source);
    const string AutoEqBase = "https://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/results/";
    static List<AutoEqEntry>? _index;

    public static async Task<List<AutoEqEntry>> AutoEqIndexAsync()
    {
        if (_index != null) return _index;
        var cache = Path.Combine(AppInfo.DataDir, "autoeq_index.md");
        string text;
        try
        {
            if (File.Exists(cache) && (DateTime.Now - File.GetLastWriteTime(cache)).TotalDays < 14) text = await File.ReadAllTextAsync(cache);
            else
            {
                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("OmniHub");
                text = await http.GetStringAsync(AutoEqBase + "INDEX.md");
                await File.WriteAllTextAsync(cache, text);
            }
        }
        catch { return new(); }
        var rx = new System.Text.RegularExpressions.Regex(@"^- \[(?<n>[^\]]+)\]\(\./(?<p>[^)]+)\) by (?<s>\S+)");
        _index = text.Split('\n').Select(l => rx.Match(l.Trim())).Where(m => m.Success)
            .Select(m => new AutoEqEntry(m.Groups["n"].Value, m.Groups["p"].Value, m.Groups["s"].Value)).ToList();
        return _index;
    }

    /// <summary>Busca el modelo a partir de un texto o del nombre del dispositivo de Windows.</summary>
    public static async Task<List<AutoEqEntry>> SearchAutoEqAsync(string query)
    {
        var idx = await AutoEqIndexAsync();
        // "Altavoces (INZONE H3)" -> "INZONE H3"
        var m = System.Text.RegularExpressions.Regex.Match(query, @"\(([^)]+)\)");
        var q = (m.Success ? m.Groups[1].Value : query).Trim();
        q = System.Text.RegularExpressions.Regex.Replace(q, @"^\d+-\s*", ""); // "2- Wireless…"
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToArray();
        if (words.Length == 0) return new();
        int SourceRank(string s) => s switch { "oratory1990" => 0, "crinacle" => 1, "Rtings" => 2, "Innerfidelity" => 3, _ => 4 };
        return idx.Where(e => words.All(w => e.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Name.Length).ThenBy(e => SourceRank(e.Source)).Take(30).ToList();
    }

    public static async Task<string> DownloadAutoEqAsync(AutoEqEntry e)
    {
        var name = Uri.UnescapeDataString(e.Path.Split('/')[^1]);
        var url = AutoEqBase + e.Path + "/" + Uri.EscapeDataString(name + " ParametricEQ.txt");
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OmniHub");
        var txt = await http.GetStringAsync(url);
        if (!txt.Contains("Filter")) throw new InvalidDataException("Perfil AutoEq no válido");
        var file = Path.Combine(AppInfo.DataDir, "autoeq_" + string.Concat(name.Split(Path.GetInvalidFileNameChars())) + ".txt");
        await File.WriteAllTextAsync(file, txt);
        Settings.Current.AutoEqFile = file;
        Settings.Current.AutoEqName = $"{e.Name} ({e.Source})";
        return file;
    }

    /// <summary>Primera vez: aplica sola la corrección de los auriculares detectados si hay coincidencia clara.</summary>
    public async Task AutoConfigureAsync()
    {
        var st = Settings.Current;
        if (st.AutoEqTried || !string.IsNullOrEmpty(st.AutoEqFile) || !EqAvailable) return;
        st.AutoEqTried = true;
        var dev = DefaultOutput?.FriendlyName;
        if (dev == null) return;
        var hits = await SearchAutoEqAsync(dev);
        if (hits.Count == 0) return;
        try
        {
            await DownloadAutoEqAsync(hits[0]);
            ApplyEq(st.Eq, st.AutoEqFile);
            st.Save();
            Hub.Toast($"Auriculares configurados: {hits[0].Name}");
        }
        catch { }
    }

    public static void OpenWindowsSpatial() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe", "shell32.dll,Control_RunDLL mmsys.cpl,,0") { UseShellExecute = true });

    // ---------- COM no documentado de Windows para cambiar el dispositivo por defecto ----------
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility();
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClient { }
}
