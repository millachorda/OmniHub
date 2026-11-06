using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OmniHub.Core;

namespace OmniHub.Services;

/// <summary>Punto central: todos los servicios + perfiles + bucle de fondo.</summary>
public static class Hub
{
    public static RgbService Rgb { get; } = new();
    public static FanService Fans { get; } = new();
    public static MouseService Mouse { get; } = new();
    public static AudioService Audio { get; } = new();
    public static MediaService Media { get; } = new();
    public static ObsService Obs { get; } = new();
    public static DiscordService Discord { get; } = new();

    public static Action? ShowMain;
    public static Action<string>? ToastHandler;
    public static event Action? Ticked;
    public static string ActiveProfile { get; private set; } = "";
    public static string AutoProfileReason { get; private set; } = "";

    static System.Windows.Threading.DispatcherTimer? _timer;
    static bool _started;
    static int _tick;
    static string _autoApplied = "";

    public static void Toast(string text) => ToastHandler?.Invoke(text);

    public static async Task StartAsync(IProgress<string>? log = null)
    {
        if (_started) return;
        _started = true;
        var st = Settings.Current;

        if (st.CloseVendorApps)
        {
            log?.Report("Cerrando apps del fabricante…");
            await Task.Run(VendorApps.CloseAll);
        }

        log?.Report("Leyendo sensores…");
        await Task.Run(Fans.Open);
        Rgb.CpuTemp = () => Fans.CpuTemp;

        log?.Report("Buscando ratones…");
        await Task.Run(Mouse.Discover);
        Mouse.InstallHook();

        log?.Report("Conectando con multimedia…");
        await Media.InitAsync();

        log?.Report("Conectando iluminación…");
        _ = Rgb.ConnectAsync(); // en segundo plano: OpenRGB puede tardar


        if (Audio.EqAvailable)
        {
            Audio.ApplyEq(st.Eq, st.AutoEqFile);
            _ = Audio.AutoConfigureAsync();
        }

        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    static async Task TickAsync()
    {
        _tick++;
        var st = Settings.Current;
        if (_tick % 2 == 0) await Task.Run(Fans.Tick);
        await Media.PollAsync();
        Rgb.MediaColor = Media.CoverColor;
        if (st.MediaLights && Media.Playing && Media.CoverColor != null && st.Lighting.Effect != "Carátula de la música")
        { st.Lighting.Effect = "Carátula de la música"; Rgb.Changed(); }
        if (Rgb.State.Effect == "Carátula de la música") Rgb.Changed();

        Ticked?.Invoke();
    }

    static void UpdateOverride()
    {
        var st = Settings.Current;
        Rgb.Override = st.ObsRecordingLights && (Obs.Recording || Obs.Streaming)
            ? ((byte)255, (byte)0, (byte)30)
            : null;
        Rgb.Changed();
    }

    // ---------- Perfiles ----------
    public static void ApplyProfile(Profile p, bool silent = false)
    {
        var st = Settings.Current;
        st.Lighting = Clone(p.Lighting);
        st.FanPreset = p.FanPreset;
        st.FanModes.Clear();
        st.Eq = (double[])p.Eq.Clone();
        st.EqPreset = p.EqPreset;
        st.Save();
        Rgb.Changed();
        if (p.Dpi > 0) Mouse.SetDpi(p.Dpi);
        if (Audio.EqAvailable) Audio.ApplyEq(st.Eq, st.AutoEqFile);
        ActiveProfile = p.Name;
        if (!silent) Toast($"Perfil «{p.Name}» aplicado");
    }

    public static Profile Snapshot(string name)
    {
        var st = Settings.Current;
        return new Profile
        {
            Name = name, Lighting = Clone(st.Lighting), FanPreset = st.FanPreset,
            Eq = (double[])st.Eq.Clone(), EqPreset = st.EqPreset,
            Dpi = Mouse.Active?.GetDpi() ?? 0,
        };
    }

    static LightingState Clone(LightingState l) => new()
    {
        Effect = l.Effect, Color1 = l.Color1, Color2 = l.Color2, Speed = l.Speed, Brightness = l.Brightness,
        DisabledDevices = new List<string>(l.DisabledDevices)
    };

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    /// <summary>Perfil automático según la app en primer plano (juegos, OBS, Spotify…).</summary>
    static void AutoProfile()
    {
        var st = Settings.Current;
        string fg = "";
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            fg = Process.GetProcessById((int)pid).ProcessName;
        }
        catch { }
        if (fg.Equals("OmniHub", StringComparison.OrdinalIgnoreCase)) return;
        var rule = st.AppRules.FirstOrDefault(r => r.Process.Length > 0 && fg.Equals(r.Process.Replace(".exe", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase));
        var target = rule?.Profile ?? st.DefaultProfile;
        if (string.IsNullOrEmpty(target) || target == _autoApplied) return;
        var p = st.Profiles.FirstOrDefault(x => x.Name == target);
        if (p == null) return;
        _autoApplied = target;
        AutoProfileReason = rule != null ? $"por {fg}" : "por defecto";
        ApplyProfile(p, silent: false);
    }

    public static void Shutdown()
    {
        _timer?.Stop();
        try { Fans.Dispose(); } catch { }
        try { Rgb.Dispose(); } catch { }
        try { Mouse.Dispose(); } catch { }
        try { Discord.Dispose(); } catch { }
        try { Obs.Disconnect(); } catch { }
    }
}
