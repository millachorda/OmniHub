using OmniHub.Core;

namespace OmniHub.Services;

/// <summary>Punto central: todos los servicios + perfiles + bucle de fondo.</summary>
public static class Hub
{
    public static RgbService Rgb { get; } = new();
    public static FanService Fans { get; } = new();
    public static AudioService Audio { get; } = new();
    public static MediaService Media { get; } = new();

    public static Action? ShowMain;
    /// <summary>Modo de prueba (--test): no se bloquea por instancia única.</summary>
    public static bool TestMode;
    public static Action<string>? ToastHandler;
    public static event Action? Ticked;
    public static string ActiveProfile { get; private set; } = "";
    public static string AutoProfileReason => "";

    static System.Windows.Threading.DispatcherTimer? _timer;
    static bool _started;
    static int _tick;

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

        if (!TestMode) await Task.Run(RestoreMouseOnce);

        log?.Report("Leyendo sensores…");
        await Task.Run(Fans.Open);
        Rgb.CpuTemp = () => Fans.CpuTemp;

        log?.Report("Conectando con multimedia…");
        await Media.InitAsync();

        log?.Report("Conectando iluminación…");
        // en segundo plano: el módulo de iluminación tarda unos segundos en detectar todo.
        // Se reinicia con nuestros permisos de administrador para que vea RAM y placa; en modo prueba no se toca.
        _ = TestMode ? Rgb.ConnectAsync(launchIfNeeded: false) : Rgb.ConnectAsync(restart: true);

        if (Audio.EqAvailable)
        {
            Audio.ApplyEq(st.Eq, st.AutoEqFile);
            _ = Audio.AutoConfigureAsync();
        }

        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    /// <summary>
    /// OmniHub ya no gestiona el ratón. Las versiones anteriores (y G HUB) dejaban los botones G
    /// desactivados en el propio ratón; aquí, una sola vez, se devuelve el ratón a su memoria
    /// interna para que funcione solo, sin ningún programa. Si G HUB está abierto, no se toca.
    /// </summary>
    static void RestoreMouseOnce()
    {
        var st = Settings.Current;
        if (st.MouseRestored) return;
        if (System.Diagnostics.Process.GetProcessesByName("lghub").Length > 0 ||
            System.Diagnostics.Process.GetProcessesByName("lghub_agent").Length > 0) return;
        try
        {
            foreach (var d in HidppDevice.Discover())
            {
                try
                {
                    if (d.HasButtonSpy)
                    {
                        d.StopSpy();
                        var n = Math.Min(16, d.ButtonCount);
                        d.SetRemap(Enumerable.Range(1, 16).Select(i => (byte)(i <= n ? i : 0)).ToArray());
                    }
                    if (d.HasOnboard) d.HostMode = false; // modo memoria interna: los botones hacen lo suyo
                }
                finally { d.Dispose(); }
            }
        }
        catch { }
        st.MouseRestored = true;
        st.Save();
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
        };
    }

    static LightingState Clone(LightingState l) => new()
    {
        Effect = l.Effect, Color1 = l.Color1, Color2 = l.Color2, Speed = l.Speed, Brightness = l.Brightness,
        DisabledDevices = new List<string>(l.DisabledDevices)
    };

    public static void Shutdown()
    {
        _timer?.Stop();
        try { Fans.Dispose(); } catch { }
        try { Rgb.Dispose(); } catch { }
    }
}
