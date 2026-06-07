using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GlassPad.Input;

internal sealed class StickZone
{
    internal const double Outer     = 108;
    internal const double Inner     = 52;
    private  const double MaxTravel = (Outer - Inner) / 2; // 28 px

    private readonly TranslateTransform _nubTx = new();
    private int _touchId = -1;

    public string Name { get; }
    public double X    { get; private set; }
    public double Y    { get; private set; }

    public event Action<StickZone>? Changed;

    internal StickZone(string name) => Name = name;

    internal FrameworkElement Build()
    {
        var grid = new Grid
        {
            Width  = Outer,
            Height = Outer,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };

        // 외부 링 (히트테스트 영역 = outer circle 전체)
        grid.Children.Add(new Ellipse
        {
            Fill            = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B8202030")),
            Stroke          = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            StrokeThickness = 1.5,
        });

        // 내부 nub — 터치에 반응하지 않고 시각적 피드백만
        grid.Children.Add(new Ellipse
        {
            Width               = Inner,
            Height              = Inner,
            Fill                = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B8909098")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            IsHitTestVisible    = false,
            RenderTransform     = _nubTx,
        });

        grid.TouchDown        += OnDown;
        grid.TouchMove        += OnMove;
        grid.TouchUp          += OnUp;
        grid.LostTouchCapture += OnLost;

        return grid;
    }

    private void OnDown(object? sender, TouchEventArgs e)
    {
        if (_touchId >= 0) return;
        _touchId = e.TouchDevice.Id;
        e.TouchDevice.Capture((IInputElement)sender!);
        Apply(e.GetTouchPoint((UIElement)sender!).Position);
        e.Handled = true;
    }

    private void OnMove(object? sender, TouchEventArgs e)
    {
        if (e.TouchDevice.Id != _touchId) return;
        Apply(e.GetTouchPoint((UIElement)sender!).Position);
        e.Handled = true;
    }

    private void OnUp(object? sender, TouchEventArgs e)
    {
        if (e.TouchDevice.Id != _touchId) return;
        Reset();
        e.Handled = true;
    }

    private void OnLost(object? sender, TouchEventArgs e)
    {
        if (e.TouchDevice.Id == _touchId) Reset();
    }

    private void Apply(Point pos)
    {
        double dx = pos.X - Outer / 2;
        double dy = pos.Y - Outer / 2;
        double d  = Math.Sqrt(dx * dx + dy * dy);
        if (d > MaxTravel) { dx = dx / d * MaxTravel; dy = dy / d * MaxTravel; }
        _nubTx.X = dx;
        _nubTx.Y = dy;
        X = dx / MaxTravel;
        Y = dy / MaxTravel;
        Changed?.Invoke(this);
    }

    private void Reset()
    {
        _touchId = -1;
        _nubTx.X = 0;
        _nubTx.Y = 0;
        X = 0; Y = 0;
        Changed?.Invoke(this);
    }
}
