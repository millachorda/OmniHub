using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using HidSharp;

namespace OmniHub.Services;

/// <summary>
/// Cliente HID++ 2.0 para ratones Logitech (cable o receptor Lightspeed/Unifying).
/// DPI, tasa de sondeo, perfiles, batería y botones G (Mouse Button Spy 0x8110) sin G HUB.
/// Un hilo lee todo lo que manda el ratón: respuestas a peticiones y eventos de botones.
/// </summary>
public sealed class HidppDevice : IDisposable
{
    const byte SWID = 0x0A;
    readonly HidStream _s;
    byte _idx;
    readonly Dictionary<ushort, byte> _feat = new();
    readonly object _callLock = new();
    readonly Thread _reader;
    volatile bool _run = true;
    readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]?>> _pending = new();

    public string Name { get; private set; } = "Logitech";
    public int Pid { get; }

    /// <summary>Máscara de botones pulsados (bit 0 = botón 1) cada vez que cambia.</summary>
    public event Action<int>? ButtonsChanged;

    HidppDevice(HidStream s, int pid)
    {
        _s = s; Pid = pid;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "HID++" };
        _reader.Start();
    }

    public static List<HidppDevice> Discover()
    {
        var list = new List<HidppDevice>();
        foreach (var dev in DeviceList.Local.GetHidDevices(0x046D))
        {
            try
            {
                if (dev.GetMaxOutputReportLength() != 20) continue; // colección "long" de HID++
                if (!dev.GetReportDescriptor().Reports.Any(r => r.ReportID == 0x11)) continue;
                if (!dev.TryOpen(out var stream)) continue;
                stream.ReadTimeout = 250;
                var h = new HidppDevice(stream, dev.ProductID);
                // 0xFF = por cable; 1..6 = ranuras de un receptor
                bool found = false;
                foreach (byte idx in new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 })
                {
                    h._idx = idx; h._feat.Clear();
                    if (h.Ping() && h.Feature(0x2201) != 0) { h.LoadName(); found = true; break; }
                }
                if (found) list.Add(h); else h.Dispose();
            }
            catch (Exception e) { Debug.WriteLine(e); }
        }
        return list;
    }

    void ReadLoop()
    {
        var r = new byte[64];
        int lastMask = -1;
        while (_run)
        {
            int n;
            try { n = _s.Read(r); }
            catch (TimeoutException) { continue; }
            catch { if (!_run) break; Thread.Sleep(200); continue; }
            if (n < 6 || r[1] != _idx) continue;

            // Error HID++ 2.0: [11, idx, FF, feat, fn|swid, código]
            if (r[2] == 0xFF && _pending.TryRemove((r[3] << 8) | r[4], out var err)) { err.TrySetException(new InvalidOperationException($"HID++ error {r[5]}")); continue; }
            if (r[0] == 0x10 && r[2] == 0x8F && _pending.TryRemove((r[3] << 8) | r[4], out var old)) { old.TrySetResult(null); continue; }

            if ((r[3] & 0x0F) == SWID && _pending.TryRemove((r[2] << 8) | r[3], out var tcs)) { tcs.TrySetResult(r[..20]); continue; }

            // Eventos (swid 0): botones del espía 0x8110
            if ((r[3] & 0x0F) == 0 && _spyIdx != 0 && r[2] == _spyIdx && (r[3] >> 4) == 0)
            {
                int mask = (r[4] << 8) | r[5];
                if (mask != lastMask) { lastMask = mask; try { ButtonsChanged?.Invoke(mask); } catch { } }
            }
        }
    }

    bool Ping()
    {
        try { return Call(0x00, 1, 0, 0, 0x5A) != null; } catch { return false; }
    }

    byte[]? Call(byte feat, byte fn, params byte[] args)
    {
        lock (_callLock)
        {
            var b = new byte[20];
            b[0] = 0x11; b[1] = _idx; b[2] = feat; b[3] = (byte)((fn << 4) | SWID);
            Array.Copy(args, 0, b, 4, Math.Min(args.Length, 16));
            var key = (feat << 8) | b[3];
            var tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[key] = tcs;
            try { _s.Write(b); }
            catch { _pending.TryRemove(key, out _); return null; }
            if (!tcs.Task.Wait(700)) { _pending.TryRemove(key, out _); return null; }
            return tcs.Task.Result; // relanza el error HID++ si lo hubo
        }
    }

    public byte Feature(ushort id)
    {
        if (_feat.TryGetValue(id, out var i)) return i;
        byte idx = 0;
        try { var r = Call(0x00, 0, (byte)(id >> 8), (byte)id); idx = r?[4] ?? 0; } catch { }
        _feat[id] = idx;
        return idx;
    }

    void LoadName()
    {
        var f = Feature(0x0005);
        if (f == 0) return;
        try
        {
            var len = Call(f, 0)?[4] ?? 0;
            var sb = new StringBuilder();
            while (sb.Length < len)
            {
                var r = Call(f, 1, (byte)sb.Length);
                if (r == null) break;
                for (int k = 4; k < 20 && sb.Length < len; k++) sb.Append((char)r[k]);
            }
            if (sb.Length > 0) Name = "Logitech " + sb.ToString().Trim('\0', ' ');
        }
        catch { }
    }

    // ---------- DPI (0x2201) ----------
    public (int min, int max, int step) DpiRange()
    {
        var f = Feature(0x2201);
        if (f == 0) return (200, 8000, 50);
        try
        {
            var r = Call(f, 1, 0);
            if (r == null) return (200, 8000, 50);
            var vals = new List<int>(); int step = 50;
            for (int k = 5; k + 1 < 20; k += 2)
            {
                int v = (r[k] << 8) | r[k + 1];
                if (v == 0) break;
                if ((v & 0xE000) == 0xE000) step = v & 0x1FFF; else vals.Add(v);
            }
            if (vals.Count == 0) return (200, 8000, 50);
            return (vals.Min(), vals.Max(), Math.Max(1, step));
        }
        catch { return (200, 8000, 50); }
    }

    public int GetDpi()
    {
        var f = Feature(0x2201);
        if (f == 0) return 0;
        try { var r = Call(f, 2, 0); return r == null ? 0 : (r[5] << 8) | r[6]; } catch { return 0; }
    }

    public bool SetDpi(int dpi)
    {
        var f = Feature(0x2201);
        if (f == 0) return false;
        try { return Call(f, 3, 0, (byte)(dpi >> 8), (byte)dpi) != null; } catch { return false; }
    }

    // ---------- Tasa de sondeo (0x8060) ----------
    public int[] ReportRates()
    {
        var f = Feature(0x8060);
        if (f == 0) return Array.Empty<int>();
        try
        {
            var r = Call(f, 0);
            if (r == null) return Array.Empty<int>();
            var mask = r[4];
            return Enumerable.Range(0, 8).Where(b => (mask & (1 << b)) != 0).Select(b => 1000 / (b + 1)).ToArray();
        }
        catch { return Array.Empty<int>(); }
    }

    public int GetReportRate()
    {
        var f = Feature(0x8060);
        if (f == 0) return 0;
        try { var r = Call(f, 1); return r == null || r[4] == 0 ? 0 : 1000 / r[4]; } catch { return 0; }
    }

    public string? SetReportRate(int hz)
    {
        var f = Feature(0x8060);
        if (f == 0) return "No soportado";
        try { Call(f, 2, (byte)Math.Max(1, 1000 / hz)); return null; }
        catch (Exception e) { return e.Message + " (prueba a activar el modo software)"; }
    }

    // ---------- Perfiles internos (0x8100) ----------
    public bool HasOnboard => Feature(0x8100) != 0;
    public bool? HostMode
    {
        get
        {
            var f = Feature(0x8100);
            if (f == 0) return null;
            try { return Call(f, 2)?[4] == 2; } catch { return null; }
        }
        set
        {
            var f = Feature(0x8100);
            if (f == 0 || value == null) return;
            try { Call(f, 1, (byte)(value.Value ? 2 : 1)); } catch { }
        }
    }

    // ---------- Botones G: Mouse Button Spy (0x8110) ----------
    public bool HasButtonSpy => Feature(0x8110) != 0;

    public int ButtonCount
    {
        get
        {
            var f = Feature(0x8110);
            if (f == 0) return 0;
            try { return Call(f, 0)?[4] ?? 0; } catch { return 0; }
        }
    }

    volatile byte _spyIdx;
    public bool StartSpy() { var f = Feature(0x8110); _spyIdx = f; try { return f != 0 && Call(f, 1) != null; } catch { return false; } }
    public void StopSpy() { var f = Feature(0x8110); _spyIdx = 0; try { if (f != 0) Call(f, 2); } catch { } }

    public byte[]? GetRemap()
    {
        var f = Feature(0x8110);
        if (f == 0) return null;
        try { return Call(f, 3)?[4..20]; } catch { return null; }
    }

    /// <summary>
    /// Tabla física→lógica: valor = botón que ve Windows, 0 = el ratón no hace nada (lo gestiona OmniHub).
    /// Los botones 1-3 (izquierdo, derecho, rueda) nunca se tocan.
    /// </summary>
    public bool SetRemap(byte[] map)
    {
        var f = Feature(0x8110);
        if (f == 0) return false;
        var m = new byte[16];
        Array.Copy(map, m, Math.Min(16, map.Length));
        m[0] = 1; m[1] = 2; m[2] = 3;
        try { return Call(f, 4, m) != null; } catch { return false; }
    }

    // ---------- Batería (0x1004 unificada o 0x1000) ----------
    public int? Battery()
    {
        try
        {
            var f = Feature(0x1004);
            if (f != 0) { var r = Call(f, 1); if (r != null) return r[4]; }
            f = Feature(0x1000);
            if (f != 0) { var r = Call(f, 0); if (r != null) return r[4]; }
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        _run = false;
        try { _s.Dispose(); } catch { }
    }
}
