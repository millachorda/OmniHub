using LibreHardwareMonitor.Hardware;
using OmniHub.Core;

namespace OmniHub.Services;

public class FanChannel
{
    public string Id = "";
    public string Name = "";
    public string Hardware = "";
    public ISensor? Control;   // % (controlable)
    public ISensor? Rpm;       // RPM (lectura)
    public float? Percent => Control?.Value;
    public float? RpmValue => Rpm?.Value;
}

/// <summary>Temperaturas y ventiladores de cualquier placa/GPU mediante LibreHardwareMonitor (+PawnIO).</summary>
public sealed class FanService : IDisposable
{
    public static readonly string[] Modes = { "Auto (BIOS)", "Inteligente", "Silencioso", "Equilibrado", "Rendimiento", "Personalizado", "Máximo", "Fijo" };

    /// <summary>Turbo: todos los ventiladores al 100 % mientras esté activo.</summary>
    public bool Boost { get; set; }
    public static readonly string[] Sources = { "Máximo CPU/GPU", "CPU", "GPU" };

    readonly Computer _pc = new()
    {
        IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true,
        IsControllerEnabled = true, IsMemoryEnabled = true, IsStorageEnabled = true,
    };

    public List<FanChannel> Channels { get; } = new();
    public List<(string name, ISensor s)> Temps { get; } = new();
    public double CpuTemp { get; private set; }
    public double GpuTemp { get; private set; }
    public double CpuLoad { get; private set; }
    public double GpuLoad { get; private set; }
    public string Status { get; private set; } = "";
    public bool Opened { get; private set; }
    readonly Dictionary<string, double> _smoothed = new();

    public void Open()
    {
        try
        {
            _pc.Open();
            Opened = true;
            Discover();
            Status = Channels.Count > 0 ? $"{Channels.Count} canal(es) de ventilador" :
                Components.PawnIO.IsInstalled() ? "No se encontraron ventiladores controlables" : "Instala PawnIO para ver la placa base";
        }
        catch (Exception e) { Status = "Error abriendo sensores: " + e.Message; }
    }

    IEnumerable<IHardware> AllHw()
    {
        foreach (var h in _pc.Hardware)
        {
            yield return h;
            foreach (var s in h.SubHardware) yield return s;
        }
    }

    void Discover()
    {
        Channels.Clear(); Temps.Clear();
        UpdateAll();
        foreach (var h in AllHw())
        {
            var fans = h.Sensors.Where(s => s.SensorType == SensorType.Fan).ToList();
            foreach (var c in h.Sensors.Where(s => s.SensorType == SensorType.Control && s.Control != null))
            {
                var rpm = fans.FirstOrDefault(f => f.Index == c.Index) ?? fans.FirstOrDefault(f => f.Name == c.Name);
                Channels.Add(new FanChannel
                {
                    Id = c.Identifier.ToString(), Name = $"{c.Name}", Hardware = h.Name, Control = c, Rpm = rpm
                });
            }
            // Ventiladores solo-lectura (sin control)
            foreach (var f in fans.Where(f => !Channels.Any(ch => ch.Rpm == f)))
                Channels.Add(new FanChannel { Id = f.Identifier.ToString(), Name = f.Name, Hardware = h.Name, Rpm = f });

            foreach (var t in h.Sensors.Where(s => s.SensorType == SensorType.Temperature))
                Temps.Add(($"{h.Name} · {t.Name}", t));
        }
    }

    void UpdateAll()
    {
        foreach (var h in _pc.Hardware)
        {
            h.Update();
            foreach (var s in h.SubHardware) s.Update();
        }
    }

    /// <summary>Llamar cada ~2 s: lee sensores y aplica curvas.</summary>
    public void Tick()
    {
        if (!Opened) return;
        try { UpdateAll(); } catch { return; }

        CpuTemp = PickTemp(HardwareType.Cpu, "Package", "Tctl", "Tdie", "Core (Tctl/Tdie)", "CCD", "Core Max", "Core Average");
        // Si el procesador no da su sensor: el sensor "CPU" de la placa base y, por último, la zona térmica de Windows
        if (CpuTemp <= 0) CpuTemp = PickTemp(new[] { HardwareType.Motherboard, HardwareType.SuperIO, HardwareType.EmbeddedController }, "CPU Core", "CPU Socket", "CPU");
        if (CpuTemp <= 0 && _tick++ % 5 == 0) _acpiTemp = AcpiTemp();
        if (CpuTemp <= 0) CpuTemp = _acpiTemp;
        if (CpuTemp <= 0 && !_reported && _tick > 3) { _reported = true; WriteReport(); }
        GpuTemp = PickTemp(new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel }, "GPU Core", "GPU Hot Spot", "Core");
        CpuLoad = PickLoad(HardwareType.Cpu, "CPU Total");
        GpuLoad = PickLoad(new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel }, "GPU Core", "D3D 3D");

        var st = Settings.Current;
        foreach (var ch in Channels)
        {
            if (ch.Control?.Control is not { } ctl) continue;
            var mode = st.FanModes.TryGetValue(ch.Id, out var m) ? m : st.FanPreset;
            bool isGpu = ch.Hardware.Contains("GPU", StringComparison.OrdinalIgnoreCase) || ch.Hardware.Contains("GeForce") || ch.Hardware.Contains("Radeon") || ch.Hardware.Contains("Arc");
            try
            {
                if (Boost) { ctl.SetSoftware((float)ctl.MaxSoftwareValue); _smoothed[ch.Id] = 100; continue; }
                if (isGpu && st.GpuZeroFan && GpuTemp > 0 && GpuTemp < st.ZeroFanTemp)
                {
                    // Ventilador cero: la gráfica en reposo se queda en silencio total
                    ctl.SetSoftware((float)Math.Max(0, ctl.MinSoftwareValue)); _smoothed[ch.Id] = 0; continue;
                }
                if (mode == "Auto (BIOS)")
                {
                    if (ctl.ControlMode != ControlMode.Default) ctl.SetDefault();
                    continue;
                }
                double target;
                if (mode == "Fijo") target = st.FanFixed.TryGetValue(ch.Id, out var f) ? f : 50;
                else
                {
                    double temp = st.FanSource switch { "CPU" => CpuTemp, "GPU" => GpuTemp, _ => Math.Max(CpuTemp, GpuTemp) };
                    double load = Math.Max(CpuLoad, GpuLoad);
                    if (isGpu) { temp = GpuTemp; load = GpuLoad; }
                    if (mode == "Inteligente")
                    {
                        // Se adelanta a la carga: sube antes cuando el PC trabaja y se calla en reposo
                        target = Presets.EvalCurve(Presets.FanCurves["Equilibrado"], temp);
                        if (load > 70) target += 12; else if (load > 40) target += 5; else if (load < 15) target -= 10;
                        target = Math.Clamp(target, 20, 100);
                    }
                    else if (mode == "Personalizado") target = Presets.EvalCurve(Presets.Custom(st.CustomCurve), temp);
                    else target = Presets.FanCurves.TryGetValue(mode, out var curve) ? Presets.EvalCurve(curve, temp) : 50;
                }
                // Suavizado: sube rápido, baja lento (evita "tirones" de ruido)
                var prev = _smoothed.TryGetValue(ch.Id, out var p) ? p : target;
                var val = target > prev ? prev + (target - prev) * 0.6 : prev + (target - prev) * 0.2;
                _smoothed[ch.Id] = val;
                val = Math.Clamp(val, ctl.MinSoftwareValue, ctl.MaxSoftwareValue);
                ctl.SetSoftware((float)val);
            }
            catch { }
        }
    }

    double PickTemp(HardwareType t, params string[] names) => PickTemp(new[] { t }, names);
    int _tick;
    double _acpiTemp;
    bool _reported;

    /// <summary>Informe completo de sensores para diagnosticar (se guarda en %AppData%\OmniHub\sensores.txt).</summary>
    public string WriteReport()
    {
        var path = System.IO.Path.Combine(AppInfo.DataDir, "sensores.txt");
        try
        {
            var admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            var head = $"OmniHub {AppInfo.Version} · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · admin={admin} · PawnIO={Components.PawnIO.IsInstalled()}\n" +
                       $"CPU={CpuTemp:0.0} GPU={GpuTemp:0.0}\n\n";
            System.IO.File.WriteAllText(path, head + _pc.GetReport());
        }
        catch (Exception e) { try { System.IO.File.WriteAllText(path, "Error: " + e); } catch { } }
        return path;
    }

    /// <summary>Temperatura de la zona térmica ACPI (aproximada, pero funciona sin drivers en casi todos los PC).</summary>
    static double AcpiTemp()
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            double best = 0;
            foreach (var o in s.Get())
            {
                var c = Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15; // décimas de Kelvin
                if (c is > 5 and < 120) best = Math.Max(best, c);
            }
            return best;
        }
        catch { return 0; }
    }

    double PickTemp(HardwareType[] types, params string[] names)
    {
        var hw = AllHw().Where(h => types.Contains(h.HardwareType)).ToList();
        foreach (var n in names)
            foreach (var h in hw)
                foreach (var s in h.Sensors)
                    if (s.SensorType == SensorType.Temperature && s.Name.Contains(n, StringComparison.OrdinalIgnoreCase) && s.Value is > 0)
                        return s.Value.Value;
        var any = hw.SelectMany(h => h.Sensors).Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0).Select(s => s.Value!.Value).DefaultIfEmpty(0).Max();
        return any;
    }

    double PickLoad(HardwareType t, params string[] names) => PickLoad(new[] { t }, names);
    double PickLoad(HardwareType[] types, params string[] names)
    {
        foreach (var n in names)
            foreach (var h in AllHw().Where(h => types.Contains(h.HardwareType)))
                foreach (var s in h.Sensors)
                    if (s.SensorType == SensorType.Load && s.Name.Contains(n, StringComparison.OrdinalIgnoreCase) && s.Value != null)
                        return s.Value.Value;
        return 0;
    }

    /// <summary>Todos los sensores agrupados por componente (para el monitor de hardware).</summary>
    public List<(string hardware, string type, List<ISensor> sensors)> SensorGroups()
    {
        var res = new List<(string, string, List<ISensor>)>();
        if (!Opened) return res;
        foreach (var h in AllHw())
            res.Add((h.Name, h.HardwareType.ToString(), h.Sensors.Where(s => s.Value != null).OrderBy(s => s.SensorType).ThenBy(s => s.Index).ToList()));
        return res.Where(g => g.Item3.Count > 0).ToList();
    }

    /// <summary>Devuelve todos los ventiladores al control de la BIOS (al salir).</summary>
    public void RestoreDefaults()
    {
        foreach (var ch in Channels)
            try { ch.Control?.Control?.SetDefault(); } catch { }
    }

    public void Dispose()
    {
        RestoreDefaults();
        try { _pc.Close(); } catch { }
    }
}
