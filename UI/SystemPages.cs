using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LibreHardwareMonitor.Hardware;
using OmniHub.Services;

namespace OmniHub.UI;

/// <summary>Rendimiento (escenarios de usuario) y Monitor de hardware.</summary>
public partial class MainWindow
{
    UIElement PagePerformance()
    {
        var root = Stack(Header("Rendimiento", "Escenarios para todo el PC: plan de energía de Windows y refrigeración en un clic"));
        var active = SystemService.ActivePlan;

        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-6, 0, -6, 12) };
        foreach (var sc in SystemService.Scenarios)
        {
            bool sel = St.Scenario == sc.name;
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new TextBlock { Text = sc.glyph, FontFamily = Icons, FontSize = 34, Foreground = sel ? B("Accent") : B("Muted"), HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = sc.name, FontSize = 17, Margin = new Thickness(0, 10, 0, 4), Foreground = sel ? B("Text") : B("Muted"), FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = sc.hint, FontSize = 12, Foreground = B("Muted"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(14, 0, 14, 0) });
            var tile = new Border
            {
                Height = 170, CornerRadius = new CornerRadius(14), Margin = new Thickness(6, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand,
                Background = sel ? B("CardHi") : B("Bg"), BorderThickness = new Thickness(sel ? 2 : 1), BorderBrush = sel ? B("AccentGrad") : B("Line"), Child = content
            };
            var name = sc.name;
            tile.MouseEnter += (_, _) => { if (St.Scenario != name) tile.BorderBrush = B("Accent"); };
            tile.MouseLeave += (_, _) => { if (St.Scenario != name) tile.BorderBrush = B("Line"); };
            tile.MouseLeftButtonUp += async (_, _) =>
            {
                Toast($"Aplicando «{name}»…");
                await Task.Run(() => SystemService.ApplyScenario(name));
                Toast($"Escenario: {name}");
                Refresh();
            };
            tiles.Children.Add(tile);
        }
        var title = H2("Escenario"); title.Foreground = B("Accent"); title.FontSize = 18;
        root.Children.Add(Card(title, tiles,
            Sub($"Plan de energía activo: {active?.Name ?? "—"} · Ventiladores: {St.FanPreset}", 12)));

        // Planes de energía disponibles (para quien quiera elegir a mano)
        var plans = SystemService.Plans();
        root.Children.Add(Card(H2("Plan de energía de Windows"),
            Combo(plans.Select(p => p.Name), active?.Name, n =>
            {
                var p = plans.FirstOrDefault(x => x.Name == n);
                if (p != null && SystemService.SetPlan(p.Guid)) { St.Scenario = ""; St.Save(); Toast("Plan: " + n); }
            }, 320),
            Sub("Elegir un plan a mano desmarca el escenario.", 12)));

        root.Children.Add(Card(H2("Juegos"),
            Toggle("Modo juego de Windows (prioriza el juego y evita actualizaciones mientras juegas)", SystemService.GameMode, v => { SystemService.GameMode = v; Toast(v ? "Modo juego activado" : "Modo juego desactivado"); }),
            Toggle("Cerrar las apps del fabricante al abrir OmniHub (evita conflictos)", St.CloseVendorApps, v => { St.CloseVendorApps = v; SaveSoon(); })));
        return root;
    }

    UIElement PageMonitor()
    {
        var root = Stack(Header("Monitor", "Todos los sensores de tu PC en tiempo real"));
        root.Children.Add(Cols(3,
            Stat("CPU", () => $"{Hub.Fans.CpuTemp:0} °C · {Hub.Fans.CpuLoad:0} %", "Accent"),
            Stat("GPU", () => $"{Hub.Fans.GpuTemp:0} °C · {Hub.Fans.GpuLoad:0} %", "Accent2"),
            Stat("Ventiladores", () => { var r = Hub.Fans.Channels.Where(c => c.RpmValue > 0).Select(c => c.RpmValue!.Value).ToList(); return r.Count > 0 ? $"{r.Average():0} RPM" : "—"; })));

        var groups = Hub.Fans.SensorGroups();
        if (groups.Count == 0) root.Children.Add(Card(Sub(Hub.Fans.Status)));
        foreach (var (hw, type, sensors) in groups)
        {
            var list = new StackPanel();
            foreach (var byType in sensors.GroupBy(s => s.SensorType))
            {
                list.Children.Add(new TextBlock { Text = TypeName(byType.Key), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B("Accent"), Margin = new Thickness(0, 8, 0, 2) });
                var grid = new UniformGrid { Columns = 3 };
                foreach (var s in byType)
                {
                    var sensor = s;
                    var g = new DockPanel { Margin = new Thickness(0, 2, 18, 2) };
                    var v = Txt(""); v.FontWeight = FontWeights.SemiBold;
                    DockPanel.SetDock(v, Dock.Right);
                    g.Children.Add(v);
                    var n = Sub(s.Name, 12); n.TextTrimming = TextTrimming.CharacterEllipsis; n.TextWrapping = TextWrapping.NoWrap;
                    g.Children.Add(n);
                    _live.Add(() => v.Text = Format(sensor));
                    grid.Children.Add(g);
                }
                list.Children.Add(grid);
            }
            root.Children.Add(Card(H2(hw), list));
        }
        if (!Core.Components.PawnIO.IsInstalled())
            root.Children.Add(Sub("Con el módulo de sensores aparecen también la placa base, la CPU y sus voltajes.", 12));
        return root;
    }

    static string TypeName(SensorType t) => t switch
    {
        SensorType.Temperature => "TEMPERATURAS", SensorType.Load => "USO", SensorType.Clock => "FRECUENCIAS",
        SensorType.Fan => "VENTILADORES", SensorType.Control => "CONTROL DE VENTILADORES", SensorType.Voltage => "VOLTAJES",
        SensorType.Power => "CONSUMO", SensorType.Data => "DATOS", SensorType.SmallData => "MEMORIA",
        SensorType.Throughput => "TRANSFERENCIA", SensorType.Current => "CORRIENTE", SensorType.Frequency => "FRECUENCIA",
        _ => t.ToString().ToUpperInvariant()
    };

    static string Format(ISensor s)
    {
        var v = s.Value ?? 0;
        return s.SensorType switch
        {
            SensorType.Temperature => $"{v:0} °C",
            SensorType.Load or SensorType.Control or SensorType.Level => $"{v:0} %",
            SensorType.Clock => v >= 1000 ? $"{v / 1000:0.00} GHz" : $"{v:0} MHz",
            SensorType.Fan => $"{v:0} RPM",
            SensorType.Voltage => $"{v:0.000} V",
            SensorType.Power => $"{v:0.0} W",
            SensorType.Current => $"{v:0.00} A",
            SensorType.Data => $"{v:0.0} GB",
            SensorType.SmallData => $"{v:0} MB",
            SensorType.Throughput => v > 1048576 ? $"{v / 1048576:0.0} MB/s" : $"{v / 1024:0} KB/s",
            SensorType.Frequency => $"{v:0} Hz",
            _ => $"{v:0.##}"
        };
    }
}
