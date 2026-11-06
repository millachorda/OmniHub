using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace OmniHub.Services;

/// <summary>
/// "Ahora suena" universal: Spotify, YouTube/navegador, Apple Music, VLC… cualquier app
/// que publique en los controles multimedia de Windows.
/// </summary>
public sealed class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string App { get; private set; } = "";
    public bool Playing { get; private set; }
    public BitmapImage? Cover { get; private set; }
    public (byte r, byte g, byte b)? CoverColor { get; private set; }
    public event Action? Changed;
    string _lastKey = "";

    public async Task InitAsync()
    {
        try { _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch { }
    }

    public async Task PollAsync()
    {
        if (_mgr == null) return;
        try
        {
            var s = _mgr.GetCurrentSession();
            if (s == null) { if (Title != "") { Title = Artist = App = ""; Cover = null; CoverColor = null; Playing = false; Changed?.Invoke(); } return; }
            var info = await s.TryGetMediaPropertiesAsync();
            var pb = s.GetPlaybackInfo();
            var playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var key = info.Title + "|" + info.Artist;
            bool changed = key != _lastKey || playing != Playing;
            Title = info.Title; Artist = info.Artist; Playing = playing;
            App = PrettyApp(s.SourceAppUserModelId);
            if (key != _lastKey)
            {
                _lastKey = key;
                Cover = null; CoverColor = null;
                if (info.Thumbnail != null)
                {
                    using var ras = await info.Thumbnail.OpenReadAsync();
                    using var st = ras.AsStreamForRead();
                    var ms = new MemoryStream();
                    await st.CopyToAsync(ms);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
                    Cover = bmp;
                    CoverColor = Dominant(bmp);
                }
            }
            if (changed) Changed?.Invoke();
        }
        catch { }
    }

    static string PrettyApp(string id)
    {
        var l = id.ToLowerInvariant();
        if (l.Contains("spotify")) return "Spotify";
        if (l.Contains("chrome")) return "Chrome";
        if (l.Contains("msedge")) return "Edge";
        if (l.Contains("firefox")) return "Firefox";
        if (l.Contains("opera")) return "Opera";
        if (l.Contains("vlc")) return "VLC";
        if (l.Contains("applemusic") || l.Contains("itunes")) return "Apple Music";
        if (l.Contains("zune") || l.Contains("media")) return "Reproductor multimedia";
        return Path.GetFileNameWithoutExtension(id);
    }

    /// <summary>Color dominante "vivo" de la carátula (para las luces).</summary>
    static (byte, byte, byte)? Dominant(BitmapSource src)
    {
        try
        {
            var small = new TransformedBitmap(new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0),
                new System.Windows.Media.ScaleTransform(24.0 / src.PixelWidth, 24.0 / src.PixelHeight));
            int w = small.PixelWidth, h = small.PixelHeight;
            var px = new byte[w * h * 4];
            small.CopyPixels(px, w * 4, 0);
            double br = 0, bg = 0, bb = 0, wsum = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i], g = px[i + 1], r = px[i + 2];
                double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                double sat = mx == 0 ? 0 : (mx - mn) / mx;
                double wgt = sat * sat * (mx / 255.0) + 0.001; // priorizar colores saturados y brillantes
                br += r * wgt; bg += g * wgt; bb += b * wgt; wsum += wgt;
            }
            double R = br / wsum, G = bg / wsum, B = bb / wsum;
            double m = Math.Max(R, Math.Max(G, B));
            if (m < 1) return (255, 255, 255);
            double k = 255 / m; // máximo brillo para LEDs
            return ((byte)(R * k), (byte)(G * k), (byte)(B * k));
        }
        catch { return null; }
    }

    public async Task PlayPause() { try { await _mgr?.GetCurrentSession()?.TryTogglePlayPauseAsync()!; } catch { } }
    public async Task Next() { try { await _mgr?.GetCurrentSession()?.TrySkipNextAsync()!; } catch { } }
    public async Task Prev() { try { await _mgr?.GetCurrentSession()?.TrySkipPreviousAsync()!; } catch { } }
}
