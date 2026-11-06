using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace OmniHub.Core;

public static class AppInfo
{
    public const string Name = "OmniHub";
    public const string Version = "1.0.4";
    public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OmniHub";

    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name);

    /// <summary>Carpeta de instalación registrada (o la de por defecto).</summary>
    public static string InstallDir
    {
        get
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(UninstallKey);
                if (k?.GetValue("InstallLocation") is string s && s.Length > 0) return s;
            }
            catch { }
            return DefaultInstallDir;
        }
    }

    public static bool IsRegistered
    {
        get
        {
            try { using var k = Registry.LocalMachine.OpenSubKey(UninstallKey); return k != null; }
            catch { return false; }
        }
    }

    public static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>¿Se está ejecutando desde la carpeta instalada?</summary>
    public static bool RunningInstalled =>
        string.Equals(Path.GetDirectoryName(ExePath)?.TrimEnd('\\'), InstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static string ComponentsDir => Path.Combine(InstallDir, "Components");

    public static string DataDir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
            Directory.CreateDirectory(d);
            return d;
        }
    }
}

public class LightingState
{
    public string Effect { get; set; } = "Estático";
    public string Color1 { get; set; } = "#00E5FF";
    public string Color2 { get; set; } = "#8C52FF";
    public double Speed { get; set; } = 50;
    public double Brightness { get; set; } = 100;
    public List<string> DisabledDevices { get; set; } = new();
}

public class Profile
{
    public string Name { get; set; } = "Perfil";
    public LightingState Lighting { get; set; } = new();
    public string FanPreset { get; set; } = "Equilibrado";
    public double[] Eq { get; set; } = new double[10];
    public string EqPreset { get; set; } = "Plano";
}

public class Settings
{
    // General
    public bool SkipLauncher { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool CloseVendorApps { get; set; }
    public string? OpenRgbPath { get; set; }
    public bool MouseRestored { get; set; }      // el ratón se devolvió a su memoria interna (una vez)

    // Estado actual
    public LightingState Lighting { get; set; } = new();
    public string FanPreset { get; set; } = "Auto (BIOS)";
    public string Scenario { get; set; } = "";
    public string FanSource { get; set; } = "Máximo CPU/GPU";
    public Dictionary<string, string> FanModes { get; set; } = new();   // sensor id -> modo
    public Dictionary<string, double> FanFixed { get; set; } = new();   // sensor id -> %
    public double[] CustomCurve { get; set; } = { 25, 35, 55, 80, 100 }; // % a 30/45/60/75/90 °C
    public bool GpuZeroFan { get; set; }
    public double ZeroFanTemp { get; set; } = 50;


    // Audio
    public double[] Eq { get; set; } = new double[10];
    public string EqPreset { get; set; } = "Plano";
    public string? AutoEqFile { get; set; }
    public string? AutoEqName { get; set; }
    public bool AutoEqTried { get; set; }
    public Dictionary<string, double[]> EqUserPresets { get; set; } = new();
    // Efectos / audio espacial (Equalizer APO)
    public string SpatialMode { get; set; } = "Desactivado";
    public double OutputGain { get; set; }      // dB
    public double Bass { get; set; }            // dB (estantería 105 Hz)
    public double Treble { get; set; }          // dB (estantería 8 kHz)
    public double Width { get; set; } = 100;    // % anchura estéreo
    public double Crossfeed { get; set; }       // % escenario/cruce
    public double Balance { get; set; }         // -100 izq … +100 der
    public string EqExtra { get; set; } = "";   // filtros avanzados con sintaxis de Equalizer APO

    // Integraciones
    public bool MediaLights { get; set; }

    // Perfiles
    public List<Profile> Profiles { get; set; } = new();

    static string FilePath => Path.Combine(AppInfo.DataDir, "settings.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Settings Current { get; private set; } = Load();

    static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                // Reparación de ajustes guardados por la 1.0.1/1.0.2 con los acentos rotos ("EstÃ¡tico")
                if (text.Contains('Ã'))
                {
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    text = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.GetEncoding(1252).GetBytes(text));
                }
                var s = JsonSerializer.Deserialize<Settings>(text, Opts);
                if (s != null) { s.EnsureDefaults(); return s; }
            }
        }
        catch { }
        var n = new Settings();
        n.EnsureDefaults();
        return n;
    }

    void EnsureDefaults()
    {
        if (Eq.Length != 10) Eq = new double[10];
        if (CustomCurve.Length != 5) CustomCurve = new double[] { 25, 35, 55, 80, 100 };
        if (Profiles.Count == 0)
        {
            Profiles.Add(new Profile { Name = "Gaming", FanPreset = "Rendimiento", EqPreset = "FPS (pasos)", Eq = Presets.Eq["FPS (pasos)"],
                Lighting = new LightingState { Effect = "Arcoíris", Speed = 60 } });
            Profiles.Add(new Profile { Name = "Silencio", FanPreset = "Silencioso", EqPreset = "Plano",
                Lighting = new LightingState { Effect = "Respirar", Color1 = "#3050FF", Speed = 25, Brightness = 50 } });
            Profiles.Add(new Profile { Name = "Música", FanPreset = "Equilibrado", EqPreset = "Música", Eq = Presets.Eq["Música"],
                Lighting = new LightingState { Effect = "Ciclo de color", Speed = 30 } });
            Profiles.Add(new Profile { Name = "Noche", FanPreset = "Silencioso", EqPreset = "Plano",
                Lighting = new LightingState { Effect = "Apagado" } });
        }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts)); } catch { }
    }
}

public static class Presets
{
    public static readonly int[] EqFreqs = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public static readonly Dictionary<string, double[]> Eq = new()
    {
        ["Plano"]        = new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        ["FPS (pasos)"]  = new double[] { -4, -3, -2, -1, 0, 2, 4, 5, 3, 1 },
        ["Graves+"]      = new double[] { 6, 5, 4, 2, 0, 0, 0, 0, 0, 0 },
        ["Música"]       = new double[] { 3, 2, 1, 0, -1, 0, 1, 2, 3, 3 },
        ["Cine"]         = new double[] { 4, 3, 2, 0, 0, 1, 2, 2, 2, 1 },
        ["Voz / Podcast"]= new double[] { -3, -2, -1, 1, 3, 4, 4, 3, 1, 0 },
        ["Agudos+"]      = new double[] { 0, 0, 0, 0, 0, 1, 3, 4, 5, 4 },
        ["V clásica"]    = new double[] { 5, 4, 2, 0, -2, -2, 0, 2, 4, 5 },
    };

    // Curvas de ventilador: (temperatura °C, % velocidad)
    public static readonly Dictionary<string, (double t, double p)[]> FanCurves = new()
    {
        ["Silencioso"]  = new[] { (30.0, 20.0), (50, 30), (65, 45), (75, 65), (85, 100) },
        ["Equilibrado"] = new[] { (30.0, 30.0), (50, 40), (65, 60), (75, 80), (85, 100) },
        ["Rendimiento"] = new[] { (30.0, 45.0), (50, 60), (65, 80), (75, 100) },
        ["Máximo"]      = new[] { (0.0, 100.0) },
    };

    public static readonly double[] CustomTemps = { 30, 45, 60, 75, 90 };

    public static (double t, double p)[] Custom(double[] pct) =>
        CustomTemps.Select((t, i) => (t, i < pct.Length ? pct[i] : 100)).ToArray();

    public static double EvalCurve((double t, double p)[] c, double temp)
    {
        if (temp <= c[0].t) return c[0].p;
        for (int i = 1; i < c.Length; i++)
            if (temp <= c[i].t)
            {
                var k = (temp - c[i - 1].t) / (c[i].t - c[i - 1].t);
                return c[i - 1].p + k * (c[i].p - c[i - 1].p);
            }
        return c[^1].p;
    }
}
