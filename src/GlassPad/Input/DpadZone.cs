using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using HIDMaestro;

namespace GlassPad.Input;

internal sealed class DpadZone
{
    internal enum DpadMode { Four, Eight }

    internal const double Base    = 180;
    private  const double BaseArm =  60;   // arm width

    private int _touchId = -1;

    private readonly Dictionary<string, Rectangle> _armHighlights = [];
    private Ellipse? _modeRing;

    private DpadMode _mode = DpadMode.Four;
    internal DpadMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            if (_modeRing is not null)
                _modeRing.Visibility = value == DpadMode.Eight
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    internal event Action<HMHat>? HatChanged;

    // ── Build ──────────────────────────────────────────────────────────

    internal FrameworkElement Build()
    {
        double c = (Base - BaseArm) / 2; // = 60

        var inner = new Canvas
        {
            Width      = Base,
            Height     = Base,
            Background = Brushes.Transparent,
        };

        // 8-way 모드 배경 원 (십자 뒤에, 기본 숨김)
        // 십자 arm 끝 꼭짓점까지 반지름 ~95px → 지름 200으로 완전히 감싸도록
        const double RingSize = 200;
        const double RingOff  = (Base - RingSize) / 2; // -10
        _modeRing = new Ellipse
        {
            Width            = RingSize,
            Height           = RingSize,
            Fill             = new SolidColorBrush(Color.FromArgb(60, 0, 180, 255)),
            Stroke           = new SolidColorBrush(Color.FromArgb(200, 0, 220, 255)),
            StrokeThickness  = 2.5,
            Visibility       = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_modeRing, RingOff);
        Canvas.SetTop(_modeRing,  RingOff);
        inner.Children.Add(_modeRing);

        // 십자 베이스 (배경 + 테두리)
        inner.Children.Add(new Path
        {
            Data            = CrossGeom(Base, BaseArm),
            Fill            = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8404055")),
            Stroke          = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            StrokeThickness = 1.5,
        });

        // 방향 하이라이트 (각 arm 영역, 초기 숨김)
        AddArmHighlight(inner, "N", c,         0,         BaseArm, c);
        AddArmHighlight(inner, "S", c,         c+BaseArm, BaseArm, c);
        AddArmHighlight(inner, "W", 0,         c,         c,       BaseArm);
        AddArmHighlight(inner, "E", c+BaseArm, c,         c,       BaseArm);

        // 방향 화살표 레이블
        inner.Children.Add(Arrow("▲", Base / 2 - 7,     c / 2 - 9));
        inner.Children.Add(Arrow("▼", Base / 2 - 7,     Base - c / 2 - 9));
        inner.Children.Add(Arrow("◀", c / 2 - 9,        Base / 2 - 9));
        inner.Children.Add(Arrow("▶", Base - c / 2 - 9, Base / 2 - 9));

        inner.TouchDown        += OnDown;
        inner.TouchMove        += OnMove;
        inner.TouchUp          += OnUp;
        inner.LostTouchCapture += OnLost;

        // Viewbox: 편집 모드 리사이즈 시 비율 유지 스케일
        return new Viewbox { Width = Base, Height = Base, Child = inner };
    }

    private void AddArmHighlight(Canvas c, string key,
        double l, double t, double w, double h)
    {
        var r = new Rectangle
        {
            Width  = w,
            Height = h,
            Fill   = new SolidColorBrush(Color.FromArgb(100, 0, 220, 255)),
            Opacity = 0,
            IsHitTestVisible = false,
            RadiusX = 4,
            RadiusY = 4,
        };
        Canvas.SetLeft(r, l);
        Canvas.SetTop(r,  t);
        c.Children.Add(r);
        _armHighlights[key] = r;
    }

    // ── Touch ─────────────────────────────────────────────────────────

    private void OnDown(object? s, TouchEventArgs te)
    {
        if (_touchId >= 0) return;
        _touchId = te.TouchDevice.Id;
        te.TouchDevice.Capture((IInputElement)s!);
        UpdateHat(te.GetTouchPoint((UIElement)s!).Position);
        te.Handled = true;
    }

    private void OnMove(object? s, TouchEventArgs te)
    {
        if (te.TouchDevice.Id != _touchId) return;
        UpdateHat(te.GetTouchPoint((UIElement)s!).Position);
        te.Handled = true;
    }

    private void OnUp(object? s, TouchEventArgs te)
    {
        if (te.TouchDevice.Id != _touchId) return;
        _touchId = -1;
        ClearHighlights();
        HatChanged?.Invoke(HMHat.None);
        te.Handled = true;
    }

    private void OnLost(object? s, TouchEventArgs te)
    {
        if (te.TouchDevice.Id != _touchId) return;
        _touchId = -1;
        ClearHighlights();
        HatChanged?.Invoke(HMHat.None);
    }

    private void UpdateHat(Point pos)
    {
        double dx = pos.X - Base / 2;
        double dy = pos.Y - Base / 2;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < 12)
        {
            ClearHighlights();
            HatChanged?.Invoke(HMHat.None);
            return;
        }

        double angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
        var hat = _mode == DpadMode.Eight ? ToHat8(angle) : ToHat4(angle);

        UpdateHighlight(hat);
        HatChanged?.Invoke(hat);
    }

    // ── Highlight ─────────────────────────────────────────────────────

    private void UpdateHighlight(HMHat hat)
    {
        ClearHighlights();
        switch (hat)
        {
            case HMHat.North:     LitArm("N");               break;
            case HMHat.South:     LitArm("S");               break;
            case HMHat.East:      LitArm("E");               break;
            case HMHat.West:      LitArm("W");               break;
            case HMHat.NorthEast: LitArm("N", 0.7); LitArm("E", 0.7); break;
            case HMHat.SouthEast: LitArm("S", 0.7); LitArm("E", 0.7); break;
            case HMHat.SouthWest: LitArm("S", 0.7); LitArm("W", 0.7); break;
            case HMHat.NorthWest: LitArm("N", 0.7); LitArm("W", 0.7); break;
        }
    }

    private void LitArm(string key, double opacity = 1.0)
    {
        if (!_armHighlights.TryGetValue(key, out var r)) return;
        r.BeginAnimation(UIElement.OpacityProperty, null);
        r.Opacity = opacity;
    }

    private void ClearHighlights()
    {
        var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120)));
        foreach (var r in _armHighlights.Values)
            r.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // ── Direction mapping ─────────────────────────────────────────────

    private static HMHat ToHat4(double a)
    {
        if (a > -135 && a <= -45) return HMHat.North;
        if (a > -45  && a <=  45) return HMHat.East;
        if (a >  45  && a <= 135) return HMHat.South;
        return HMHat.West;
    }

    private static HMHat ToHat8(double a)
    {
        double norm = ((a % 360) + 360) % 360;
        int s = (int)((norm + 22.5) / 45) % 8;
        return s switch
        {
            0 => HMHat.East,
            1 => HMHat.SouthEast,
            2 => HMHat.South,
            3 => HMHat.SouthWest,
            4 => HMHat.West,
            5 => HMHat.NorthWest,
            6 => HMHat.North,
            7 => HMHat.NorthEast,
            _ => HMHat.None,
        };
    }

    // ── Geometry / UI helpers ─────────────────────────────────────────

    private static PathGeometry CrossGeom(double outer, double arm)
    {
        double c = (outer - arm) / 2;
        var fig = new PathFigure { IsClosed = true, StartPoint = new Point(c, 0) };
        fig.Segments.Add(new LineSegment(new Point(c + arm, 0),     true));
        fig.Segments.Add(new LineSegment(new Point(c + arm, c),     true));
        fig.Segments.Add(new LineSegment(new Point(outer,   c),     true));
        fig.Segments.Add(new LineSegment(new Point(outer,   c+arm), true));
        fig.Segments.Add(new LineSegment(new Point(c + arm, c+arm), true));
        fig.Segments.Add(new LineSegment(new Point(c + arm, outer), true));
        fig.Segments.Add(new LineSegment(new Point(c,       outer), true));
        fig.Segments.Add(new LineSegment(new Point(c,       c+arm), true));
        fig.Segments.Add(new LineSegment(new Point(0,       c+arm), true));
        fig.Segments.Add(new LineSegment(new Point(0,       c),     true));
        fig.Segments.Add(new LineSegment(new Point(c,       c),     true));
        var geom = new PathGeometry();
        geom.Figures.Add(fig);
        return geom;
    }

    private static TextBlock Arrow(string sym, double left, double top)
    {
        var tb = new TextBlock
        {
            Text             = sym,
            Foreground       = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            FontSize         = 16,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb,  top);
        return tb;
    }
}
