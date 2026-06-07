using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GlassPad.Input;

namespace GlassPad.Overlay;

internal sealed class GamepadLayout
{
    private readonly Canvas _canvas;
    private readonly List<(FrameworkElement Element, string Tag)> _buttons      = [];
    private readonly List<StickZone>                              _sticks        = [];
    private readonly List<FrameworkElement>                       _stickElements = [];

    // 기본 레이아웃 (Reset용) — AllEditable 순서와 동일
    private readonly List<(FrameworkElement El, double L, double T, double W, double H)> _defaults = [];

    internal IReadOnlyList<(FrameworkElement Element, string Tag)> Buttons  => _buttons;
    internal IReadOnlyList<StickZone>        Sticks   => _sticks;
    internal DpadZone?                       Dpad     { get; private set; }

    // 편집 가능한 전체 요소: 버튼 → 스틱 → D-pad 순
    internal IReadOnlyList<FrameworkElement> AllEditable
        => [.._buttons.Select(b => b.Element), .._stickElements];

    internal GamepadLayout(Canvas canvas) => _canvas = canvas;

    internal void Build(double w, double h)
    {
        _canvas.Children.Clear();
        _buttons.Clear();
        _sticks.Clear();
        _stickElements.Clear();
        _defaults.Clear();
        Dpad = null;

        // ── Triggers ─────────────────────────────────────────────
        Pill("LT", 40,       35, 110, 55, "#B3505060");
        Pill("RT", w - 150,  35, 110, 55, "#B3505060");

        // ── Shoulder buttons ─────────────────────────────────────
        Pill("LB", 40,       100, 120, 48, "#B3607080");
        Pill("RB", w - 160,  100, 120, 48, "#B3607080");

        // ── D-Pad ────────────────────────────────────────────────
        DpadAt(175, h * 0.52);

        // ── Left stick ───────────────────────────────────────────
        Stick("LS", 260, h * 0.78);

        // ── A / B / X / Y ────────────────────────────────────────
        double fcx = w - 175, fcy = h * 0.56, fd = 95;
        Circle("Y", "#B3D4AC00", fcx,      fcy - fd, 75);
        Circle("X", "#B31A5FB0", fcx - fd, fcy,      75);
        Circle("B", "#B3C02020", fcx + fd, fcy,      75);
        Circle("A", "#B3108040", fcx,      fcy + fd, 75);

        // ── Right stick ──────────────────────────────────────────
        Stick("RS", w - 300, h * 0.78);

        // ── Center cluster ───────────────────────────────────────
        Circle("⊟", "#B3404055", w / 2 - 115, h * 0.90, 52);
        Circle("⊙", "#B3908020", w / 2,        h * 0.88, 62);
        Circle("⊞", "#B3404055", w / 2 + 115, h * 0.90, 52);

        // 기본 위치 기록 (AllEditable 순서 그대로)
        foreach (var el in AllEditable)
            _defaults.Add((el, Canvas.GetLeft(el), Canvas.GetTop(el), el.Width, el.Height));
    }

    // ── Reset / Save / Load ──────────────────────────────────────────

    internal void Reset()
    {
        foreach (var (el, l, t, w, h) in _defaults)
        {
            Canvas.SetLeft(el, l);
            Canvas.SetTop(el, t);
            el.Width      = w;
            el.Height     = h;
            el.Opacity    = 1.0;
            el.Visibility = Visibility.Visible;
        }
    }

    internal void SaveLayout(string filePath, double opacity, int dpadMode = 4)
    {
        var items = _defaults.Select(d => new
        {
            left   = Canvas.GetLeft(d.El),
            top    = Canvas.GetTop(d.El),
            width  = d.El.Width,
            height = d.El.Height,
        });
        var root = new { opacity, dpadMode, elements = items };
        string? dir = System.IO.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(filePath,
            JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal void LoadLayout(string filePath, out double opacity, out int dpadMode)
    {
        opacity  = 1.0;
        dpadMode = 4;
        if (!File.Exists(filePath)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            var root      = doc.RootElement;
            if (root.TryGetProperty("opacity",  out var op)) opacity  = op.GetDouble();
            if (root.TryGetProperty("dpadMode", out var dm)) dpadMode = dm.GetInt32();
            if (!root.TryGetProperty("elements", out var els)) return;
            int i = 0;
            foreach (var item in els.EnumerateArray())
            {
                if (i >= _defaults.Count) break;
                var el = _defaults[i].El;
                Canvas.SetLeft(el, item.GetProperty("left").GetDouble());
                Canvas.SetTop(el,  item.GetProperty("top").GetDouble());
                el.Width   = item.GetProperty("width").GetDouble();
                el.Height  = item.GetProperty("height").GetDouble();
                el.Opacity = opacity;
                i++;
            }
        }
        catch { /* corrupted file, ignore */ }
    }

    // ── Builder helpers ──────────────────────────────────────────────

    private void Pill(string tag, double x, double y, double bw, double bh, string color)
    {
        var el = new Border
        {
            Tag             = tag,
            Width           = bw,
            Height          = bh,
            Background      = Br(color),
            CornerRadius    = new CornerRadius(bh / 2),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            BorderThickness = new Thickness(1.5),
            Child           = Lbl(tag, bh * 0.38),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        _canvas.Children.Add(el);
        _buttons.Add((el, tag));
    }

    private void Circle(string tag, string color, double cx, double cy, double d)
    {
        var g = new Grid
        {
            Tag    = tag,
            Width  = d,
            Height = d,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        g.Children.Add(new Ellipse
        {
            Fill            = Br(color),
            Stroke          = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            StrokeThickness = 1.5,
        });
        g.Children.Add(Lbl(tag, d * 0.32));
        Canvas.SetLeft(g, cx - d / 2);
        Canvas.SetTop(g,  cy - d / 2);
        _canvas.Children.Add(g);
        _buttons.Add((g, tag));
    }

    private void Stick(string name, double cx, double cy)
    {
        var zone = new StickZone(name);
        _sticks.Add(zone);
        var el = zone.Build();
        Canvas.SetLeft(el, cx - StickZone.Outer / 2);
        Canvas.SetTop(el,  cy - StickZone.Outer / 2);
        _canvas.Children.Add(el);
        _stickElements.Add(el);
    }

    private void DpadAt(double cx, double cy)
    {
        const double size = DpadZone.Base;
        Dpad = new DpadZone();
        var el = Dpad.Build();
        Canvas.SetLeft(el, cx - size / 2);
        Canvas.SetTop(el,  cy - size / 2);
        _canvas.Children.Add(el);
        _stickElements.Add(el);
    }

    private static TextBlock Lbl(string text, double size) => new()
    {
        Text                = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment   = VerticalAlignment.Center,
        Foreground          = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
        FontFamily          = new FontFamily("Segoe UI"),
        FontSize            = size,
        FontWeight          = FontWeights.Bold,
    };

    private static SolidColorBrush Br(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
