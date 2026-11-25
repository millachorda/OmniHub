using System.Diagnostics;
using System.IO;
using OmniHub.Core;
using OpenRGB.NET;

namespace OmniHub.Services;

/// <summary>
/// Iluminación universal a través del servidor SDK de OpenRGB (cualquier marca).
/// Los efectos se calculan aquí y se envían en modo "Direct/Custom" a cada dispositivo.
/// </summary>
public sealed class RgbService : IDisposable
{
    public static readonly string[] Effects = { "Estático", "Respirar", "Arcoíris", "Ola arcoíris", "Ciclo de color", "Temperatura CPU", "Carátula de la música", "Apagado" };

    OpenRgbClient? _client;
    Device[] _devices = Array.Empty<Device>();
    readonly object _lock = new();
    Thread? _loop;
    volatile bool _run;
    bool _staticSent;

    public LightingState State => Settings.Current.Lighting;
    public bool Connected => _client?.Connected == true;
    public IReadOnlyList<Device> Devices => _devices;
    public string Status { get; private set; } = "Sin conectar";

    /// <summary>Color externo forzado (OBS grabando, etc.). Tiene prioridad sobre el efecto.</summary>
    public (byte r, byte g, byte b)? Override { get; set; }
    public Func<double>? CpuTemp { get; set; }
    public (byte r, byte g, byte b)? MediaColor { get; set; }

    public async Task<bool> ConnectAsync(bool launchIfNeeded = true, bool restart = false)
    {
        // La primera vez que arranca OmniHub (con permisos de administrador) reiniciamos el módulo:
        // si alguien lo abrió sin permisos, no puede leer la RAM ni el RGB de la placa (bus SMBus).
        if (restart && IsAdmin)
        {
            foreach (var p in Process.GetProcessesByName("OpenRGB"))
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            await Task.Delay(500);
        }
        else if (TryConnect()) return true;
        if (!launchIfNeeded) return false;
        var exe = Components.FindOpenRgb();
        if (exe == null) { Status = "Falta el módulo de iluminación"; return false; }
        if (!Process.GetProcessesByName("OpenRGB").Any())
        {
            Status = "Arrancando OpenRGB…";
            try
            {
                // Sin --gui el módulo corre sin ventana ni icono: solo el servidor local para OmniHub
                Process.Start(new ProcessStartInfo(exe, "--server --server-host 127.0.0.1")
                { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch (Exception e) { Status = "No se pudo abrir OpenRGB: " + e.Message; return false; }
        }
        // La detección tarda (la RAM por el bus de la placa puede llevar más de 30 s). Si no llega, el vigilante del bucle sigue intentándolo.
        StartLoop();
        for (int i = 0; i < 90; i++)
        {
            await Task.Delay(1000);
            if (TryConnect()) return true;
        }
        Status = "El módulo de iluminación tarda en responder; se sigue intentando…";
        return false;
    }

    bool TryConnect()
    {
        try
        {
            lock (_lock)
            {
                _client?.Dispose();
                _client = new OpenRgbClient("127.0.0.1", 6742, "OmniHub", true, 1500, 4);
                _devices = _client.GetAllControllerData();
                _client.DeviceListUpdated += (_, _) => Task.Run(Refresh);
                foreach (var d in _devices) SafeCustom(d.Index);
                ApplyZoneSizes();
            }
            Status = $"{_devices.Length} dispositivo(s) RGB";
            _staticSent = false;
            _connected = true;
            StartLoop();
            return true;
        }
        catch
        {
            Status = "Sin conectar";
            return false;
        }
    }

    static bool IsAdmin => new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    /// <summary>LEDs por defecto para un conector ARGB vacío (lo que no exista se ignora sin problema).</summary>
    public const int DefaultArgbLeds = 30;
    public static string ZoneKey(Device d, Zone z) => $"{d.Name}|{z.Name}";

    /// <summary>
    /// Los conectores ARGB de la placa (ventiladores, tiras) llegan con 0 LEDs porque el hardware no
    /// dice cuántos hay. Les damos el número guardado por el usuario o uno por defecto. Llamar con _lock.
    /// </summary>
    void ApplyZoneSizes()
    {
        if (_client == null) return;
        bool changed = false;
        var st = Settings.Current;
        foreach (var d in _devices)
            for (int zi = 0; zi < d.Zones.Length; zi++)
            {
                var z = d.Zones[zi];
                if (z.LedsMax <= z.LedsMin) continue; // no redimensionable
                var key = ZoneKey(d, z);
                int want = st.ZoneLeds.TryGetValue(key, out var n) ? n : z.LedCount == 0 ? DefaultArgbLeds : (int)z.LedCount;
                want = (int)Math.Clamp(want, z.LedsMin, z.LedsMax);
                st.ZoneLeds[key] = want;
                if (want != z.LedCount) { try { _client.ResizeZone(d.Index, zi, want); changed = true; } catch { } }
            }
        if (changed)
        {
            _devices = _client.GetAllControllerData();
            foreach (var d in _devices) SafeCustom(d.Index);
            st.Save();
        }
    }

    /// <summary>Cambia los LEDs de un conector ARGB (p. ej. 3 ventiladores × 8 LEDs = 24).</summary>
    public void SetZoneLeds(string key, int leds)
    {
        Settings.Current.ZoneLeds[key] = leds;
        lock (_lock) { try { ApplyZoneSizes(); } catch { } }
        _staticSent = false;
        Settings.Current.Save();
    }

    public void Refresh()
    {
        try
        {
            lock (_lock)
            {
                if (_client == null) return;
                _devices = _client.GetAllControllerData();
                foreach (var d in _devices) SafeCustom(d.Index);
                ApplyZoneSizes(); // los conectores ARGB llegan a 0 LEDs: sin esto los ventiladores se quedan apagados
            }
            Status = $"{_devices.Length} dispositivo(s) RGB";
            _staticSent = false;
            _fadeRequested = true;
        }
        catch { }
    }

    void SafeCustom(int idx) { try { _client!.SetCustomMode(idx); } catch { } }

    readonly AutoResetEvent _wake = new(false);
    double _lastSend = -10;

    /// <summary>Algo cambió (color, efecto, brillo…): se aplica al momento.</summary>
    public void Changed() { _staticSent = false; _fadeRequested = true; _wake.Set(); }

    void StartLoop()
    {
        if (_loop != null) return;
        _run = true;
        _loop = new Thread(Loop) { IsBackground = true, Name = "RGB" };
        _loop.Start();
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

    const double FrameMs = 1000.0 / 60; // 60 fps con ritmo exacto
    double _nextCheck;

    void Loop()
    {
        var sw = Stopwatch.StartNew();
        bool hiRes = false;
        double nextFrame = 0;
        while (_run)
        {
            double now = sw.Elapsed.TotalSeconds;

            // Vigilante: sin conexión → reintenta solo (y vuelve a abrir el módulo si se cerró);
            // con conexión → si el módulo detectó dispositivos nuevos, se refresca (con LEDs de los conectores)
            if (now >= _nextCheck)
            {
                _nextCheck = now + 3;
                try { Watchdog(); } catch { }
            }

            try { Tick(now); }
            catch { Status = "Conexión con el módulo de iluminación perdida; reconectando…"; _connected = false; }

            if (_moving)
            {
                // El reloj normal de Windows va de 15,6 en 15,6 ms (fotogramas desiguales = tirones).
                // Mientras hay animación se pide precisión de 1 ms y se programa cada fotograma a su hora exacta.
                if (!hiRes) { timeBeginPeriod(1); hiRes = true; nextFrame = sw.Elapsed.TotalMilliseconds; }
                nextFrame += FrameMs;
                double wait = nextFrame - sw.Elapsed.TotalMilliseconds;
                if (wait < -FrameMs * 3) { nextFrame = sw.Elapsed.TotalMilliseconds; wait = 0; } // se quedó atrás: no acumular
                if (wait > 0) _wake.WaitOne((int)Math.Round(wait));
            }
            else
            {
                if (hiRes) { timeEndPeriod(1); hiRes = false; }
                _wake.WaitOne(250);
            }
        }
        if (hiRes) timeEndPeriod(1);
    }

    volatile bool _connected;

    void Watchdog()
    {
        if (_client?.Connected != true || !_connected)
        {
            // ¿Se cerró el módulo? Se vuelve a abrir (solo con permisos de administrador: sin ellos no ve RAM ni placa)
            if (IsAdmin && !Hub.TestMode && !Process.GetProcessesByName("OpenRGB").Any() && Components.FindOpenRgb() is { } exe)
                try
                {
                    Process.Start(new ProcessStartInfo(exe, "--server --server-host 127.0.0.1")
                    { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                }
                catch { }
            TryConnect();
            return;
        }
        // Dispositivos que el módulo ha terminado de detectar después de conectarnos
        int count;
        lock (_lock) count = _client!.GetControllerCount();
        if (count != _devices.Length) Refresh();
    }

    // ---------- Motor de efectos ----------
    // Todo va por "fase" acumulada (cambiar la velocidad no da saltos), el brillo se corrige con gamma
    // (el ojo no ve la luz de forma lineal) y cada cambio de color o efecto se funde en 0,6 s.
    const double Gamma = 2.2, FadeSeconds = 0.6;
    volatile bool _moving;
    double _phase, _lastT;
    double _fadeStart = double.NaN;
    volatile bool _fadeRequested;
    readonly Dictionary<int, (double r, double g, double b)[]> _shown = new();
    Dictionary<int, (double r, double g, double b)[]> _fadeFrom = new();
    (double r, double g, double b) _smCover = (0, 0, 0);
    double _smTemp = double.NaN;

    static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    /// <summary>Intensidad percibida (0–1) → intensidad real del LED.</summary>
    static double Perceived(double x) => Math.Pow(Math.Clamp(x, 0, 1), Gamma);

    void Tick(double t)
    {
        var s = State;
        double dt = Math.Clamp(t - _lastT, 0, 0.25);
        _lastT = t;

        if (_fadeRequested)
        {
            _fadeRequested = false;
            _fadeStart = t;
            _fadeFrom = _shown.ToDictionary(kv => kv.Key, kv => ((double r, double g, double b)[])kv.Value.Clone());
        }
        double fade = double.IsNaN(_fadeStart) ? 1 : Smooth((t - _fadeStart) / FadeSeconds);
        bool fading = fade < 1;
        if (!fading) _fadeStart = double.NaN;

        bool animated = Override == null && s.Effect is not ("Estático" or "Apagado");
        _moving = animated || fading;
        // Fijo: se reenvía por si otra app pisa los colores (cada segundo en modo prioridad, si no cada 5 s)
        if (!_moving && _staticSent && t - _lastSend < (Settings.Current.LightingPriority ? 1 : 5)) return;
        _lastSend = t;

        // Velocidad exponencial: mucha finura en lo lento (1 % ≈ un ciclo cada 30 s, 100 % ≈ 1,4 por segundo)
        double freq = 0.033 * Math.Pow(2, s.Speed / 100.0 * 5.4);
        _phase = (_phase + dt * freq) % 1000;
        double bri = Perceived(s.Brightness / 100.0);
        var c1 = Hex(s.Color1);
        var c2 = Hex(s.Color2);

        // Valores que cambian despacio (temperatura y carátula) se suavizan para no dar saltos
        var temp = CpuTemp?.Invoke() ?? 40;
        _smTemp = double.IsNaN(_smTemp) ? temp : _smTemp + (temp - _smTemp) * Math.Min(1, dt * 0.8);
        var cover = MediaColor is { } mc ? ((double)mc.r, (double)mc.g, (double)mc.b) : c1;
        double k = Math.Min(1, dt * 2.5);
        _smCover = (_smCover.r + (cover.Item1 - _smCover.r) * k, _smCover.g + (cover.Item2 - _smCover.g) * k, _smCover.b + (cover.Item3 - _smCover.b) * k);

        lock (_lock)
        {
            if (_client == null || !_client.Connected) return;
            foreach (var d in _devices)
            {
                if (s.DisabledDevices.Contains(d.Name)) continue;
                int n = d.Leds.Length;
                if (n == 0) continue;

                // Posición de cada LED dentro de su zona (cada ventilador/conector tiene su propia ola)
                var pos = new double[n];
                int start = 0;
                foreach (var z in d.Zones)
                {
                    int len = (int)z.LedCount;
                    for (int j = 0; j < len && start + j < n; j++) pos[start + j] = len > 1 ? (double)j / len : 0;
                    start += len;
                }

                var target = new (double r, double g, double b)[n];
                for (int i = 0; i < n; i++)
                {
                    (double r, double g, double b) c;
                    if (Override is { } o) c = (o.r, o.g, o.b);
                    else c = s.Effect switch
                    {
                        "Apagado" => (0, 0, 0),
                        // Respiración: coseno suave + gamma, sin quedarse "muerta" abajo
                        "Respirar" => Scale(c1, 0.02 + 0.98 * Perceived((1 - Math.Cos(2 * Math.PI * _phase)) / 2)),
                        "Arcoíris" => Hsv(_phase * 360 % 360, 1, 1),
                        // La ola da una vuelta completa por cada ventilador/conector
                        "Ola arcoíris" => Hsv((_phase * 360 + pos[i] * 360) % 360, 1, 1),
                        // De un color a otro por el arcoíris (sin tonos grises a mitad)
                        "Ciclo de color" => HueMix(c1, c2, Smooth((1 - Math.Cos(2 * Math.PI * _phase)) / 2)),
                        "Temperatura CPU" => TempColor(_smTemp),
                        "Carátula de la música" => _smCover,
                        _ => c1,
                    };
                    target[i] = (c.r * bri, c.g * bri, c.b * bri);
                }

                // Fundido desde lo que se veía antes
                if (fading && _fadeFrom.TryGetValue(d.Index, out var from) && from.Length == n)
                    for (int i = 0; i < n; i++)
                        target[i] = (from[i].r + (target[i].r - from[i].r) * fade, from[i].g + (target[i].g - from[i].g) * fade, from[i].b + (target[i].b - from[i].b) * fade);

                _shown[d.Index] = target;
                var cols = new Color[n];
                for (int i = 0; i < n; i++)
                    cols[i] = new Color((byte)Math.Clamp(Math.Round(target[i].r), 0, 255), (byte)Math.Clamp(Math.Round(target[i].g), 0, 255), (byte)Math.Clamp(Math.Round(target[i].b), 0, 255));
                _client.UpdateLeds(d.Index, cols);
            }
        }
        _staticSent = true;
    }

    static (double, double, double) Scale((double r, double g, double b) c, double k) => (c.r * k, c.g * k, c.b * k);

    /// <summary>Mezcla dos colores recorriendo el tono por el camino más corto (como un arcoíris parcial).</summary>
    static (double, double, double) HueMix((double r, double g, double b) a, (double r, double g, double b) b, double k)
    {
        var (h1, s1, v1) = ToHsv(a);
        var (h2, s2, v2) = ToHsv(b);
        double dh = ((h2 - h1 + 540) % 360) - 180;
        return Hsv((h1 + dh * k + 360) % 360, s1 + (s2 - s1) * k, v1 + (v2 - v1) * k);
    }

    static (double h, double s, double v) ToHsv((double r, double g, double b) c)
    {
        double r = c.r / 255, g = c.g / 255, b = c.b / 255;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }

    static (double, double, double) TempColor(double temp)
    {
        // 35 °C azul → 60 °C verde → 80 °C rojo
        double h = temp <= 35 ? 220 : temp >= 85 ? 0 : 220 - (temp - 35) / 50.0 * 220;
        return Hsv(h, 1, 1);
    }

    public static (double r, double g, double b) Hex(string hex)
    {
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return (c.R, c.G, c.B);
        }
        catch { return (0, 229, 255); }
    }

    static (double, double, double) Mix((double r, double g, double b) a, (double r, double g, double b) b, double k) =>
        (a.r + (b.r - a.r) * k, a.g + (b.g - a.g) * k, a.b + (b.b - a.b) * k);

    public static (double r, double g, double b) Hsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x),
            < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x)
        };
        return ((r + m) * 255, (g + m) * 255, (b + m) * 255);
    }

    public void Dispose()
    {
        _run = false;
        try { _client?.Dispose(); } catch { }
    }
}
