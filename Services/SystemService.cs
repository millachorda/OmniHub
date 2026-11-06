using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using OmniHub.Core;

namespace OmniHub.Services;

/// <summary>Escenarios de rendimiento (planes de energía de Windows + ventiladores) y Modo juego.</summary>
public static class SystemService
{
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";
    public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public record Plan(string Guid, string Name, bool Active);

    /// <summary>(nombre, icono, descripción, plan de energía, modo de ventiladores)</summary>
    public static readonly (string name, string glyph, string hint, string plan, string fans)[] Scenarios =
    {
        ("Máximo rendimiento", "", "CPU sin límites y ventiladores en Rendimiento. Para jugar.", "ultimate", "Rendimiento"),
        ("Equilibrado", "", "Lo normal de Windows con refrigeración inteligente.", Balanced, "Inteligente"),
        ("Silencioso", "", "Menos consumo y ventiladores al mínimo. Para trabajar o ver vídeo.", PowerSaver, "Silencioso"),
    };

    public static List<Plan> Plans()
    {
        var list = new List<Plan>();
        var (_, output) = Run("powercfg", "/list");
        foreach (Match m in Regex.Matches(output, @"([0-9a-fA-F-]{36})\s+\((.+?)\)(\s*\*)?"))
            list.Add(new Plan(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.Trim(), m.Groups[3].Success && m.Groups[3].Value.Contains('*')));
        return list;
    }

    public static Plan? ActivePlan => Plans().FirstOrDefault(p => p.Active);

    public static bool SetPlan(string guid)
    {
        if (guid == "ultimate") guid = EnsureUltimate() ?? HighPerf;
        if (!Plans().Any(p => p.Guid == guid) && guid == HighPerf)
        {
            // En algunos PC "Alto rendimiento" está oculto: lo recreamos
            Run("powercfg", $"-duplicatescheme {HighPerf} {HighPerf}");
        }
        return Run("powercfg", $"/setactive {guid}").code == 0;
    }

    /// <summary>"Máximo rendimiento" existe en Windows pero oculto: lo añadimos una vez.</summary>
    static string? EnsureUltimate()
    {
        var plans = Plans();
        var existing = plans.FirstOrDefault(p => p.Guid == Ultimate || p.Name.Contains("Máximo rendimiento") || p.Name.Contains("Ultimate"));
        if (existing != null) return existing.Guid;
        var (code, output) = Run("powercfg", $"-duplicatescheme {Ultimate}");
        var m = Regex.Match(output, @"[0-9a-fA-F-]{36}");
        return code == 0 && m.Success ? m.Value.ToLowerInvariant() : null;
    }

    public static void ApplyScenario(string name)
    {
        var s = Scenarios.FirstOrDefault(x => x.name == name);
        if (s.name == null) return;
        SetPlan(s.plan);
        Settings.Current.FanPreset = s.fans;
        Settings.Current.FanModes.Clear();
        Settings.Current.Scenario = name;
        Settings.Current.Save();
    }

    // ---------- Modo juego de Windows ----------
    public static bool GameMode
    {
        get
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar"); return (k?.GetValue("AutoGameModeEnabled") as int? ?? 1) != 0; }
            catch { return true; }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
                k.SetValue("AutoGameModeEnabled", value ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("AllowAutoGameMode", value ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }
    }

    static (int code, string output) Run(string exe, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(exe, args)
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.GetEncoding(850) });
            if (p == null) return (-1, "");
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, o);
        }
        catch { return (-1, ""); }
    }
}
