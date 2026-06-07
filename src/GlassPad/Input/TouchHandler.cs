using System.Windows;
using System.Windows.Input;

namespace GlassPad.Input;

/// <summary>
/// WPF UIElement의 Touch 이벤트로 멀티터치를 수신한다.
/// AllowsTransparency 창에서 WM_TOUCH/WM_POINTER WndProc 훅은 IRealTimeStylus에 막힘.
/// UIElement Background는 알파>0이어야 UpdateLayeredWindow hit-test를 통과한다.
/// </summary>
internal sealed class TouchHandler : IDisposable
{
    // X/Y는 WPF 논리 픽셀 (화면 좌상단 기준)
    public readonly record struct PointerPoint(int Id, double X, double Y);

    private readonly UIElement _target;
    private readonly Dictionary<int, PointerPoint> _active = new();

    public event Action<IReadOnlyList<PointerPoint>>? PointersChanged;
    public string InputMode { get; private set; } = "대기 중";

    public TouchHandler(UIElement target)
    {
        _target = target;
        target.TouchDown += OnTouch;
        target.TouchMove += OnTouch;
        target.TouchUp   += OnTouchUp;
    }

    private void OnTouch(object? sender, TouchEventArgs e)
    {
        // null → 스크린 루트 기준 WPF 논리 좌표
        var pt = e.GetTouchPoint(null);
        int id = e.TouchDevice.Id;
        _active[id] = new PointerPoint(id, pt.Position.X, pt.Position.Y);
        InputMode = $"WPF Touch ({_active.Count}pt)";
        PointersChanged?.Invoke([.. _active.Values]);
        e.Handled = true; // 다른 요소로 버블링 차단
    }

    private void OnTouchUp(object? sender, TouchEventArgs e)
    {
        _active.Remove(e.TouchDevice.Id);
        InputMode = $"WPF Touch ({_active.Count}pt)";
        PointersChanged?.Invoke([.. _active.Values]);
        e.Handled = true;
    }

    public void Dispose()
    {
        _target.TouchDown -= OnTouch;
        _target.TouchMove -= OnTouch;
        _target.TouchUp   -= OnTouchUp;
    }
}
