using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OmniHub.Services;
using Path = System.Windows.Shapes.Path;

namespace OmniHub.UI;

/// <summary>
/// Página del ratón al estilo de los programas de los fabricantes:
/// subpestañas (Asignaciones / Sensibilidad / Luces), panel de comandos con buscador
/// y un mapa del ratón con etiquetas. Se asigna haciendo clic o arrastrando.
/// </summary>
public partial class MainWindow
{
    record MapSpot(string Id, string Name, double X, double Y, bool Left, int DefaultButton, bool Fixed = false);

    // Coordenadas sobre el dibujo del ratón (290 × 540)
    static readonly MapSpot[] Spots =
    {
        new("L",  "Clic principal",            92, 112, true,  1, true),
        new("G8", "Botón G8",                  56, 104, true,  7),
        new("G7", "Botón G7",                  54, 150, true,  8),
        new("G6", "Francotirador (G6)",        24, 246, true,  6),
        new("G5", "Adelante (G5)",             16, 298, true,  5),
        new("G4", "Atrás (G4)",                14, 350, true,  4),
        new("M",  "Clic central",             150, 112, false, 3, true),
        new("R",  "Clic secundario",          208, 112, false, 2, true),
        new("TL", "Rueda a la izquierda",     128, 124, false, 10),
        new("TR", "Rueda a la derecha",       172, 124, false, 11),
        new("G9", "Botón G9",                 150, 214, false, 9),
    };

    static string _mouseTab = "Asignaciones";
    static string _assignTab = "Comandos";
    static string _selectedSpot = "G8";
    static bool _shiftLayer;
    static bool _calibrating;
    static string _search = "";

    int SpotButton(MapSpot s) => St.MouseMapPos.TryGetValue(s.Id, out var n) ? n : s.DefaultButton;

    UIElement PageMouse()
    {
        var name = Hub.Mouse.Active?.Name.Replace("Logitech ", "") ?? "Ratón";
        var head = new DockPanel { Margin = new Thickness(0, 4, 0, 18) };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var t in new[] { ("Asignaciones", ""), ("Sensibilidad", ""), ("Luces", "") })
        {
            var tt = t.Item1;
            var sel = _mouseTab == tt;
            var b = new Button
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = {
                    new TextBlock { Text = t.Item2, FontFamily = Icons, FontSize = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = tt, VerticalAlignment = VerticalAlignment.Center } } },
                Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 9, 16, 9),
            };
            if (sel) b.Style = S("AccentButton");
            b.Click += (_, _) => { _mouseTab = tt; Refresh(); };
            tabs.Children.Add(b);
        }
        DockPanel.SetDock(tabs, Dock.Right);
        head.Children.Add(tabs);
        var title = new TextBlock { Text = name.ToUpperInvariant(), FontSize = 30, FontWeight = FontWeights.Bold, Foreground = B("Text") };
        head.Children.Add(title);

        var root = new StackPanel();
        root.Children.Add(head);
        root.Children.Add(_mouseTab switch
        {
            "Sensibilidad" => MouseSensitivityPage(),
            "Luces" => MouseLightsPage(),
            _ => Hub.Mouse.GButtonCount > 3 ? AssignmentsPage() : MouseGenericButtons(),
        });
        return root;
    }

    // =====================================================================
    UIElement AssignmentsPage()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var panel = CommandPanel();
        grid.Children.Add(panel);
        var map = MapPanel();
        Grid.SetColumn(map, 2);
        grid.Children.Add(map);
        return grid;
    }

    // ---------- Panel de comandos (izquierda) ----------
    record Cmd(string Group, string Keys, string Label, string Action);

    static IEnumerable<Cmd> Commands() => new Cmd[]
    {
        new("Discord", "", "Silenciarme", "Discord: silenciar micro (Ctrl+Shift+M)"),
        new("Discord", "", "Desactivar mi escucha", "Discord: ensordecer (Ctrl+Shift+D)"),
        new("Ratón", "", "Atrás", "Atrás"), new("Ratón", "", "Adelante", "Adelante"),
        new("Ratón", "", "Desplazar a la izquierda", "Desplazar a la izquierda"), new("Ratón", "", "Desplazar a la derecha", "Desplazar a la derecha"),
        new("Ratón", "", "G-Shift (segunda capa)", Actions.GShift),
        new("DPI", "", "Subir DPI", "Subir DPI"), new("DPI", "", "Bajar DPI", "Bajar DPI"),
        new("DPI", "", "Ciclar DPI", "Ciclar DPI"), new("DPI", "", "Francotirador (mantener)", "Francotirador DPI (mantener)"),
        new("Multimedia", "", "Reproducir / Pausa", "Reproducir / Pausa"), new("Multimedia", "", "Siguiente pista", "Siguiente pista"),
        new("Multimedia", "", "Pista anterior", "Pista anterior"), new("Multimedia", "", "Subir volumen", "Subir volumen"),
        new("Multimedia", "", "Bajar volumen", "Bajar volumen"), new("Multimedia", "", "Silenciar sonido", "Silenciar sonido"),
        new("Multimedia", "", "Silenciar micrófono", "Silenciar micrófono (sistema)"),
        new("Edición", "CTRL + C", "Copiar", "Atajo: Ctrl+C"), new("Edición", "CTRL + V", "Pegar", "Atajo: Ctrl+V"),
        new("Edición", "CTRL + X", "Cortar", "Atajo: Ctrl+X"), new("Edición", "CTRL + Z", "Deshacer", "Atajo: Ctrl+Z"),
        new("Edición", "CTRL + Y", "Rehacer", "Atajo: Ctrl+Y"), new("Edición", "CTRL + A", "Seleccionar todo", "Atajo: Ctrl+A"),
        new("Edición", "CTRL + S", "Guardar", "Atajo: Ctrl+S"), new("Edición", "CTRL + T", "Nueva pestaña", "Atajo: Ctrl+T"),
        new("Edición", "CTRL + W", "Cerrar pestaña", "Atajo: Ctrl+W"), new("Edición", "CTRL + F", "Buscar", "Atajo: Ctrl+F"),
        new("Sistema", "WIN + D", "Mostrar escritorio", "Mostrar escritorio"), new("Sistema", "WIN + SHIFT + S", "Captura de pantalla", "Captura de pantalla"),
        new("Sistema", "CTRL + SHIFT + ESC", "Administrador de tareas", "Administrador de tareas"), new("Sistema", "ALT + TAB", "Cambiar de ventana", "Atajo: Alt+Tab"),
        new("Sistema", "", "Turbo ventiladores", "Turbo ventiladores (activar/parar)"), new("Sistema", "", "Abrir OmniHub", "Abrir OmniHub"),
        new("Sistema", "", "Desactivar el botón", "Desactivado"), new("Sistema", "", "Restablecer (predeterminado)", Actions.Default),
    };

    UIElement CommandPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Asignaciones", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), Margin = new Thickness(0, 0, 0, 14) });
        var tabs = new WrapPanel();
        foreach (var t in new[] { "Comandos", "Teclas", "Macros" })
        {
            var tt = t;
            var tb = new TextBlock
            {
                Text = t.ToUpperInvariant(), FontSize = 15, Margin = new Thickness(0, 0, 22, 10), Cursor = Cursors.Hand,
                Foreground = _assignTab == t ? B("Text") : B("Muted"),
                TextDecorations = _assignTab == t ? TextDecorations.Underline : null,
            };
            tb.MouseLeftButtonUp += (_, _) => { _assignTab = tt; Refresh(); };
            tabs.Children.Add(tb);
        }
        sp.Children.Add(tabs);
        sp.Children.Add(Sub("Haz clic en un comando (o arrástralo a un botón del ratón) para asignarlo al botón seleccionado.", 12));
        sp.Children.Add(new Border { Height = 10 });

        var list = new StackPanel();
        if (_assignTab == "Comandos")
        {
            var search = new TextBox { Text = _search, Margin = new Thickness(0, 0, 0, 10), ToolTip = "Buscar un comando" };
            sp.Children.Add(search);
            void Fill()
            {
                list.Children.Clear();
                var q = search.Text.Trim();
                foreach (var g in Commands().Where(c => q.Length == 0 || c.Label.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Keys.Contains(q, StringComparison.OrdinalIgnoreCase)).GroupBy(c => c.Group))
                {
                    list.Children.Add(new TextBlock { Text = g.Key, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), Margin = new Thickness(0, 10, 0, 4) });
                    foreach (var c in g) list.Children.Add(CommandItem(c.Keys.Length > 0 ? c.Keys : c.Label, c.Keys.Length > 0 ? c.Label : "", c.Action));
                }
            }
            search.TextChanged += (_, _) => { _search = search.Text; Fill(); };
            Fill();
        }
        else if (_assignTab == "Teclas")
        {
            var box = new TextBox { IsReadOnly = true, Text = "Haz clic aquí y pulsa la combinación…", Margin = new Thickness(0, 0, 0, 8) };
            string combo = "";
            box.PreviewKeyDown += (_, e) =>
            {
                e.Handled = true;
                var k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
                var parts = new List<string>();
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
                parts.Add(KeyName(k));
                combo = string.Join("+", parts);
                box.Text = combo;
            };
            var hold = new CheckBox { Content = "Mantener mientras pulso el botón (pulsar para hablar)" };
            sp.Children.Add(box);
            sp.Children.Add(hold);
            sp.Children.Add(Btn("Asignar al botón seleccionado", () =>
            {
                if (combo.Length == 0 || Actions.Parse(combo).Count == 0) { Toast("Pulsa primero una combinación"); return; }
                Assign(_selectedSpot, (hold.IsChecked == true ? "Mantener: " : "Atajo: ") + combo);
            }, true));
            list.Children.Add(new TextBlock { Text = "Teclas extra", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), Margin = new Thickness(0, 14, 0, 4) });
            foreach (var f in Enumerable.Range(13, 12)) list.Children.Add(CommandItem($"F{f}", "Tecla que nadie usa: ideal para atajos de Discord/OBS", $"Atajo: F{f}"));
        }
        else
        {
            var name = new TextBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Nombre de la macro" };
            var spec = new TextBox { Height = 70, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "Pasos separados por comas. Ej.: Ctrl+A, 50, Ctrl+C, 200, Alt+Tab, texto:gg wp, Enter" };
            sp.Children.Add(Sub("Nombre", 12)); sp.Children.Add(name);
            sp.Children.Add(Sub("Pasos (atajos, esperas en ms y texto:…)", 12)); sp.Children.Add(spec);
            sp.Children.Add(Row(Btn("Guardar macro", () =>
            {
                var n = name.Text.Trim();
                if (n.Length == 0 || spec.Text.Trim().Length == 0) { Toast("Pon nombre y pasos"); return; }
                St.Macros[n] = spec.Text.Trim(); St.Save(); Refresh();
            }, true), Btn("Probar (en 2 s)", async () => { await Task.Delay(2000); Actions.RunMacro(spec.Text); })));
            list.Children.Add(new TextBlock { Text = "Mis macros", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), Margin = new Thickness(0, 14, 0, 4) });
            if (St.Macros.Count == 0) list.Children.Add(Sub("Aún no tienes macros.", 12));
            foreach (var (n, s) in St.Macros) list.Children.Add(CommandItem(n, s, "Macro: " + s));
        }
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 470 };
        sp.Children.Add(scroll);
        return new Border { Background = B("Card"), CornerRadius = new CornerRadius(14), Padding = new Thickness(22), BorderBrush = B("Line"), BorderThickness = new Thickness(1), Child = sp, VerticalAlignment = VerticalAlignment.Top };
    }

    UIElement CommandItem(string left, string right, string action)
    {
        var dp = new DockPanel { Margin = new Thickness(0) };
        var r = new TextBlock { Text = right, Foreground = B("Muted"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200 };
        DockPanel.SetDock(r, Dock.Right);
        dp.Children.Add(r);
        dp.Children.Add(new TextBlock { Text = left, Foreground = B("Text"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        var item = new Border { Padding = new Thickness(8, 9, 8, 9), Child = dp, Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderBrush = B("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };
        item.MouseEnter += (_, _) => item.Background = B("CardHi");
        item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        Point start = default;
        item.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(item);
        item.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(item);
            if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) > 8) DragDrop.DoDragDrop(item, action, DragDropEffects.Copy);
        };
        item.MouseLeftButtonUp += (_, _) => Assign(_selectedSpot, action);
        return item;
    }

    void Assign(string spotId, string action)
    {
        var s = Spots.First(x => x.Id == spotId);
        if (s.Fixed) { Toast("Los clics principal, secundario y central no se reasignan"); return; }
        var key = MouseService.GKey(SpotButton(s), _shiftLayer);
        if (action == Actions.Default) St.ButtonMap.Remove(key); else St.ButtonMap[key] = action;
        St.Save();
        Hub.Mouse.ApplyRemap();
        Toast($"{s.Name}{(_shiftLayer ? " (G-Shift)" : "")}: {Friendly(action, SpotButton(s))}");
        Refresh();
    }

    static string Friendly(string a, int btn) => a switch
    {
        Actions.Default => btn == 4 ? "Atrás" : btn == 5 ? "Adelante" : "Sin asignar",
        "Discord: silenciar micro (Ctrl+Shift+M)" => "Silenciarme",
        "Discord: ensordecer (Ctrl+Shift+D)" => "Desactivar mi escucha",
        "Desactivado" => "Desactivado",
        _ when a.StartsWith("Atajo: ") => a[7..].ToUpperInvariant().Replace("+", " + "),
        _ when a.StartsWith("Mantener: ") => "Mantener " + a[10..].ToUpperInvariant(),
        _ when a.StartsWith("Macro: ") => "Macro",
        _ => a,
    };

    static string KeyName(Key k) => k switch
    {
        >= Key.A and <= Key.Z => k.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (int)(k - Key.NumPad0),
        >= Key.F1 and <= Key.F24 => k.ToString(),
        Key.Space => "Espacio", Key.Enter => "Enter", Key.Tab => "Tab", Key.Escape => "Esc",
        Key.Delete => "Supr", Key.Insert => "Insert", Key.Home => "Inicio", Key.End => "Fin",
        Key.PageUp => "RePag", Key.PageDown => "AvPag", Key.Up => "Arriba", Key.Down => "Abajo", Key.Left => "Izquierda", Key.Right => "Derecha",
        _ => k.ToString(),
    };

    // ---------- Mapa del ratón (derecha) ----------
    const double MouseX = 250, MouseY = 30, MapW = 790, MapH = 600;

    UIElement MapPanel()
    {
        var spots = Spots.Where(s => s.Fixed || SpotButton(s) <= Hub.Mouse.GButtonCount).ToArray();
        var canvas = new Canvas { Width = MapW, Height = MapH };

        // Dibujo propio del ratón (vista superior)
        var mouse = new Canvas { Width = 290, Height = 540 };
        var body = Geometry.Parse("M150,10 C232,10 282,70 286,190 C290,320 268,450 214,508 C182,540 118,540 86,512 C56,486 40,440 30,392 C22,352 -6,330 4,292 C12,262 34,250 40,226 C46,170 52,60 150,10 Z");
        var bodyBrush = new LinearGradientBrush(Color.FromRgb(0x2A, 0x2F, 0x3C), Color.FromRgb(0x0F, 0x11, 0x17), 90);
        Add(mouse, new Path { Data = body, Fill = bodyBrush, Stroke = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x50)), StrokeThickness = 2 });
        Add(mouse, new Path { Data = Geometry.Parse("M150,12 L150,60 M150,178 L150,236 M58,240 C100,254 200,254 280,236"), Stroke = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x50)), StrokeThickness = 2 });
        Add(mouse, new Rectangle { Width = 24, Height = 104, RadiusX = 11, RadiusY = 11, Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x25)), Stroke = B("Muted"), StrokeThickness = 1.5 }, 138, 66);
        for (int i = 0; i < 7; i++) Add(mouse, new Line { X1 = 142, X2 = 158, Y1 = 76 + i * 13, Y2 = 76 + i * 13, Stroke = new SolidColorBrush(Color.FromRgb(0x4A, 0x50, 0x60)), StrokeThickness = 2 });
        var btnFill = new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x31));
        var btnLine = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x50));
        Add(mouse, new Rectangle { Width = 14, Height = 40, RadiusX = 6, RadiusY = 6, Fill = btnFill, Stroke = btnLine }, 49, 84);
        Add(mouse, new Rectangle { Width = 14, Height = 40, RadiusX = 6, RadiusY = 6, Fill = btnFill, Stroke = btnLine }, 47, 130);
        Add(mouse, new Rectangle { Width = 22, Height = 16, RadiusX = 5, RadiusY = 5, Fill = btnFill, Stroke = btnLine }, 139, 206);
        Add(mouse, new Ellipse { Width = 20, Height = 20, Fill = btnFill, Stroke = btnLine }, 14, 236);
        Add(mouse, new Rectangle { Width = 18, Height = 40, RadiusX = 7, RadiusY = 7, Fill = btnFill, Stroke = btnLine }, 7, 278);
        Add(mouse, new Rectangle { Width = 18, Height = 40, RadiusX = 7, RadiusY = 7, Fill = btnFill, Stroke = btnLine }, 5, 330);
        Add(mouse, new Ellipse { Width = 40, Height = 40, Stroke = B("AccentGrad"), StrokeThickness = 3 }, 130, 400);
        Add(canvas, mouse, MouseX, MouseY);

        var dots = new Dictionary<string, Ellipse>();
        var labels = new Dictionary<string, TextBlock>();
        var lineBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x72, 0x85));
        void Place(MapSpot[] list, bool left)
        {
            for (int i = 0; i < list.Length; i++)
            {
                var s = list[i];
                int btn = SpotButton(s);
                double y = 70 + i * (MapH - 140) / Math.Max(1, list.Length - 1);
                double dx = MouseX + s.X, dy = MouseY + s.Y;
                double lx0 = left ? 0 : MapW - 230, lx1 = left ? 230 : MapW;
                double elbow = left ? lx1 : lx0;
                Add(canvas, new Polyline { Points = new PointCollection { new(lx0, y), new(lx1, y), new(left ? elbow + 30 : elbow - 30, y), new(dx, dy) }, Stroke = lineBrush, StrokeThickness = 1 });

                string action = s.Fixed ? s.Name : GetAction(btn);
                bool isSel = s.Id == _selectedSpot;
                var txt = new TextBlock
                {
                    Text = s.Fixed ? s.Name : Friendly(action, btn), FontSize = 16, FontWeight = FontWeights.SemiBold,
                    Foreground = isSel ? B("Accent") : s.Fixed ? B("Muted") : B("Text"), Width = 230, TextAlignment = left ? TextAlignment.Left : TextAlignment.Right,
                    TextTrimming = TextTrimming.CharacterEllipsis, AllowDrop = !s.Fixed, Cursor = s.Fixed ? null : Cursors.Hand, Background = Brushes.Transparent,
                    ToolTip = s.Fixed ? null : $"{s.Name} · botón {btn}",
                };
                if (action.StartsWith("Discord")) txt.Text = "● " + txt.Text;
                var sub = new TextBlock { Text = s.Fixed ? "" : s.Name, FontSize = 11, Foreground = B("Muted"), Width = 230, TextAlignment = left ? TextAlignment.Left : TextAlignment.Right };
                Add(canvas, txt, lx0, y - 26);
                if (!s.Fixed) Add(canvas, sub, lx0, y + 3);
                labels[s.Id] = txt;

                var dot = new Ellipse { Width = 18, Height = 18, Fill = isSel ? B("Accent") : Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x18)), StrokeThickness = 3, AllowDrop = !s.Fixed, Cursor = s.Fixed ? null : Cursors.Hand };
                Add(canvas, dot, dx - 9, dy - 9);
                dots[s.Id] = dot;

                if (!s.Fixed)
                {
                    var id = s.Id;
                    foreach (FrameworkElement fe in new FrameworkElement[] { txt, dot })
                    {
                        fe.MouseLeftButtonUp += (_, _) => { _selectedSpot = id; Refresh(); };
                        fe.DragEnter += (_, e) => { dot.Fill = B("Warn"); e.Effects = DragDropEffects.Copy; };
                        fe.DragLeave += (_, _) => dot.Fill = id == _selectedSpot ? B("Accent") : Brushes.White;
                        fe.Drop += (_, e) => { if (e.Data.GetData(typeof(string)) is string a) { _selectedSpot = id; Assign(id, a); } };
                    }
                }
            }
        }
        Place(spots.Where(s => s.Left).ToArray(), true);
        Place(spots.Where(s => !s.Left).ToArray(), false);

        // Pulsar un botón físico lo resalta (o lo calibra)
        var status = Sub(_calibrating ? "Calibrando: pulsa en el ratón el botón que corresponde al punto seleccionado…" : "Pulsa un botón del ratón para verlo en el mapa.", 12);
        if (_calibrating) status.Foreground = B("Warn");
        void Pressed(int b) => Dispatcher.BeginInvoke(() =>
        {
            if (_calibrating && b > 3)
            {
                foreach (var k in St.MouseMapPos.Where(kv => kv.Value == b).Select(kv => kv.Key).ToList()) St.MouseMapPos.Remove(k);
                St.MouseMapPos[_selectedSpot] = b;
                _calibrating = false; St.Save();
                Toast($"«{Spots.First(x => x.Id == _selectedSpot).Name}» = botón {b}");
                Refresh();
                return;
            }
            var hit = spots.FirstOrDefault(s => SpotButton(s) == b);
            foreach (var (id, d) in dots) d.Fill = hit?.Id == id ? B("Warn") : id == _selectedSpot ? B("Accent") : Brushes.White;
            status.Text = hit != null ? $"Has pulsado: {hit.Name} (botón {b})" : $"Has pulsado el botón {b}";
        });
        Hub.Mouse.GButtonPressed += Pressed;
        _cleanup.Add(() => Hub.Mouse.GButtonPressed -= Pressed);

        // Barra inferior: capa Predeterminado / G-Shift
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        var lblDef = new TextBlock { Text = "PREDETERMINADO", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = _shiftLayer ? B("Muted") : B("Text"), Margin = new Thickness(0, 0, 12, 0) };
        var sw = new CheckBox { IsChecked = _shiftLayer, VerticalAlignment = VerticalAlignment.Center };
        sw.Checked += (_, _) => { _shiftLayer = true; Refresh(); };
        sw.Unchecked += (_, _) => { _shiftLayer = false; Refresh(); };
        var lblShift = new TextBlock { Text = "G-SHIFT", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = _shiftLayer ? B("Accent") : B("Muted"), Margin = new Thickness(2, 0, 0, 0) };
        bottom.Children.Add(lblDef); bottom.Children.Add(sw); bottom.Children.Add(lblShift);

        var sel = Spots.FirstOrDefault(s => s.Id == _selectedSpot && !s.Fixed) ?? Spots.First(s => !s.Fixed);
        var tools = Row(
            Btn(_calibrating ? "Cancelar calibración" : "Calibrar punto", () => { _calibrating = !_calibrating; Refresh(); }),
            Btn("Restablecer botón", () => Assign(sel.Id, Actions.Default)),
            Btn("Restablecer mapa", () => { St.MouseMapPos.Clear(); St.Save(); Refresh(); }));
        tools.HorizontalAlignment = HorizontalAlignment.Center;
        var selInfo = Sub($"Seleccionado: {sel.Name} (botón {SpotButton(sel)}){(_shiftLayer ? " · capa G-Shift: lo que hace mientras mantienes el botón de G-Shift" : "")}", 12);
        selInfo.HorizontalAlignment = HorizontalAlignment.Center;
        status.HorizontalAlignment = HorizontalAlignment.Center;

        var view = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 600 };
        var sp = new StackPanel();
        sp.Children.Add(status);
        sp.Children.Add(view);
        sp.Children.Add(bottom);
        sp.Children.Add(selInfo);
        sp.Children.Add(tools);
        sp.Children.Add(Sub("Todo se configura directamente en el ratón, sin G HUB. Al cerrar OmniHub, los laterales vuelven a ser Atrás/Adelante.", 11));
        return new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Child = sp };
    }

    string GetAction(int btn) =>
        _shiftLayer && St.ButtonMap.TryGetValue(MouseService.GKey(btn, true), out var s) ? s
        : _shiftLayer ? Actions.Default
        : St.ButtonMap.TryGetValue(MouseService.GKey(btn), out var a) ? a : Actions.Default;

    // ---------- Luces del ratón ----------
    UIElement MouseLightsPage()
    {
        var mouseDevs = Hub.Rgb.Devices.Where(d => d.Type == OpenRGB.NET.DeviceType.Mouse).ToList();
        return Card(H2("Luces del ratón"),
            Sub(mouseDevs.Count > 0 ? "Tu ratón: " + string.Join(", ", mouseDevs.Select(d => d.Name)) : "El ratón aparecerá aquí cuando el módulo de iluminación lo detecte."),
            Sub("Los efectos y colores se eligen en «Iluminación» y se aplican a la vez a todo el equipo (ratón, placa, RAM, gráfica…). Allí puedes desmarcar el ratón si lo quieres apagado."),
            Btn("Ir a Iluminación", () => { foreach (RadioButton r in Nav.Children) if ((string)r.Tag == "Luces") r.IsChecked = true; }, true));
    }

    UIElement MouseGenericButtons()
    {
        var btns = new StackPanel();
        foreach (var b in MouseService.Buttons)
            btns.Children.Add(ButtonRow(b, b, null));
        return Card(H2("Botones (cualquier ratón)"), btns,
            Sub("Los botones extra que el ratón gestiona por sí mismo (p. ej. los de DPI) no llegan a Windows y no se pueden reasignar aquí.", 12));
    }

    static void Add(Canvas c, UIElement e, double x = 0, double y = 0)
    {
        Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
        c.Children.Add(e);
    }
}
