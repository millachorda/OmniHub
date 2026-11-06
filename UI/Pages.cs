using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using OmniHub.Core;
using OmniHub.Services;
using Line = System.Windows.Shapes.Line;
using Polygon = System.Windows.Shapes.Polygon;
using Polyline = System.Windows.Shapes.Polyline;

namespace OmniHub.UI;

public partial class MainWindow
{
    Settings St => Settings.Current;
    DispatcherTimer? _saveTimer, _eqTimer;

    void SaveSoon()
    {
        _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Stop();
        _saveTimer.Tick -= SaveTick; _saveTimer.Tick += SaveTick;
        _saveTimer.Start();
    }
    void SaveTick(object? s, EventArgs e) { _saveTimer!.Stop(); St.Save(); }

    void ApplyEqSoon()
    {
        _eqTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _eqTimer.Stop();
        _eqTimer.Tick -= EqTick; _eqTimer.Tick += EqTick;
        _eqTimer.Start();
    }
    void EqTick(object? s, EventArgs e) { _eqTimer!.Stop(); var r = Hub.Audio.ApplyEq(St.Eq, St.AutoEqFile); St.Save(); if (!r.StartsWith("Ecualizador")) Toast(r); }

    void OpenComponents() { new ComponentsWindow { Owner = this }.ShowDialog(); Refresh(); }

    // =====================================================================
    UIElement PageDashboard()
    {
        var quick = Row();
        foreach (var p in St.Profiles)
        {
            var pp = p;
            quick.Children.Add(Btn(p.Name, () => Hub.ApplyProfile(pp), p.Name == Hub.ActiveProfile));
        }
        var devs = new StackPanel();
        void Line(string k, Func<string> v)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var val = Txt("");
            Grid.SetColumn(val, 1);
            g.Children.Add(Sub(k)); g.Children.Add(val);
            _live.Add(() => val.Text = v());
            devs.Children.Add(g);
        }
        Line("Iluminación", () => Hub.Rgb.Status);
        Line("Ventiladores", () =>
        {
            var rpm = Hub.Fans.Channels.Where(c => c.RpmValue > 0).Select(c => c.RpmValue!.Value).ToList();
            return Hub.Fans.Status + (rpm.Count > 0 ? $" · media {rpm.Average():0} RPM" : "");
        });
        Line("Ratón", () => Hub.Mouse.Active is { } m ? $"{m.Name}" : "Ratón genérico (botones y sensibilidad)");
        Line("Audio", () => Hub.Audio.DefaultOutput?.FriendlyName ?? "—");
        Line("OBS", () => Hub.Obs.Connected ? (Hub.Obs.Recording ? "● Grabando" : Hub.Obs.Streaming ? "● En directo" : "Conectado") : St.ObsEnabled ? Hub.Obs.Status : "Desactivado");
        Line("Discord", () => St.DiscordPresence ? Hub.Discord.Status : "Desactivado");

        return Stack(
            Header("Panel", "Todo tu PC de un vistazo"),
            Cols(4,
                Stat("Temperatura CPU", () => $"{Hub.Fans.CpuTemp:0} °C", "Accent"),
                Stat("Temperatura GPU", () => $"{Hub.Fans.GpuTemp:0} °C", "Accent2"),
                Stat("Uso CPU", () => $"{Hub.Fans.CpuLoad:0} %"),
                Stat("Uso GPU", () => $"{Hub.Fans.GpuLoad:0} %")),
            Cols(2,
                MediaCard(),
                Card(H2("Perfiles rápidos"), quick, Sub("Cambian luces, ventiladores, DPI y ecualizador de golpe. Edítalos en «Perfiles».", 12))),
            Card(H2("Estado"), devs));
    }

    Border MediaCard()
    {
        var img = new Image { Width = 92, Height = 92, Margin = new Thickness(0, 0, 16, 0), Stretch = Stretch.UniformToFill };
        var imgBox = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = B("CardHi"), Child = img, Width = 92, Height = 92, Margin = new Thickness(0, 0, 16, 0) };
        img.Margin = new Thickness(0);
        var title = Txt("", 16); title.FontWeight = FontWeights.SemiBold; title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap;
        var artist = Sub(""); var app = Sub("", 12);
        var ctrls = Row(Btn("⏮", () => _ = Hub.Media.Prev()), Btn("⏯", () => _ = Hub.Media.PlayPause(), true), Btn("⏭", () => _ = Hub.Media.Next()));
        ctrls.Margin = new Thickness(0, 8, 0, 0);
        var info = Stack(title, artist, app, ctrls);
        var dp = new DockPanel();
        DockPanel.SetDock(imgBox, Dock.Left);
        dp.Children.Add(imgBox); dp.Children.Add(info);
        _live.Add(() =>
        {
            img.Source = Hub.Media.Cover;
            title.Text = Hub.Media.Title.Length > 0 ? Hub.Media.Title : "No suena nada";
            artist.Text = Hub.Media.Artist;
            app.Text = Hub.Media.App.Length > 0 ? (Hub.Media.Playing ? "▶ " : "⏸ ") + Hub.Media.App : "Spotify, YouTube o cualquier reproductor";
        });
        return Card(H2("Ahora suena"), dp);
    }

    // =====================================================================
    UIElement PageLights()
    {
        var root = Stack(Header("Iluminación", "RGB de placa, RAM, gráfica, ventiladores y periféricos de cualquier marca (motor: OpenRGB)"));
        if (Components.FindOpenRgb() == null)
            root.Children.Add(Banner("OpenRGB no está instalado: es el motor que habla con el RGB de todas las marcas.", "Instalar", OpenComponents));

        var status = Txt("");
        _live.Add(() => status.Text = Hub.Rgb.Status);
        root.Children.Add(Card(H2("Conexión"), status, Row(
            Btn("Reconectar", async () => { Toast("Conectando con OpenRGB…"); await Hub.Rgb.ConnectAsync(); Refresh(); }),
            Btn("Volver a detectar dispositivos", () => { Hub.Rgb.Refresh(); Refresh(); })),
            Sub("Si una app del fabricante (MSI Center, G HUB, iCUE, Synapse…) está abierta puede pisar los colores. Puedes cerrarlas en Ajustes.", 12)));

        var L = St.Lighting;
        root.Children.Add(Cols(2,
            Card(H2("Efecto"),
                Combo(RgbService.Effects, L.Effect, e => { L.Effect = e; Hub.Rgb.Changed(); SaveSoon(); }),
                LabeledSlider("Velocidad", 1, 100, L.Speed, v => { L.Speed = v; SaveSoon(); }, "0", " %"),
                LabeledSlider("Brillo", 0, 100, L.Brightness, v => { L.Brightness = v; Hub.Rgb.Changed(); SaveSoon(); }, "0", " %"),
                Toggle("Al sonar música, las luces cogen el color de la carátula", St.MediaLights, v => { St.MediaLights = v; SaveSoon(); }),
                Toggle("Luces en rojo mientras OBS graba o emite", St.ObsRecordingLights, v => { St.ObsRecordingLights = v; SaveSoon(); })),
            Card(
                ColorPicker("Color principal", L.Color1, c => { L.Color1 = c; Hub.Rgb.Changed(); SaveSoon(); }),
                new Border { Height = 10 },
                ColorPicker("Color secundario (para «Ciclo de color»)", L.Color2, c => { L.Color2 = c; Hub.Rgb.Changed(); SaveSoon(); }))));

        var list = new StackPanel();
        if (Hub.Rgb.Devices.Count == 0) list.Children.Add(Sub("No hay dispositivos todavía. Pulsa «Reconectar» cuando OpenRGB esté instalado."));
        foreach (var d in Hub.Rgb.Devices)
        {
            var name = d.Name;
            list.Children.Add(Toggle($"{d.Name}   ·   {d.Type}   ·   {d.Leds.Length} LEDs", !L.DisabledDevices.Contains(name), on =>
            {
                if (on) L.DisabledDevices.Remove(name); else if (!L.DisabledDevices.Contains(name)) L.DisabledDevices.Add(name);
                Hub.Rgb.Changed(); SaveSoon();
            }));
        }
        root.Children.Add(Card(H2("Dispositivos RGB"), list));
        return root;
    }

    // =====================================================================
    UIElement PageFans()
    {
        var root = Stack(Header("Ventiladores", "Curvas automáticas para los ventiladores de la placa base y la gráfica (motor: LibreHardwareMonitor)"));
        if (!Components.PawnIO.IsInstalled())
            root.Children.Add(Banner("Falta PawnIO: sin él solo se ven la gráfica y algunos sensores. Con él, cualquier placa base.", "Instalar", OpenComponents));

        root.Children.Add(Cols(3,
            Stat("CPU", () => $"{Hub.Fans.CpuTemp:0} °C", "Accent"),
            Stat("GPU", () => $"{Hub.Fans.GpuTemp:0} °C", "Accent2"),
            Stat("Ventiladores", () => $"{Hub.Fans.Channels.Count(c => c.Control != null)} controlables")));

        root.Children.Add(CoolingPanel());
        if (St.FanPreset == "Personalizado") root.Children.Add(CurveEditor());
        root.Children.Add(Card(H2("Opciones"),
            Row(Sub("Temperatura que siguen las curvas"), Combo(FanService.Sources, St.FanSource, s => { St.FanSource = s; SaveSoon(); }),
                Sub("  Otros modos"), Combo(FanService.Modes, St.FanPreset, m => { St.FanPreset = m; SaveSoon(); Refresh(); }, 180)),
            Sub("Los ventiladores de la gráfica siempre siguen a la GPU. Al cerrar OmniHub todo vuelve a la BIOS.", 12)));

        var list = new StackPanel();
        if (Hub.Fans.Channels.Count == 0) list.Children.Add(Sub(Hub.Fans.Status));
        foreach (var ch in Hub.Fans.Channels)
        {
            var c = ch;
            var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = Stack(Txt(c.Name), Sub(c.Hardware, 11));
            var val = Txt("");
            _live.Add(() => val.Text = (c.RpmValue is { } r ? $"{r:0} RPM" : "") + (c.Percent is { } p ? $"   {p:0} %" : ""));
            g.Children.Add(name);
            Grid.SetColumn(val, 1); g.Children.Add(val);
            if (c.Control != null)
            {
                var mode = St.FanModes.TryGetValue(c.Id, out var m) ? m : "Global";
                var fixedS = new Slider { Minimum = 0, Maximum = 100, Value = St.FanFixed.TryGetValue(c.Id, out var f) ? f : 50, IsEnabled = mode == "Fijo", VerticalAlignment = VerticalAlignment.Center };
                fixedS.ValueChanged += (_, e) => { St.FanFixed[c.Id] = e.NewValue; SaveSoon(); };
                var combo = Combo(new[] { "Global" }.Concat(FanService.Modes), mode, v =>
                {
                    if (v == "Global") St.FanModes.Remove(c.Id); else St.FanModes[c.Id] = v;
                    fixedS.IsEnabled = v == "Fijo"; SaveSoon();
                }, 170);
                Grid.SetColumn(combo, 2); g.Children.Add(combo);
                Grid.SetColumn(fixedS, 3); g.Children.Add(fixedS);
            }
            else
            {
                var ro = Sub("solo lectura", 12); Grid.SetColumn(ro, 2); g.Children.Add(ro);
            }
            list.Children.Add(g);
        }
        root.Children.Add(Card(H2("Canales"), list));
        return root;
    }

    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Selector de modo de refrigeración con baldosas grandes + herramientas de la gráfica.</summary>
    UIElement CoolingPanel()
    {
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-6, 0, -6, 0) };
        (string mode, string glyph, string label)[] modes =
        {
            ("Inteligente", "", "Inteligente"),
            ("Rendimiento", "", "Rendimiento"),
            ("Silencioso", "", "Silencioso"),
            ("Personalizado", "", "Personalizado"),
        };
        foreach (var (mode, glyph, label) in modes)
        {
            bool sel = St.FanPreset == mode;
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 28, Foreground = sel ? B("Accent") : B("Muted"), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = label, FontSize = 15, Margin = new Thickness(14, 0, 0, 0), Foreground = sel ? B("Text") : B("Muted"), VerticalAlignment = VerticalAlignment.Center, FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal });
            var tile = new Border
            {
                Height = 86, CornerRadius = new CornerRadius(12), Margin = new Thickness(6, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand,
                Background = sel ? B("CardHi") : B("Bg"), BorderThickness = new Thickness(sel ? 2 : 1), BorderBrush = sel ? B("AccentGrad") : B("Line"),
                Child = content, ToolTip = mode switch
                {
                    "Inteligente" => "Curva equilibrada que se adelanta a la carga: sube al jugar y se calla en reposo",
                    "Rendimiento" => "Más aire siempre: temperaturas mínimas",
                    "Silencioso" => "Lo más bajo posible hasta que haga falta",
                    _ => "Dibuja tu propia curva"
                }
            };
            var m = mode;
            tile.MouseEnter += (_, _) => { if (St.FanPreset != m) tile.BorderBrush = B("Accent"); };
            tile.MouseLeave += (_, _) => { if (St.FanPreset != m) tile.BorderBrush = B("Line"); };
            tile.MouseLeftButtonUp += (_, _) => { St.FanPreset = m; SaveSoon(); Toast($"Refrigeración: {m}"); Refresh(); };
            tiles.Children.Add(tile);
        }

        var rpm = Txt(""); rpm.FontWeight = FontWeights.SemiBold; rpm.VerticalAlignment = VerticalAlignment.Center;
        _live.Add(() =>
        {
            var r = Hub.Fans.Channels.Where(c => c.RpmValue > 0).Select(c => c.RpmValue!.Value).ToList();
            rpm.Text = r.Count > 0 ? $"Ventiladores: {r.Average():0} RPM" : "Ventiladores: —";
        });
        var control = Toggle("Control OmniHub", St.FanPreset != "Auto (BIOS)", on =>
        {
            St.FanPreset = on ? "Inteligente" : "Auto (BIOS)"; SaveSoon(); Refresh();
        });
        control.Margin = new Thickness(18, 0, 0, 0);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(rpm); right.Children.Add(control);
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        var title = H2("Refrigeración inteligente"); title.Foreground = B("Accent"); title.FontSize = 18;
        head.Children.Add(title);
        var left = Card(head, tiles,
            Sub(St.FanPreset == "Auto (BIOS)" ? "Ahora mismo manda la BIOS de la placa. Elige un modo para que OmniHub tome el control." : $"Modo activo: {St.FanPreset}", 12));

        // Herramientas de la gráfica
        var zero = Toggle("Ventilador cero", St.GpuZeroFan, v => { St.GpuZeroFan = v; SaveSoon(); });
        zero.ToolTip = "Los ventiladores de la gráfica se paran mientras esté fría (ideal para escritorio y vídeo).";
        var zeroLbl = Sub($"Parar por debajo de {St.ZeroFanTemp:0} °C", 12);
        var zeroS = new Slider { Minimum = 35, Maximum = 65, Value = St.ZeroFanTemp, TickFrequency = 1, IsSnapToTickEnabled = true };
        zeroS.ValueChanged += (_, e) => { St.ZeroFanTemp = e.NewValue; zeroLbl.Text = $"Parar por debajo de {e.NewValue:0} °C"; SaveSoon(); };
        var zeroTemp = Stack(zeroLbl, zeroS);
        var turboBtn = Btn(Hub.Fans.Boost ? "Parar" : "Iniciar", () => { }, true);
        turboBtn.Click += (_, _) =>
        {
            Hub.Fans.Boost = !Hub.Fans.Boost;
            turboBtn.Content = Hub.Fans.Boost ? "Parar" : "Iniciar";
            Toast(Hub.Fans.Boost ? "Turbo: todos los ventiladores al 100 %" : "Turbo desactivado");
        };
        var turboRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(turboBtn, Dock.Right); turboBtn.Margin = new Thickness(0);
        turboRow.Children.Add(turboBtn);
        var turboText = Stack(Txt("Turbo"), Sub("Todo al 100 % para enfriar rápido", 12));
        turboRow.Children.Add(turboText);
        var gpuTitle = H2("Herramientas de la gráfica"); gpuTitle.Foreground = B("Accent2"); gpuTitle.FontSize = 18;
        var gpu = Card(gpuTitle, zero, zeroTemp, turboRow);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        grid.Children.Add(left);
        Grid.SetColumn(gpu, 2); grid.Children.Add(gpu);
        return grid;
    }

    /// <summary>Editor de la curva personalizada: 5 puntos (30–90 °C) con vista previa.</summary>
    UIElement CurveEditor()
    {
        const double W = 520, H = 170;
        var canvas = new Canvas { Width = W, Height = H, Background = B("Bg"), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i <= 4; i++)
        {
            canvas.Children.Add(new Line { X1 = 0, X2 = W, Y1 = H * i / 4, Y2 = H * i / 4, Stroke = B("Line"), StrokeThickness = 1 });
            var lbl = new TextBlock { Text = $"{100 - i * 25}%", FontSize = 10, Foreground = B("Muted") };
            Canvas.SetLeft(lbl, 4); Canvas.SetTop(lbl, Math.Min(H * i / 4, H - 14)); canvas.Children.Add(lbl);
        }
        var line = new Polyline { Stroke = B("AccentGrad"), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round };
        var area = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(40, 0, 229, 255)) };
        canvas.Children.Add(area); canvas.Children.Add(line);
        var dots = new List<System.Windows.Shapes.Ellipse>();
        double X(double t) => (t - 20) / 80.0 * W;
        double Y(double p) => H - p / 100.0 * H;
        void Redraw()
        {
            var pts = new PointCollection { new Point(0, Y(St.CustomCurve[0])) };
            for (int i = 0; i < 5; i++) pts.Add(new Point(X(Presets.CustomTemps[i]), Y(St.CustomCurve[i])));
            pts.Add(new Point(W, Y(St.CustomCurve[4])));
            line.Points = pts;
            var poly = new PointCollection(pts) { new Point(W, H), new Point(0, H) };
            area.Points = poly;
            for (int i = 0; i < dots.Count; i++) { Canvas.SetLeft(dots[i], X(Presets.CustomTemps[i]) - 5); Canvas.SetTop(dots[i], Y(St.CustomCurve[i]) - 5); }
        }
        for (int i = 0; i < 5; i++)
        {
            var d = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = B("Text") };
            dots.Add(d); canvas.Children.Add(d);
        }
        var marker = new Line { Stroke = B("Warn"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 3 }, Y1 = 0, Y2 = H };
        canvas.Children.Add(marker);
        _live.Add(() =>
        {
            var t = St.FanSource switch { "CPU" => Hub.Fans.CpuTemp, "GPU" => Hub.Fans.GpuTemp, _ => Math.Max(Hub.Fans.CpuTemp, Hub.Fans.GpuTemp) };
            marker.X1 = marker.X2 = Math.Clamp(X(t), 0, W);
        });

        var sliders = new UniformGrid { Columns = 5 };
        var all = new List<Slider>();
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var v = Txt($"{St.CustomCurve[i]:0} %"); v.HorizontalAlignment = HorizontalAlignment.Center;
            var s = new Slider { Minimum = 0, Maximum = 100, Value = St.CustomCurve[i], Margin = new Thickness(6, 0, 6, 0), TickFrequency = 5, IsSnapToTickEnabled = true };
            s.ValueChanged += (_, e) =>
            {
                St.CustomCurve[idx] = e.NewValue;
                // la curva nunca baja al subir la temperatura
                for (int k = idx + 1; k < 5; k++) if (St.CustomCurve[k] < e.NewValue) St.CustomCurve[k] = e.NewValue;
                for (int k = 0; k < idx; k++) if (St.CustomCurve[k] > e.NewValue) St.CustomCurve[k] = e.NewValue;
                v.Text = $"{e.NewValue:0} %"; Redraw(); SaveSoon();
                for (int k = 0; k < all.Count; k++) if (k != idx && Math.Abs(all[k].Value - St.CustomCurve[k]) > 0.1) all[k].Value = St.CustomCurve[k];
            };
            all.Add(s);
            var t = Sub($"{Presets.CustomTemps[i]:0} °C", 12); t.HorizontalAlignment = HorizontalAlignment.Center;
            sliders.Children.Add(Stack(t, s, v));
        }
        Redraw();
        return Card(H2("Curva personalizada"), canvas, sliders,
            Sub("Velocidad de los ventiladores según la temperatura. La línea discontinua marca la temperatura actual.", 12));
    }

    // =====================================================================
    UIElement PageMouse()
    {
        var root = Stack(Header("Ratón", "Sensibilidad, DPI y botones — funciona con cualquier ratón; Logitech además por hardware"));

        root.Children.Add(Card(H2("Sensibilidad de Windows (cualquier ratón)"),
            LabeledSlider("Velocidad del puntero", 1, 20, MouseService.PointerSpeed, v => MouseService.PointerSpeed = (int)v),
            Toggle("Aceleración (Mejorar precisión del puntero) — desactívala para juegos", MouseService.Acceleration, v => MouseService.Acceleration = v),
            LabeledSlider("Líneas por rueda", 1, 20, MouseService.ScrollLines, v => MouseService.ScrollLines = (int)v)));

        if (Hub.Mouse.Active is { } m)
        {
            var (min, max, step) = m.DpiRange();
            var cur = m.GetDpi();
            var batt = Sub("");
            _live.Add(() => { });
            Task.Run(() => m.Battery()).ContinueWith(t => Dispatcher.BeginInvoke(() => batt.Text = t.Result is { } b ? $"Batería: {b} %" : "Ratón con cable"));
            var rates = m.ReportRates();
            var curRate = m.GetReportRate();
            var stages = new TextBox { Width = 260, Text = string.Join(", ", St.DpiStages) };
            var card = Card(H2(m.Name), batt,
                LabeledSlider("DPI", min, Math.Min(max, 26000), cur > 0 ? cur : 1600, v => Hub.Mouse.SetDpi((int)v), "0", "", step),
                LabeledSlider("DPI francotirador", min, 3200, St.SniperDpi, v => { St.SniperDpi = (int)v; SaveSoon(); }, "0", "", step),
                Sub("Niveles de DPI para «Ciclar DPI» (separados por comas)"),
                Row(stages, Btn("Guardar niveles", () =>
                {
                    St.DpiStages = stages.Text.Split(',').Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).Where(v => v >= min && v <= max).ToList();
                    St.Save(); Toast("Niveles guardados");
                })));
            if (rates.Length > 0)
                ((StackPanel)card.Child).Children.Add(Row(Sub("Tasa de sondeo  "), Combo(rates.Select(r => $"{r} Hz"), $"{curRate} Hz", v =>
                {
                    var err = m.SetReportRate(int.Parse(v.Split(' ')[0]));
                    Toast(err == null ? $"Tasa: {v}" : "No se pudo: " + err);
                }, 140)));
            if (m.HasOnboard)
                ((StackPanel)card.Child).Children.Add(Toggle("Modo software (los ajustes los manda OmniHub en vez de la memoria del ratón)", m.HostMode == true, v => { m.HostMode = v; Toast(v ? "Modo software" : "Modo memoria interna"); }));
            ((StackPanel)card.Child).Children.Add(Sub("Las luces del ratón se controlan en «Iluminación». Si G HUB está abierto puede deshacer estos cambios.", 12));
            root.Children.Add(card);
        }
        else
        {
            root.Children.Add(Card(H2("DPI por hardware"),
                Sub("No he encontrado un ratón con DPI ajustable por software estándar (Logitech HID++). Con cualquier otro ratón funcionan la sensibilidad, los botones y las luces vía OpenRGB; el DPI se cambia con su botón físico."),
                Btn("Buscar de nuevo", () => { Hub.Mouse.Discover(); Refresh(); })));
        }

        var btns = new StackPanel();
        foreach (var b in MouseService.Buttons)
        {
            var bb = b;
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var l = Txt(b); l.VerticalAlignment = VerticalAlignment.Center;
            var c = Combo(Actions.All, St.ButtonMap.TryGetValue(b, out var a) ? a : Actions.Default, v => { St.ButtonMap[bb] = v; SaveSoon(); }, 280);
            Grid.SetColumn(c, 1);
            g.Children.Add(l); g.Children.Add(c);
            btns.Children.Add(g);
        }
        root.Children.Add(Card(H2("Botones (cualquier ratón)"), btns,
            Sub("Los botones extra que el ratón gestiona por sí mismo (p. ej. los de DPI) no llegan a Windows y no se pueden reasignar aquí.", 12)));
        return root;
    }

    // =====================================================================
    UIElement PageAudio()
    {
        var root = Stack(Header("Audio", "Volumen, micrófono y ecualizador para cualquier auricular o altavoz"));

        var outs = Hub.Audio.Outputs();
        var ins = Hub.Audio.Inputs();
        var defOut = Hub.Audio.DefaultOutput;
        var defIn = Hub.Audio.DefaultInput;

        UIElement DeviceBlock(string title, List<NAudio.CoreAudioApi.MMDevice> devs, NAudio.CoreAudioApi.MMDevice? def)
        {
            if (def == null) return Card(H2(title), Sub("No hay dispositivos."));
            var vol = def.AudioEndpointVolume;
            return Card(H2(title),
                Combo(devs.Select(d => d.FriendlyName), def.FriendlyName, n =>
                {
                    var d = devs.FirstOrDefault(x => x.FriendlyName == n);
                    if (d != null) { try { Hub.Audio.SetDefault(d); Toast("Dispositivo predeterminado: " + n); } catch (Exception e) { Toast(e.Message); } Refresh(); }
                }, 360),
                LabeledSlider("Volumen", 0, 100, vol.MasterVolumeLevelScalar * 100, v => vol.MasterVolumeLevelScalar = (float)(v / 100), "0", " %"),
                Toggle("Silenciado", vol.Mute, v => vol.Mute = v));
        }
        root.Children.Add(Cols(2, DeviceBlock("Salida (auriculares/altavoces)", outs, defOut), DeviceBlock("Micrófono", ins, defIn)));

        var winSpatial = Row(
            Btn("Sonido espacial de Windows (Windows Sonic / Dolby Atmos / DTS)", AudioService.OpenWindowsSpatial),
            Btn("Conseguir Dolby Access", () => SystemTools.OpenUrl("ms-windows-store://pdp/?productid=9N0866FS04W8")),
            Btn("Conseguir DTS Sound Unbound", () => SystemTools.OpenUrl("ms-windows-store://pdp/?productid=9PJ0NKL8MCSJ")));

        if (!Hub.Audio.EqAvailable)
        {
            root.Children.Add(Banner("Para el ecualizador, la corrección de tus auriculares y el audio espacial de OmniHub hace falta Equalizer APO (gratis). En su instalador marca tus auriculares.", "Instalar", OpenComponents));
            root.Children.Add(Card(H2("Audio espacial de Windows"), Sub("En la pestaña «Audio espacial» de tu dispositivo puedes activar Windows Sonic (gratis) o Dolby Atmos / DTS si los tienes."), winSpatial));
            return root;
        }

        // ---- Corrección automática para tus auriculares (AutoEq) ----
        var aeStatus = Txt(St.AutoEqName != null ? "✓ Corrección activa: " + St.AutoEqName : string.IsNullOrEmpty(St.AutoEqFile) ? "Sin corrección" : "✓ Perfil importado: " + Path.GetFileName(St.AutoEqFile));
        aeStatus.Foreground = St.AutoEqFile != null ? B("Ok") : B("Muted");
        var search = new TextBox { Width = 260, Text = defOut?.FriendlyName is { } fn && System.Text.RegularExpressions.Regex.Match(fn, @"\(([^)]+)\)") is { Success: true } mm ? mm.Groups[1].Value : "" };
        var results = new ComboBox { Width = 380, HorizontalAlignment = HorizontalAlignment.Left };
        var found = new List<AudioService.AutoEqEntry>();
        async Task DoSearch()
        {
            results.Items.Clear(); found.Clear();
            results.Items.Add("Buscando…"); results.SelectedIndex = 0;
            found.AddRange(await AudioService.SearchAutoEqAsync(search.Text));
            results.Items.Clear();
            foreach (var f in found) results.Items.Add($"{f.Name}   ·   medido por {f.Source}");
            if (found.Count == 0) results.Items.Add("Sin resultados (prueba con la marca y el modelo)");
            results.SelectedIndex = 0;
        }
        root.Children.Add(Card(H2("Corrección para tus auriculares"),
            Sub("Ajusta tus auriculares a una respuesta neutra de referencia usando mediciones reales de AutoEq (más de 8000 modelos). Es lo que más mejora el sonido."),
            aeStatus,
            Row(search, Btn("Buscar", () => _ = DoSearch())),
            Row(results,
                Btn("Aplicar", async () =>
                {
                    if (results.SelectedIndex < 0 || results.SelectedIndex >= found.Count) return;
                    try { await AudioService.DownloadAutoEqAsync(found[results.SelectedIndex]); ApplyEqSoon(); Toast("Corrección aplicada"); Refresh(); }
                    catch (Exception e) { Toast("No se pudo descargar: " + e.Message); }
                }, true),
                Btn("Importar archivo…", () =>
                {
                    var dlg = new OpenFileDialog { Filter = "Perfil Equalizer APO (*.txt)|*.txt" };
                    if (dlg.ShowDialog() != true) return;
                    var dest = Path.Combine(AppInfo.DataDir, "autoeq_" + Path.GetFileName(dlg.FileName));
                    File.Copy(dlg.FileName, dest, true);
                    St.AutoEqFile = dest; St.AutoEqName = null; ApplyEqSoon(); Refresh();
                }),
                Btn("Quitar", () => { St.AutoEqFile = null; St.AutoEqName = null; ApplyEqSoon(); Refresh(); }))));
        if (search.Text.Length > 0) _ = DoSearch();

        // ---- Audio espacial ----
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-6, 0, -6, 12) };
        foreach (var sm in AudioService.SpatialModes)
        {
            bool sel = St.SpatialMode == sm.name;
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            top.Children.Add(new TextBlock { Text = sm.glyph, FontFamily = Icons, FontSize = 24, Foreground = sel ? B("Accent") : B("Muted"), VerticalAlignment = VerticalAlignment.Center });
            top.Children.Add(new TextBlock { Text = sm.name, FontSize = 15, Margin = new Thickness(12, 0, 0, 0), Foreground = sel ? B("Text") : B("Muted"), FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(top);
            content.Children.Add(new TextBlock { Text = sm.hint, FontSize = 11, Foreground = B("Muted"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6, 8, 0) });
            var tile = new Border
            {
                Height = 92, CornerRadius = new CornerRadius(12), Margin = new Thickness(6, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand,
                Background = sel ? B("CardHi") : B("Bg"), BorderThickness = new Thickness(sel ? 2 : 1), BorderBrush = sel ? B("AccentGrad") : B("Line"), Child = content
            };
            var name = sm.name;
            tile.MouseEnter += (_, _) => { if (St.SpatialMode != name) tile.BorderBrush = B("Accent"); };
            tile.MouseLeave += (_, _) => { if (St.SpatialMode != name) tile.BorderBrush = B("Line"); };
            tile.MouseLeftButtonUp += (_, _) => { AudioService.ApplySpatial(name); ApplyEqSoon(); Toast($"Audio espacial: {name}"); Refresh(); };
            tiles.Children.Add(tile);
        }
        void Custom() { St.SpatialMode = "Personalizado"; ApplyEqSoon(); }
        root.Children.Add(Card(H2("Audio espacial y efectos"),
            tiles,
            LabeledSlider("Anchura estéreo", 0, 200, St.Width, v => { St.Width = v; Custom(); }, "0", " %"),
            LabeledSlider("Escenario (crossfeed)", 0, 100, St.Crossfeed, v => { St.Crossfeed = v; Custom(); }, "0", " %"),
            LabeledSlider("Graves", -12, 12, St.Bass, v => { St.Bass = v; Custom(); }, "+0.0;-0.0;0", " dB", 0.5),
            LabeledSlider("Agudos", -12, 12, St.Treble, v => { St.Treble = v; Custom(); }, "+0.0;-0.0;0", " dB", 0.5),
            LabeledSlider("Balance (izq ← → der)", -100, 100, St.Balance, v => { St.Balance = v; ApplyEqSoon(); }, "0"),
            LabeledSlider("Ganancia de salida", -12, 6, St.OutputGain, v => { St.OutputGain = v; ApplyEqSoon(); }, "+0.0;-0.0;0", " dB", 0.5),
            Sub("Elige un modo y retócalo a tu gusto: al mover cualquier control pasa a «Personalizado». También puedes combinarlo con el audio espacial de Windows:", 12),
            winSpatial));

        var bands = new UniformGrid { Columns = 10, Height = 240, Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < 10; i++)
        {
            int idx = i;
            var val = Txt($"{St.Eq[i]:+0;-0;0}"); val.HorizontalAlignment = HorizontalAlignment.Center;
            var s = new Slider { Orientation = Orientation.Vertical, Minimum = -12, Maximum = 12, Value = St.Eq[i], Height = 170, HorizontalAlignment = HorizontalAlignment.Center, TickFrequency = 0.5, IsSnapToTickEnabled = true };
            s.ValueChanged += (_, e) => { St.Eq[idx] = e.NewValue; St.EqPreset = "Personalizado"; val.Text = $"{e.NewValue:+0.#;-0.#;0}"; ApplyEqSoon(); };
            var f = Presets.EqFreqs[i];
            var lab = Sub(f >= 1000 ? $"{f / 1000}k" : $"{f}", 12); lab.HorizontalAlignment = HorizontalAlignment.Center;
            bands.Children.Add(Stack(val, s, lab));
        }
        var presets = Presets.Eq.Keys.Concat(St.EqUserPresets.Keys.Select(k => "★ " + k)).ToList();
        if (St.EqPreset == "Personalizado") presets.Add("Personalizado");
        var presetName = new TextBox { Width = 170 };
        root.Children.Add(Card(H2("Ecualizador de 10 bandas"),
            Row(Combo(presets, St.EqPreset, p =>
            {
                double[]? arr = Presets.Eq.TryGetValue(p, out var a) ? a : St.EqUserPresets.TryGetValue(p.TrimStart('★', ' '), out var u) ? u : null;
                if (arr == null) return;
                St.Eq = (double[])arr.Clone(); St.EqPreset = p; ApplyEqSoon(); Refresh();
            }), Btn("Restablecer", () => { St.Eq = new double[10]; St.EqPreset = "Plano"; ApplyEqSoon(); Refresh(); }),
                Sub("   Guardar como"), presetName,
                Btn("Guardar preset", () =>
                {
                    var n = presetName.Text.Trim();
                    if (n.Length == 0) { Toast("Ponle un nombre"); return; }
                    St.EqUserPresets[n] = (double[])St.Eq.Clone(); St.EqPreset = "★ " + n; St.Save(); Toast($"Preset «{n}» guardado"); Refresh();
                })),
            bands,
            Sub("La ganancia se compensa sola para que no sature. Se aplica a los dispositivos marcados en Equalizer APO.", 12)));

        // ---- Avanzado: filtros a mano ----
        var extra = new TextBox
        {
            Text = St.EqExtra, AcceptsReturn = true, Height = 110, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(Card(H2("Avanzado: filtros personalizados"),
            Sub("Cualquier filtro con la sintaxis de Equalizer APO se añade al final. Ejemplos:", 12),
            Sub("Filter: ON PK Fc 3000 Hz Gain -3 dB Q 2      ·      Filter: ON HP Fc 30 Hz      ·      Channel: L  /  Preamp: -2 dB", 12),
            extra,
            Row(Btn("Aplicar filtros", () => { St.EqExtra = extra.Text; ApplyEqSoon(); Toast("Filtros aplicados"); }, true),
                Btn("Abrir el editor de Equalizer APO", () =>
                {
                    var dir = Components.EqApoConfigDir();
                    var ed = dir == null ? null : Path.Combine(Path.GetDirectoryName(dir)!, "Editor.exe");
                    if (ed != null && File.Exists(ed)) Process.Start(new ProcessStartInfo(ed) { UseShellExecute = true });
                    else Toast("No encuentro el editor de Equalizer APO");
                }))));
        return root;
    }

    // =====================================================================
    UIElement PageIntegrations()
    {
        var root = Stack(Header("Integraciones", "Spotify y multimedia, OBS, Discord, tus apps y perfiles automáticos"));

        root.Children.Add(Cols(2, MediaCard(), Card(H2("Música y luces"),
            Toggle("Las luces siguen el color de la carátula", St.MediaLights, v => { St.MediaLights = v; SaveSoon(); }),
            Sub("Funciona con Spotify, YouTube en el navegador, Apple Music, VLC… cualquier app que aparezca en los controles multimedia de Windows. Asigna «Reproducir / Pausa» o «Siguiente pista» a un botón del ratón en «Ratón».", 12))));

        // ---- OBS ----
        var host = new TextBox { Width = 150, Text = St.ObsHost };
        var port = new TextBox { Width = 80, Text = St.ObsPort.ToString() };
        var pass = new PasswordBox { Width = 200, Password = St.ObsPassword };
        var obsStatus = Txt("");
        var scenes = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var sc in Hub.Obs.Scenes) scenes.Items.Add(sc);
        scenes.SelectedItem = Hub.Obs.CurrentScene;
        scenes.SelectionChanged += (_, _) => { if (scenes.SelectedItem is string s && s != Hub.Obs.CurrentScene) _ = Hub.Obs.SetScene(s); };
        _live.Add(() => obsStatus.Text = Hub.Obs.Connected ? (Hub.Obs.Recording ? "● Grabando" : "") + (Hub.Obs.Streaming ? "  ● En directo" : "") + (Hub.Obs.Recording || Hub.Obs.Streaming ? "" : "Conectado") : Hub.Obs.Status);
        root.Children.Add(Card(H2("OBS Studio"),
            Toggle("Activar integración con OBS", St.ObsEnabled, v => { St.ObsEnabled = v; SaveSoon(); if (v) _ = Hub.Obs.ConnectAsync(); else Hub.Obs.Disconnect(); }),
            Row(Sub("Servidor  "), host, port, Sub("Contraseña  "), pass),
            Row(Btn("Conectar", async () =>
            {
                St.ObsHost = host.Text.Trim(); St.ObsPort = int.TryParse(port.Text, out var p) ? p : 4455; St.ObsPassword = pass.Password; St.ObsEnabled = true; St.Save();
                Toast("Conectando con OBS…");
                var ok = await Hub.Obs.ConnectAsync();
                Toast(ok ? "OBS conectado" : Hub.Obs.Status);
                Refresh();
            }, true), obsStatus),
            Row(Btn("⏺ Grabar / parar", () => _ = Hub.Obs.Request("ToggleRecord")),
                Btn("📡 Emitir / parar", () => _ = Hub.Obs.Request("ToggleStream")),
                Btn("Guardar repetición", () => _ = Hub.Obs.Request("SaveReplayBuffer")),
                Sub("  Escena  "), scenes),
            Toggle("Luces en rojo mientras grabo o emito", St.ObsRecordingLights, v => { St.ObsRecordingLights = v; SaveSoon(); }),
            Sub("En OBS: Herramientas → Ajustes del servidor WebSocket → «Activar servidor WebSocket» y copia la contraseña aquí.", 12)));

        // ---- Discord ----
        var clientId = new TextBox { Width = 230, Text = St.DiscordClientId };
        var hotkey = new TextBox { Width = 150, Text = St.DiscordMuteHotkey };
        var dStatus = Txt("");
        _live.Add(() => dStatus.Text = St.DiscordPresence ? Hub.Discord.Status : "Rich Presence desactivado");
        root.Children.Add(Card(H2("Discord"),
            Toggle("Mostrar OmniHub en mi estado de Discord (Rich Presence)", St.DiscordPresence, v => { St.DiscordPresence = v; SaveSoon(); }),
            Row(Sub("Application ID  "), clientId, Btn("Conectar", async () =>
            {
                St.DiscordClientId = clientId.Text.Trim(); St.DiscordPresence = true; St.Save();
                var ok = await Task.Run(Hub.Discord.Connect);
                Toast(ok ? "Discord conectado" : Hub.Discord.Status);
            }), dStatus),
            Row(Sub("Atajo de «Silenciar» en Discord  "), hotkey, Btn("Guardar", () => { St.DiscordMuteHotkey = hotkey.Text.Trim(); St.Save(); Toast("Atajo guardado"); }),
                Btn("Probar silenciar", () => Actions.Hotkey(St.DiscordMuteHotkey))),
            Sub("Application ID: créalo gratis en discord.com/developers/applications (New Application → copia el ID). Para silenciar desde el ratón asigna «Discord: silenciar micro» y pon aquí el mismo atajo que en Discord → Ajustes → Atajos de teclado.", 12)));

        // ---- Lanzador de apps ----
        var apps = Row();
        foreach (var (name, open) in DetectApps())
        {
            var o = open;
            apps.Children.Add(Btn(name, () => { try { o(); } catch (Exception e) { Toast(e.Message); } }));
        }
        if (apps.Children.Count == 0) apps.Children.Add(Sub("No he encontrado apps conocidas."));
        root.Children.Add(Card(H2("Abrir apps"), apps));

        // ---- Perfiles automáticos ----
        var rules = new StackPanel();
        foreach (var r in St.AppRules.ToList())
        {
            var rr = r;
            var g = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var del = Btn("Quitar", () => { St.AppRules.Remove(rr); St.Save(); Refresh(); });
            DockPanel.SetDock(del, Dock.Right);
            g.Children.Add(del);
            g.Children.Add(Txt($"{rr.Process}  →  {rr.Profile}"));
            rules.Children.Add(g);
        }
        var procs = Process.GetProcesses().Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.ProcessName != "OmniHub"; } catch { return false; } })
            .Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var procBox = new TextBox { Width = 160 };
        var procPick = Combo(new[] { "Apps abiertas…" }.Concat(procs), "Apps abiertas…", v => { if (v != "Apps abiertas…") procBox.Text = v; }, 180);
        var profNames = St.Profiles.Select(p => p.Name).ToList();
        string chosen = profNames.FirstOrDefault() ?? "";
        root.Children.Add(Card(H2("Perfiles automáticos por app"),
            Toggle("Cambiar de perfil según la app que tengo delante", St.AutoProfiles, v => { St.AutoProfiles = v; SaveSoon(); }),
            Row(Sub("Perfil por defecto  "), Combo(new[] { "(ninguno)" }.Concat(profNames), St.DefaultProfile.Length > 0 ? St.DefaultProfile : "(ninguno)", v => { St.DefaultProfile = v == "(ninguno)" ? "" : v; SaveSoon(); })),
            rules,
            Row(Sub("App (proceso)  "), procPick, procBox, Sub("  usa  "), Combo(profNames, chosen, v => chosen = v, 160),
                Btn("Añadir regla", () =>
                {
                    var name = (procBox.Text ?? "").Trim();
                    if (name.Length == 0 || chosen.Length == 0) return;
                    St.AppRules.Add(new AppRule { Process = name, Profile = chosen }); St.Save(); Refresh();
                }, true)),
            Sub("Ejemplo: cs2 → Gaming, obs64 → Silencio, Spotify → Música. Elige una app abierta o escribe el nombre del proceso.", 12)));
        return root;
    }

    static List<(string, Action)> DetectApps()
    {
        var list = new List<(string, Action)>();
        void Uri(string name, string scheme, string url)
        {
            try { if (Registry.ClassesRoot.OpenSubKey(scheme) != null) list.Add((name, () => SystemTools.OpenUrl(url))); } catch { }
        }
        Uri("Spotify", "spotify", "spotify:");
        Uri("Discord", "discord", "discord://");
        Uri("Steam", "steam", "steam://open/main");
        Uri("Epic Games", "com.epicgames.launcher", "com.epicgames.launcher://");
        Uri("Battle.net", "battlenet", "battlenet://");
        Uri("EA app", "origin2", "origin2://");
        Uri("Ubisoft Connect", "uplay", "uplay://");
        Uri("Xbox", "msxbox", "msxbox://");
        var obs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio", "bin", "64bit", "obs64.exe");
        if (File.Exists(obs)) list.Add(("OBS Studio", () => Process.Start(new ProcessStartInfo(obs) { WorkingDirectory = Path.GetDirectoryName(obs)!, UseShellExecute = true })));
        return list;
    }

    // =====================================================================
    UIElement PageProfiles()
    {
        var root = Stack(Header("Perfiles", "Guarda combinaciones de luces, ventiladores, DPI y ecualizador"));
        foreach (var p in St.Profiles.ToList())
        {
            var pp = p;
            var dp = new DockPanel();
            var btns = Row(
                Btn("Aplicar", () => { Hub.ApplyProfile(pp); Refresh(); }, true),
                Btn("Sobrescribir con lo actual", () =>
                {
                    var snap = Hub.Snapshot(pp.Name);
                    St.Profiles[St.Profiles.IndexOf(pp)] = snap; St.Save(); Toast($"Perfil «{pp.Name}» actualizado"); Refresh();
                }),
                Btn("Borrar", () =>
                {
                    if (MessageBox.Show($"¿Borrar el perfil «{pp.Name}»?", "OmniHub", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    { St.Profiles.Remove(pp); St.Save(); Refresh(); }
                }));
            DockPanel.SetDock(btns, Dock.Right);
            dp.Children.Add(btns);
            var title = H2((p.Name == Hub.ActiveProfile ? "● " : "") + p.Name);
            dp.Children.Add(Stack(title, Sub($"Luces: {p.Lighting.Effect} · Ventiladores: {p.FanPreset} · EQ: {p.EqPreset}" + (p.Dpi > 0 ? $" · {p.Dpi} DPI" : ""), 12)));
            root.Children.Add(Card(dp));
        }
        var nameBox = new TextBox { Width = 240 };
        root.Children.Add(Card(H2("Nuevo perfil"), Row(nameBox, Btn("Guardar estado actual como perfil", () =>
        {
            var n = nameBox.Text.Trim();
            if (n.Length == 0) { Toast("Ponle un nombre"); return; }
            St.Profiles.RemoveAll(x => x.Name == n);
            St.Profiles.Add(Hub.Snapshot(n)); St.Save(); Toast($"Perfil «{n}» guardado"); Refresh();
        }, true))));
        return root;
    }

    // =====================================================================
    UIElement PageSettings()
    {
        var vendors = VendorApps.Running().Select(v => v.Name).ToList();
        return Stack(
            Header("Ajustes", $"OmniHub {AppInfo.Version}"),
            Card(H2("General"),
                Toggle("Al cerrar la ventana, seguir en la bandeja del sistema", St.CloseToTray, v => { St.CloseToTray = v; SaveSoon(); }),
                Toggle("Saltar el launcher al abrir", St.SkipLauncher, v => { St.SkipLauncher = v; SaveSoon(); }),
                Toggle("Iniciar con Windows", St.StartWithWindows, v => { St.StartWithWindows = v; SystemTools.SetStartup(v, AppInfo.ExePath); SaveSoon(); })),
            Card(H2("Apps del fabricante"),
                Sub(vendors.Count == 0 ? "No hay ninguna abierta ahora mismo." : "Abiertas ahora: " + string.Join(", ", vendors)),
                Toggle("Cerrarlas automáticamente al abrir OmniHub", St.CloseVendorApps, v => { St.CloseVendorApps = v; SaveSoon(); }),
                Btn("Cerrarlas ahora", () => { var n = VendorApps.CloseAll(); Toast($"{n} proceso(s) cerrados"); Refresh(); }),
                Sub("Solo se cierran las ventanas/bandeja de esas apps, no se desinstalan. Sus servicios de Windows pueden seguir activos.", 12)),
            Card(H2("Componentes y mantenimiento"),
                Row(Btn("Componentes…", OpenComponents),
                    Btn("Abrir carpeta de datos", () => SystemTools.OpenUrl(AppInfo.DataDir)),
                    Btn("Volver a escanear dispositivos", async () =>
                    {
                        await Task.Run(Hub.Mouse.Discover); Hub.Rgb.Refresh(); Toast("Dispositivos actualizados");
                    }),
                    Btn("Desinstalar OmniHub", () =>
                    {
                        try { Process.Start(new ProcessStartInfo(AppInfo.ExePath, "--uninstall") { UseShellExecute = true }); } catch { }
                        ExitApp();
                    }))),
            Card(H2("Actualizaciones"),
                Sub($"Las versiones se publican en {Updater.RepoUrl}/releases"),
                Row(Btn("Buscar actualizaciones", async () =>
                    {
                        var r = await Updater.LatestAsync();
                        if (r == null) { Toast("No se pudo consultar GitHub"); return; }
                        if (r.Version <= Updater.Current) { Toast($"Ya tienes la última versión ({AppInfo.Version})"); return; }
                        if (MessageBox.Show($"Hay una versión nueva: {r.Tag}. ¿Descargar e instalar ahora?", "OmniHub", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            Toast("Descargando actualización…");
                            try { await Updater.SelfUpdateAsync(r, null); } catch (Exception ex) { Toast(ex.Message); }
                        }
                    }, true),
                    Btn("Abrir el repositorio", () => SystemTools.OpenUrl(Updater.RepoUrl)))),
            Card(H2("Acerca de"),
                Sub("OmniHub une en una app el control de luces, ventiladores, ratón y audio de cualquier marca usando motores abiertos: OpenRGB (iluminación), LibreHardwareMonitor + PawnIO (sensores y ventiladores), Equalizer APO (ecualizador) y HID++ (ratones Logitech). No sustituye al firmware: si algo no aparece, es que ese dispositivo aún no es compatible con esos motores.")));
    }
}
