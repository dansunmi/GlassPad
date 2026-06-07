using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GlassPad.Overlay;

/// <summary>
/// 편집 모드: 오버레이 버튼/스틱을 드래그로 이동하고
/// 우하단 주황색 핸들을 드래그해 크기를 조정한다.
/// EditCanvas(상위 Canvas)에서 모든 터치를 가로채므로
/// 기존 XInput 이벤트와 충돌하지 않는다.
/// </summary>
internal sealed class LayoutEditor
{
    private const double HandleSize = 26;

    private readonly Canvas _editCanvas;
    private readonly IReadOnlyList<FrameworkElement> _elements;

    private readonly List<(FrameworkElement El, Border Drag, Ellipse Resize)> _handles = [];

    // 드래그 상태
    private FrameworkElement? _dragEl;
    private int    _dragId      = -1;
    private Point  _dragOrigin;
    private double _leftOrigin, _topOrigin;

    // 리사이즈 상태
    private FrameworkElement? _resizeEl;
    private int    _resizeId    = -1;
    private Point  _resizeOrigin;
    private double _wOrigin, _hOrigin;

    internal bool IsActive { get; private set; }

    internal LayoutEditor(Canvas editCanvas, IReadOnlyList<FrameworkElement> elements)
    {
        _editCanvas = editCanvas;
        _elements   = elements;
    }

    internal void Toggle()
    {
        IsActive = !IsActive;
        if (IsActive) Enter();
        else          ExitInternal();
    }

    internal void Exit()
    {
        if (!IsActive) return;
        IsActive = false;
        ExitInternal();
    }

    // ── Activation ───────────────────────────────────────────────────

    private void Enter()
    {
        // 옅은 파란 틴트 → 편집 모드임을 시각적으로 표시
        _editCanvas.Background       = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x60, 0xC0));
        _editCanvas.IsHitTestVisible = true;

        foreach (var el in _elements)
            CreateHandle(el);

        _editCanvas.TouchDown += OnDown;
        _editCanvas.TouchMove += OnMove;
        _editCanvas.TouchUp   += OnUp;
    }

    private void ExitInternal()
    {
        _editCanvas.TouchDown -= OnDown;
        _editCanvas.TouchMove -= OnMove;
        _editCanvas.TouchUp   -= OnUp;

        _editCanvas.IsHitTestVisible = false;
        _editCanvas.Background       = Brushes.Transparent;
        _editCanvas.Children.Clear();
        _handles.Clear();
        _dragEl = _resizeEl = null;
    }

    // Reset 후 현재 위치 기준으로 오버레이 재생성
    internal void Refresh()
    {
        if (!IsActive) return;
        _editCanvas.Children.Clear();
        _handles.Clear();
        _dragEl = _resizeEl = null;
        foreach (var el in _elements)
            CreateHandle(el);
    }

    // ── Handle creation ──────────────────────────────────────────────

    private void CreateHandle(FrameworkElement el)
    {
        double l = Canvas.GetLeft(el), t = Canvas.GetTop(el);
        double w = el.Width,           h = el.Height;

        // 파란 테두리 — 드래그 영역
        var drag = new Border
        {
            Width           = w,
            Height          = h,
            Background      = new SolidColorBrush(Color.FromArgb(45, 0, 180, 255)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(200, 0, 220, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius    = new CornerRadius(6),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(drag, l);
        Canvas.SetTop(drag, t);
        _editCanvas.Children.Add(drag);

        // 주황색 원 — 우하단 리사이즈 핸들
        var resize = new Ellipse
        {
            Width           = HandleSize,
            Height          = HandleSize,
            Fill            = new SolidColorBrush(Color.FromArgb(220, 255, 130, 0)),
            Stroke          = new SolidColorBrush(Colors.White),
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(resize, l + w - HandleSize / 2);
        Canvas.SetTop(resize,  t + h - HandleSize / 2);
        _editCanvas.Children.Add(resize);

        _handles.Add((el, drag, resize));
    }

    // ── Touch handlers ───────────────────────────────────────────────

    private void OnDown(object? _, TouchEventArgs te)
    {
        var pos = te.GetTouchPoint(_editCanvas).Position;

        // 리사이즈 핸들 우선 확인
        foreach (var (el, _, resize) in _handles)
        {
            double rl = Canvas.GetLeft(resize), rt = Canvas.GetTop(resize);
            if (pos.X >= rl && pos.X <= rl + resize.Width &&
                pos.Y >= rt && pos.Y <= rt + resize.Height)
            {
                _resizeEl     = el;
                _resizeId     = te.TouchDevice.Id;
                _resizeOrigin = pos;
                _wOrigin      = el.Width;
                _hOrigin      = el.Height;
                te.TouchDevice.Capture(_editCanvas);
                te.Handled = true;
                return;
            }
        }

        // 드래그 오버레이 확인
        foreach (var (el, drag, _) in _handles)
        {
            double dl = Canvas.GetLeft(drag), dt = Canvas.GetTop(drag);
            if (pos.X >= dl && pos.X <= dl + drag.Width &&
                pos.Y >= dt && pos.Y <= dt + drag.Height)
            {
                _dragEl     = el;
                _dragId     = te.TouchDevice.Id;
                _dragOrigin = pos;
                _leftOrigin = Canvas.GetLeft(el);
                _topOrigin  = Canvas.GetTop(el);
                te.TouchDevice.Capture(_editCanvas);
                te.Handled = true;
                return;
            }
        }
    }

    private void OnMove(object? _, TouchEventArgs te)
    {
        var pos = te.GetTouchPoint(_editCanvas).Position;

        if (_resizeEl is not null && te.TouchDevice.Id == _resizeId)
        {
            double newW = Math.Max(44, _wOrigin + (pos.X - _resizeOrigin.X));
            double newH = Math.Max(44, _hOrigin + (pos.Y - _resizeOrigin.Y));
            _resizeEl.Width  = newW;
            _resizeEl.Height = newH;
            SyncHandle(_resizeEl);
            te.Handled = true;
        }
        else if (_dragEl is not null && te.TouchDevice.Id == _dragId)
        {
            double newL = _leftOrigin + (pos.X - _dragOrigin.X);
            double newT = _topOrigin  + (pos.Y - _dragOrigin.Y);
            Canvas.SetLeft(_dragEl, newL);
            Canvas.SetTop(_dragEl,  newT);
            SyncHandle(_dragEl);
            te.Handled = true;
        }
    }

    private void OnUp(object? _, TouchEventArgs te)
    {
        if (te.TouchDevice.Id == _resizeId) { _resizeEl = null; _resizeId = -1; }
        if (te.TouchDevice.Id == _dragId)   { _dragEl   = null; _dragId   = -1; }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private void SyncHandle(FrameworkElement el)
    {
        foreach (var (target, drag, resize) in _handles)
        {
            if (target != el) continue;
            double l = Canvas.GetLeft(el), t = Canvas.GetTop(el);
            double w = el.Width,           h = el.Height;

            drag.Width  = w;
            drag.Height = h;
            Canvas.SetLeft(drag, l);
            Canvas.SetTop(drag,  t);

            Canvas.SetLeft(resize, l + w - HandleSize / 2);
            Canvas.SetTop(resize,  t + h - HandleSize / 2);
            break;
        }
    }
}
