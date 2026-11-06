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
        // La detección de dispositivos de OpenRGB tarda unos segundos
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            if (TryConnect()) return true;
        }
        Status = "OpenRGB no responde (activa 'SDK Server' en OpenRGB)";
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
            }
            Status = $"{_devices.Length} dispositivo(s) RGB";
            _staticSent = false;
        }
        catch { }
    }

    void SafeCustom(int idx) { try { _client!.SetCustomMode(idx); } catch { } }

    public void Changed() => _staticSent = false;

    void StartLoop()
    {
        if (_loop != null) return;
        _run = true;
        _loop = new Thread(Loop) { IsBackground = true, Name = "RGB" };
        _loop.Start();
    }

    void Loop()
    {
        var sw = Stopwatch.StartNew();
        while (_run)
        {
            try { Tick(sw.Elapsed.TotalSeconds); }
            catch { Status = "Conexión con OpenRGB perdida"; Thread.Sleep(2000); try { TryReconnectQuiet(); } catch { } }
            Thread.Sleep(33); // ~30 fps
        }
    }

    void TryReconnectQuiet()
    {
        lock (_lock)
        {
            if (_client?.Connected == true) return;
            _client?.Dispose();
            _client = new OpenRgbClient("127.0.0.1", 6742, "OmniHub", true, 1500, 4);
            _devices = _client.GetAllControllerData();
            foreach (var d in _devices) SafeCustom(d.Index);
            _staticSent = false;
            Status = $"{_devices.Length} dispositivo(s) RGB";
        }
    }

    void Tick(double t)
    {
        var s = State;
        bool animated = Override == null && s.Effect is not ("Estático" or "Apagado");
        if (!animated && _staticSent) { if ((int)(t * 30) % 150 != 0) return; } // reenvío cada ~5 s por si otra app pisa

        double speed = 0.15 + s.Speed / 100.0 * 1.6;
        double bri = s.Brightness / 100.0;
        var c1 = Hex(s.Color1);
        var c2 = Hex(s.Color2);

        lock (_lock)
        {
            if (_client == null || !_client.Connected) return;
            foreach (var d in _devices)
            {
                if (s.DisabledDevices.Contains(d.Name)) continue;
                int n = d.Leds.Length;
                if (n == 0) continue;
                var cols = new Color[n];
                for (int i = 0; i < n; i++)
                {
                    (double r, double g, double b) c;
                    if (Override is { } o) c = (o.r, o.g, o.b);
                    else c = s.Effect switch
                    {
                        "Apagado" => (0, 0, 0),
                        "Respirar" => Mix((0, 0, 0), c1, (Math.Sin(t * speed * 2) + 1) / 2),
                        "Arcoíris" => Hsv(t * speed * 60 % 360, 1, 1),
                        "Ola arcoíris" => Hsv((t * speed * 90 + i * 360.0 / Math.Max(n, 12)) % 360, 1, 1),
                        "Ciclo de color" => Mix(c1, c2, (Math.Sin(t * speed) + 1) / 2),
                        "Temperatura CPU" => TempColor(CpuTemp?.Invoke() ?? 40),
                        "Carátula de la música" => MediaColor is { } m ? (m.r, m.g, m.b) : c1,
                        _ => c1,
                    };
                    cols[i] = new Color((byte)Math.Clamp(c.r * bri, 0, 255), (byte)Math.Clamp(c.g * bri, 0, 255), (byte)Math.Clamp(c.b * bri, 0, 255));
                }
                _client.UpdateLeds(d.Index, cols);
            }
        }
        _staticSent = true;
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
