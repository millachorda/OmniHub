using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniHub.Core;

namespace OmniHub.UI;

/// <summary>Ventana para instalar/reintentar componentes desde Ajustes.</summary>
public class ComponentsWindow : Window
{
    readonly TextBlock _log = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _bar = new() { Height = 4, Maximum = 1, Margin = new Thickness(0, 6, 0, 10), BorderThickness = new Thickness(0) };
    readonly StackPanel _list = new();

    public ComponentsWindow()
    {
        Title = "Módulos de OmniHub";
        Width = 620; Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");
        Foreground = (Brush)Application.Current.FindResource("Text");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        _log.Foreground = (Brush)Application.Current.FindResource("Muted");
        _bar.Foreground = (Brush)Application.Current.FindResource("Accent");
        _bar.Background = (Brush)Application.Current.FindResource("Line");

        var root = new DockPanel { Margin = new Thickness(24) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "Módulos", Style = (Style)Application.Current.FindResource("H1") });
        head.Children.Add(new TextBlock { Text = "Se instalan en segundo plano desde el repositorio de OmniHub y se quitan al desinstalarlo.", Style = (Style)Application.Current.FindResource("Sub") });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        DockPanel.SetDock(_list, Dock.Top);
        _list.Margin = new Thickness(0, 14, 0, 0);
        root.Children.Add(_list);
        DockPanel.SetDock(_bar, Dock.Top);
        root.Children.Add(_bar);
        root.Children.Add(new ScrollViewer { Content = _log, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Build();
    }

    void Build()
    {
        _list.Children.Clear();
        foreach (var c in Components.All)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            bool ok = c.IsInstalled();
            var btn = new Button { Content = ok ? "Reinstalar" : "Instalar", Margin = new Thickness(0) };
            btn.Click += async (_, _) =>
            {
                btn.IsEnabled = false;
                var log = new Progress<string>(s => _log.Text += s + "\n");
                try { await c.Install(log, new Progress<double>(v => _bar.Value = v), CancellationToken.None); }
                catch (Exception ex) { _log.Text += "⚠ " + ex.Message + "\n"; }
                Build();
            };
            DockPanel.SetDock(btn, Dock.Right);
            row.Children.Add(btn);
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = (ok ? "✓ " : "") + c.Name, FontWeight = FontWeights.SemiBold });
            info.Children.Add(new TextBlock { Text = c.Purpose, Style = (Style)Application.Current.FindResource("Sub"), FontSize = 12 });
            row.Children.Add(info);
            _list.Children.Add(row);
        }
    }
}
