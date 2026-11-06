using System.Diagnostics;
using System.Text;
using HidSharp;

namespace OmniHub.Services;

/// <summary>
/// Cliente HID++ 2.0 para ratones Logitech (cable o receptor Lightspeed/Unifying).
/// Permite DPI, tasa de sondeo, modo de perfiles y batería sin G HUB.
/// </summary>
public sealed class HidppDevice : IDisposable
{
    const byte SWID = 0x0A;
    readonly HidStream _s;
    readonly byte _idx;
    readonly Dictionary<ushort, byte> _feat = new();
    readonly object _lock = new();
    public string Name { get; private set; } = "Logitech";
    public int Pid { get; }

    HidppDevice(HidStream s, byte idx, int pid) { _s = s; _idx = idx; Pid = pid; }

    public static List<HidppDevice> Discover()
    {
        var list = new List<HidppDevice>();
        foreach (var dev in DeviceList.Local.GetHidDevices(0x046D))
        {
            try
            {
                if (dev.GetMaxOutputReportLength() != 20) continue; // colección "long" de HID++
                var desc = dev.GetReportDescriptor();
                if (!desc.Reports.Any(r => r.ReportID == 0x11)) continue;
                if (!dev.TryOpen(out var stream)) continue;
                stream.ReadTimeout = 300;
                // 0xFF = dispositivo por cable; 1..6 = ranuras de un receptor
                bool found = false;
                foreach (byte idx in new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 })
                {
                    var h = new HidppDevice(stream, idx, dev.ProductID);
                    if (h.Ping())
                    {
                        h.LoadName();
                        if (h.Feature(0x2201) != 0) // solo nos interesan ratones (DPI ajustable)
                        {
                            list.Add(h);
                            found = true;
                            if (idx == 0xFF) break;
                        }
                    }
                }
                if (!found) stream.Dispose();
            }
            catch (Exception e) { Debug.WriteLine(e); }
        }
        return list;
    }

    bool Ping()
    {
        try { return Call(0x00, 1, 0, 0, 0x5A) != null; } catch { return false; }
    }

    byte[]? Call(byte feat, byte fn, params byte[] args)
    {
        lock (_lock)
        {
            var b = new byte[20];
            b[0] = 0x11; b[1] = _idx; b[2] = feat; b[3] = (byte)((fn << 4) | SWID);
            Array.Copy(args, 0, b, 4, Math.Min(args.Length, 16));
            _s.Write(b);
            var r = new byte[64];
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 700)
            {
                int n;
                try { n = _s.Read(r); } catch (TimeoutException) { break; }
                if (n < 6 || r[1] != _idx) continue;
                if (r[0] == 0x11 && r[2] == feat && r[3] == b[3]) return r[..20];
                if (r[2] == 0xFF && r[3] == feat && r[4] == b[3]) throw new InvalidOperationException($"HID++ error {r[5]}");
                if (r[0] == 0x10 && r[2] == 0x8F) return null; // HID++ 1.0: no existe
            }
            return null;
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

    public void Dispose() { try { _s.Dispose(); } catch { } }
}
