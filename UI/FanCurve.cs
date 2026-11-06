using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using OmniHub.Core;
using OmniHub.Services;

namespace OmniHub.UI;

/// <summary>
/// Curva personalizada: dos curvas (CPU y GPU) de 3 intervalos, editables con números
/// o arrastrando los puntos, con la temperatura actual y la elección de ventiladores.
/// Los cambios se aplican al pulsar «Guardar».
/// </summary>
public partial class MainWindow
{
    static double[][]? _draftCpu, _draftGpu;

    static double[][] Copy(double[][] c) => c.Select(p => (double[])p.Clone()).ToArray();

    UIElement CurveEditor()
    {
        _draftCpu ??= Copy(St.CpuCurve);
        _draftGpu ??= Copy(St.GpuCurve);
        var cpuBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x3E, 0x5A));
        var gpuBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x5C, 0xF0));
        var nowBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xC2, 0x6A));

        // ---------------- Gráfica ----------------
        const double L = 44, T = 14, PW = 560, PH = 300, W = L + PW + 16, H = T + PH + 40;
        double X(double t) => L + Math.Clamp(t, 0, 100) / 100 * PW;
        double Y(double p) => T + (1 - Math.Clamp(p, 0, 100) / 100) * PH;
        var canvas = new Canvas { Width = W, Height = H, Background = Brushes.Transparent, ClipToBounds = false };
        for (int i = 0; i <= 10; i++)
        {
            canvas.Children.Add(new Line { X1 = X(i * 10), X2 = X(i * 10), Y1 = T, Y2 = T + PH, Stroke = B("Line"), StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = L, X2 = L + PW, Y1 = Y(i * 10), Y2 = Y(i * 10), Stroke = B("Line"), StrokeThickness = 1 });
            var yl = new TextBlock { Text = $"{i * 10}", FontSize = 11, Foreground = B("Muted"), Width = 30, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(yl, L - 36); Canvas.SetTop(yl, Y(i * 10) - 8); canvas.Children.Add(yl);
            if (i > 0)
            {
                var xl = new TextBlock { Text = $"{i * 10}", FontSize = 11, Foreground = B("Muted") };
                Canvas.SetLeft(xl, X(i * 10) - 8); Canvas.SetTop(xl, T + PH + 10); canvas.Children.Add(xl);
            }
        }
        var unitsY = new TextBlock { Text = "(%)", FontSize = 11, Foreground = B("Muted") };
        Canvas.SetLeft(unitsY, 4); Canvas.SetTop(unitsY, -6); canvas.Children.Add(unitsY);
        var unitsX = new TextBlock { Text = "(°C)", FontSize = 11, Foreground = B("Muted") };
        Canvas.SetLeft(unitsX, L + PW + 2); Canvas.SetTop(unitsX, T + PH + 10); canvas.Children.Add(unitsX);
        // barra de temperatura (frío → caliente)
        var bar = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        foreach (var (c, o) in new[] { ("#1E6BFF", 0.0), ("#2EE5E5", 0.3), ("#7CE04A", 0.55), ("#FFC23D", 0.75), ("#FF3B3B", 1.0) })
            bar.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c), o));
        var barRect = new Rectangle { Width = PW, Height = 4, Fill = bar };
        Canvas.SetLeft(barRect, L); Canvas.SetTop(barRect, T + PH); canvas.Children.Add(barRect);

        var cpuFill = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(40, 0xF0, 0x3E, 0x5A)) };
        var gpuFill = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(40, 0x4C, 0x5C, 0xF0)) };
        var cpuLine = new Polyline { Stroke = cpuBrush, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        var gpuLine = new Polyline { Stroke = gpuBrush, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        canvas.Children.Add(cpuFill); canvas.Children.Add(gpuFill); canvas.Children.Add(cpuLine); canvas.Children.Add(gpuLine);

        var nowCpu = new Line { Y1 = T, Y2 = T + PH, Stroke = nowBrush, StrokeThickness = 2 };
        var nowGpu = new Line { Y1 = T, Y2 = T + PH, Stroke = nowBrush, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } };
        canvas.Children.Add(nowCpu); canvas.Children.Add(nowGpu);

        var boxes = new Dictionary<(bool cpu, int i, int k), TextBox>();
        var dots = new List<(Ellipse e, bool cpu, int i)>();

        void Redraw()
        {
            void Curve(double[][] c, Polyline line, Polygon fill)
            {
                var s = c.OrderBy(p => p[0]).ToArray();
                var pts = new PointCollection { new(X(0), Y(s[0][1])) };
                foreach (var p in s) pts.Add(new Point(X(p[0]), Y(p[1])));
                pts.Add(new Point(X(100), Y(s[^1][1])));
                line.Points = pts;
                fill.Points = new PointCollection(pts) { new(X(100), T + PH), new(X(0), T + PH) };
            }
            Curve(_draftCpu!, cpuLine, cpuFill);
            Curve(_draftGpu!, gpuLine, gpuFill);
            foreach (var (e, cpu, i) in dots)
            {
                var p = (cpu ? _draftCpu! : _draftGpu!)[i];
                Canvas.SetLeft(e, X(p[0]) - 9); Canvas.SetTop(e, Y(p[1]) - 9);
            }
        }

        // Puntos arrastrables (encima de todo)
        foreach (var cpu in new[] { false, true })
            for (int i = 0; i < (cpu ? _draftCpu! : _draftGpu!).Length; i++)
            {
                var e = new Ellipse { Width = 18, Height = 18, Fill = Brushes.White, Stroke = cpu ? cpuBrush : gpuBrush, StrokeThickness = 3, Cursor = Cursors.SizeAll, ToolTip = $"{(cpu ? "CPU" : "GPU")} · intervalo {i + 1}" };
                int idx = i; bool isCpu = cpu;
                e.MouseLeftButtonDown += (_, ev) => { e.CaptureMouse(); ev.Handled = true; };
                e.MouseLeftButtonUp += (_, _) => { e.ReleaseMouseCapture(); CommitCurve(); };
                e.MouseMove += (_, ev) =>
                {
                    if (!e.IsMouseCaptured) return;
                    var pos = ev.GetPosition(canvas);
                    var c = isCpu ? _draftCpu! : _draftGpu!;
                    double t = Math.Round((pos.X - L) / PW * 100), p = Math.Round((1 - (pos.Y - T) / PH) * 100);
                    // Como en MSI Center: un punto no puede pasar al anterior ni al siguiente,
                    // ni en temperatura ni en velocidad (la curva nunca baja al subir la temperatura)
                    int last = c.Length - 1;
                    double lo = idx > 0 ? c[idx - 1][0] + 1 : 0, hi = idx < last ? c[idx + 1][0] - 1 : 100;
                    double plo = idx > 0 ? c[idx - 1][1] : 0, phi = idx < last ? c[idx + 1][1] : 100;
                    c[idx][0] = Math.Clamp(t, lo, hi);
                    c[idx][1] = Math.Clamp(p, plo, phi);
                    boxes[(isCpu, idx, 0)].Text = $"{c[idx][0]:0}";
                    boxes[(isCpu, idx, 1)].Text = $"{c[idx][1]:0}";
                    Redraw();
                };
                dots.Add((e, cpu, i));
                canvas.Children.Add(e);
            }

        _live.Add(() =>
        {
            nowCpu.X1 = nowCpu.X2 = X(Hub.Fans.CpuTemp);
            nowGpu.X1 = nowGpu.X2 = X(Hub.Fans.GpuTemp);
        });

        // Leyenda
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(44, 0, 0, 0) };
        foreach (var (txt, br, dash) in new[] { ("GPU", (Brush)gpuBrush, false), ("CPU", cpuBrush, false), ("Actual CPU", nowBrush, false), ("Actual GPU", nowBrush, true) })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 22, 4) };
            row.Children.Add(dash ? new Rectangle { Width = 12, Height = 2, Fill = br, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }
                                  : new Ellipse { Width = 8, Height = 8, Fill = br, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) });
            row.Children.Add(Sub(txt, 12));
            legend.Children.Add(row);
        }
        // La gráfica ocupa todo el ancho disponible (se escala), con la leyenda a la derecha
        var chartRow = new StackPanel { Margin = new Thickness(0, 10, 0, 6) };
        chartRow.Children.Add(legend);
        chartRow.Children.Add(new Viewbox { Child = canvas, MaxHeight = 420, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });

        // ---------------- Panel izquierdo ----------------
        var rpm = new TextBlock { FontSize = 46, FontWeight = FontWeights.Light, Foreground = cpuBrush };
        _live.Add(() =>
        {
            var sel = Hub.Fans.Channels.Where(c => c.RpmValue > 0 && (St.CustomFans.Count == 0 || St.CustomFans.Contains(c.Id))).Select(c => c.RpmValue!.Value).ToList();
            rpm.Text = sel.Count > 0 ? $"{sel.Average():0}" : "—";
        });
        var rpmRow = new StackPanel { Orientation = Orientation.Horizontal };
        rpmRow.Children.Add(rpm);
        rpmRow.Children.Add(new TextBlock { Text = "RPM", Foreground = B("Muted"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 24, 10) });
        rpmRow.Children.Add(Stack(Sub("Rango de velocidad: 0 % – 100 %", 12), Sub("Rango de temperatura: 0 °C – 100 °C", 12)));

        var fans = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
        var controllable = Hub.Fans.Channels.Where(c => c.Control != null).ToList();
        foreach (var ch in controllable)
        {
            var id = ch.Id;
            var cb = Toggle(ch.Name, St.CustomFans.Count == 0 || St.CustomFans.Contains(id), on =>
            {
                if (St.CustomFans.Count == 0) St.CustomFans = controllable.Select(c => c.Id).ToList();
                if (on) { if (!St.CustomFans.Contains(id)) St.CustomFans.Add(id); } else St.CustomFans.Remove(id);
                if (St.CustomFans.Count == controllable.Count) St.CustomFans.Clear();
                SaveSoon();
            });
            cb.Margin = new Thickness(0, 0, 18, 6);
            cb.Width = 230; // columnas iguales; el nombre completo cabe y si no, se ve al pasar el ratón
            cb.ToolTip = $"{ch.Name} · {ch.Hardware}";
            fans.Children.Add(cb);
        }
        if (controllable.Count == 0) fans.Children.Add(Sub("No hay ventiladores controlables (instala el módulo de sensores).", 12));

        var table = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        // Intervalo | CPU [°C] °C [%] % | separación | GPU [°C] °C [%] %
        foreach (var w in new[] { 96.0, 40, 64, 28, 64, 26, 30, 40, 64, 28, 64, 26, 40 }) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        int rows = Math.Max(_draftCpu!.Length, _draftGpu!.Length);
        for (int i = 0; i < rows; i++)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            void Cell(UIElement el, int col) { Grid.SetRow(el, i); Grid.SetColumn(el, col); table.Children.Add(el); }
            var lab = Txt($"Intervalo {i + 1}"); lab.VerticalAlignment = VerticalAlignment.Center; Cell(lab, 0);
            // ✕ para quitar este intervalo (mínimo 2)
            if (rows > MinIntervals)
            {
                int row = i;
                var del = new Button { Content = "✕", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Quitar este intervalo", VerticalAlignment = VerticalAlignment.Center };
                del.Click += (_, _) => { RemoveInterval(row); Refresh(); };
                Cell(del, 12);
            }
            int col = 1;
            foreach (var cpu in new[] { true, false })
            {
                if (i >= (cpu ? _draftCpu! : _draftGpu!).Length) { col += 6; continue; }
                var name = Sub(cpu ? "CPU" : "GPU"); name.VerticalAlignment = VerticalAlignment.Center; Cell(name, col++);
                for (int k = 0; k < 2; k++)
                {
                    var c = cpu ? _draftCpu! : _draftGpu!;
                    var box = new TextBox
                    {
                        Text = $"{c[i][k]:0}", Width = 56, Height = 34, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(4, 0, 4, 0),
                        FontSize = 14, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    };
                    int ii = i, kk = k; bool isCpu = cpu;
                    box.TextChanged += (_, _) =>
                    {
                        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v)) return;
                        var cc = isCpu ? _draftCpu! : _draftGpu!;
                        cc[ii][kk] = Math.Clamp(v, 0, 100);
                        Redraw();
                        CommitCurve();
                    };
                    // Al salir de la casilla se corrige igual que al arrastrar: entre el punto anterior y el siguiente
                    box.LostFocus += (_, _) =>
                    {
                        var cc = isCpu ? _draftCpu! : _draftGpu!;
                        double lo = ii > 0 ? cc[ii - 1][kk] + (kk == 0 ? 1 : 0) : 0;
                        double hi = ii < cc.Length - 1 ? cc[ii + 1][kk] - (kk == 0 ? 1 : 0) : 100;
                        cc[ii][kk] = Math.Clamp(cc[ii][kk], Math.Min(lo, hi), Math.Max(lo, hi));
                        box.Text = $"{cc[ii][kk]:0}";
                        Redraw();
                        CommitCurve();
                    };
                    boxes[(cpu, i, k)] = box;
                    Cell(box, col++);
                    var unit = Sub(k == 0 ? "°C" : "%"); unit.VerticalAlignment = VerticalAlignment.Center; Cell(unit, col++);
                }
                col++; // separación
            }
        }

        var addBtn = Btn("+ Añadir intervalo", () => { AddInterval(); CommitCurve(); Refresh(); });
        addBtn.IsEnabled = rows < MaxIntervals;
        var remBtn = Btn("− Quitar intervalo", () => { RemoveInterval(rows - 1); CommitCurve(); Refresh(); });
        remBtn.IsEnabled = rows > MinIntervals;
        var def = Btn("Predeterminado", () =>
        {
            _draftCpu = Presets.DefaultCpuCurve(); _draftGpu = Presets.DefaultGpuCurve();
            CommitCurve(); Refresh();
        });
        var intervalBtns = Row(addBtn, remBtn, def, Sub($"  Entre {MinIntervals} y {MaxIntervals} intervalos · se guarda solo", 12));
        intervalBtns.Margin = new Thickness(0, 6, 0, 0);

        // De arriba abajo: RPM y ventiladores · gráfica a todo lo ancho · tabla de intervalos
        var tableScroll = new ScrollViewer { Content = table, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };

        Redraw();
        var title = H2("Curva personalizada"); title.Foreground = B("Accent"); title.FontSize = 18;
        return Card(title,
            new TextBlock { Text = "Ventiladores", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = B("Text") },
            rpmRow, fans, chartRow, tableScroll, intervalBtns,
            Sub("Arrastra los puntos o escribe los valores: cada cambio se guarda y se aplica al momento. Por debajo del primer intervalo se mantiene su velocidad y por encima del último, la última. " +
                "Los ventiladores del sistema siguen la curva que pida más aire (CPU o GPU); los de la gráfica, solo la de la GPU.", 12));
    }

    DispatcherTimer? _curveTimer;

    /// <summary>Guarda y aplica la curva (ordenada y sin puntos que se crucen) poco después del último cambio.</summary>
    void CommitCurve()
    {
        _curveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _curveTimer.Stop();
        _curveTimer.Tick -= CurveTick; _curveTimer.Tick += CurveTick;
        _curveTimer.Start();
    }

    void CurveTick(object? s, EventArgs e)
    {
        _curveTimer!.Stop();
        static double[][] Normalize(double[][] src)
        {
            var c = Copy(src);
            Array.Sort(c, (a, b) => a[0].CompareTo(b[0]));
            for (int i = 1; i < c.Length; i++)
            {
                if (c[i][0] <= c[i - 1][0]) c[i][0] = Math.Min(100, c[i - 1][0] + 1);
                if (c[i][1] < c[i - 1][1]) c[i][1] = c[i - 1][1]; // la velocidad nunca baja al subir la temperatura
            }
            return c;
        }
        St.CpuCurve = Normalize(_draftCpu!);
        St.GpuCurve = Normalize(_draftGpu!);
        St.FanPreset = "Personalizado";
        St.Save();
    }

    const int MinIntervals = 2, MaxIntervals = 8;

    /// <summary>Añade un intervalo al final de las dos curvas, a medio camino entre el último y 100 °C.</summary>
    static void AddInterval()
    {
        static double[][] Add(double[][] c)
        {
            if (c.Length >= MaxIntervals) return c;
            var last = c.OrderBy(p => p[0]).Last();
            double t = Math.Min(100, Math.Round(last[0] + Math.Max(2, (100 - last[0]) / 2)));
            if (t <= last[0]) // no cabe al final: lo metemos entre los dos puntos más separados
            {
                var s = c.OrderBy(p => p[0]).ToArray();
                int gap = Enumerable.Range(0, s.Length - 1).OrderByDescending(i => s[i + 1][0] - s[i][0]).First();
                var mid = new[] { Math.Round((s[gap][0] + s[gap + 1][0]) / 2), Math.Round((s[gap][1] + s[gap + 1][1]) / 2) };
                return s.Take(gap + 1).Append(mid).Concat(s.Skip(gap + 1)).ToArray();
            }
            return c.Append(new[] { t, Math.Min(100, last[1] + 10) }).ToArray();
        }
        _draftCpu = Add(_draftCpu!);
        _draftGpu = Add(_draftGpu!);
    }

    /// <summary>Quita el intervalo n de las dos curvas (siempre quedan al menos 2).</summary>
    static void RemoveInterval(int n)
    {
        static double[][] Rem(double[][] c, int i) => c.Length > MinIntervals && i < c.Length ? c.Where((_, k) => k != i).ToArray() : c;
        _draftCpu = Rem(_draftCpu!, n);
        _draftGpu = Rem(_draftGpu!, n);
    }

    static bool Same(double[][] a, double[][] b) =>
        a.Length == b.Length && a.Zip(b).All(p => Math.Abs(p.First[0] - p.Second[0]) < 0.01 && Math.Abs(p.First[1] - p.Second[1]) < 0.01);
}
