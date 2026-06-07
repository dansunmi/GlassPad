using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GlassPad.Input;
using GlassPad.Overlay;
using HIDMaestro;

namespace GlassPad;

public partial class MainWindow : Window
{
    private GamepadLayout? _layout;
    private PadService?    _pad;
    private LayoutEditor?  _editor;

    private bool _fabOpen    = false;
    private bool _editActive = false;

    private static string LayoutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GlassPad", "layout.json");

    public MainWindow()
    {
        InitializeComponent();
        SizeToScreen();
    }

    private void SizeToScreen()
    {
        Left   = SystemParameters.VirtualScreenLeft;
        Top    = SystemParameters.VirtualScreenTop;
        Width  = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        Win32Helper.ApplyNoActivate(hwnd);

        // 게임패드 오버레이 생성
        _layout = new GamepadLayout(TouchCanvas);
        _layout.Build(Width, Height);

        // 저장된 레이아웃이 있으면 복원
        if (File.Exists(LayoutPath))
        {
            _layout.LoadLayout(LayoutPath, out double savedOpacity, out int savedDpadMode);
            OpacitySlider.Value = savedOpacity;
            foreach (var el in _layout.AllEditable)
                el.Opacity = savedOpacity;
            if (_layout.Dpad is not null)
                _layout.Dpad.Mode = savedDpadMode == 8
                    ? DpadZone.DpadMode.Eight : DpadZone.DpadMode.Four;
        }

        // 버튼 터치 → XInput
        foreach (var (el, tag) in _layout.Buttons)
        {
            el.TouchDown += (_, te) =>
            {
                el.RenderTransform = new ScaleTransform(1.08, 1.08);
                OnButtonDown(el, tag, te);
                te.Handled = true;
            };
            el.TouchUp += (_, te) =>
            {
                el.RenderTransform = Transform.Identity;
                OnButtonUp(tag);
                te.Handled = true;
            };
        }

        // 아날로그 스틱 → XInput
        foreach (var stick in _layout.Sticks)
        {
            stick.Changed += s =>
            {
                if (s.Name == "LS") _pad?.SetLeftStick(s.X, s.Y);
                else                _pad?.SetRightStick(s.X, s.Y);
            };
        }

        // D-Pad → XInput
        if (_layout.Dpad is not null)
            _layout.Dpad.HatChanged += hat => _pad?.SetDpad(hat);

        // 편집 모드 매니저
        _editor = new LayoutEditor(EditCanvas, _layout.AllEditable);

        // DisableButton: 누르는 동안 오버레이 비활성화
        DisableButton.AddHandler(UIElement.TouchDownEvent,
            new EventHandler<TouchEventArgs>((_, __) => SetOverlayEnabled(false)),
            handledEventsToo: true);
        DisableButton.AddHandler(UIElement.TouchUpEvent,
            new EventHandler<TouchEventArgs>((_, __) => SetOverlayEnabled(true)),
            handledEventsToo: true);

        // HIDMaestro 초기화 (background thread)
        _pad = new PadService();
        _pad.Init(_ => { }); // 상태바 없으므로 메시지 무시
    }

    // ── FAB 메뉴 ─────────────────────────────────────────────────────

    private void FabButton_Click(object sender, RoutedEventArgs e)
    {
        _fabOpen = !_fabOpen;
        if (_fabOpen)
        {
            FabButton.Content    = "×";
            FabButton.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xA0, 0x20, 0x20));

            // 편집 모드 버튼: 현재 활성 상태에 따라 색상 설정
            EditModeBtn.Background = _editActive
                ? new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0xCC, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0xCC, 0x40, 0x80, 0xFF));

            AnimateIn(CloseAppBtn, 0);
            AnimateIn(EditModeBtn, 60);
            AnimateIn(InfoBtn,     120);
        }
        else
        {
            CloseFab();
        }
    }

    private void CloseFab()
    {
        _fabOpen = false;
        FabButton.Content    = "≡";
        FabButton.Background = _editActive
            ? new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x70, 0xA8))  // 편집 모드 중 파란 틴트
            : new SolidColorBrush(Color.FromArgb(0xCC, 0x50, 0x50, 0xA0));
        AnimateOut(CloseAppBtn);
        AnimateOut(EditModeBtn);
        AnimateOut(InfoBtn);
    }

    // ── FAB 하위 버튼 ────────────────────────────────────────────────

    private void CloseAppBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void EditModeBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseFab();
        _editActive = !_editActive;
        _editor?.Toggle();
        EditPanel.Visibility = _editActive ? Visibility.Visible : Visibility.Collapsed;
        if (_editActive) SyncDpadModeButtons();
    }

    private void InfoBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseFab();
        InfoPanel.Visibility = Visibility.Visible;
    }

    private void InfoClose_Click(object sender, RoutedEventArgs e)
    {
        InfoPanel.Visibility = Visibility.Collapsed;
    }

    // ── 편집 패널 ────────────────────────────────────────────────────

    private void OpacitySlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_layout is null) return;
        foreach (var el in _layout.AllEditable)
            el.Opacity = e.NewValue;
    }

    private void SaveLayout_Click(object sender, RoutedEventArgs e)
    {
        int dpadMode = _layout?.Dpad?.Mode == DpadZone.DpadMode.Eight ? 8 : 4;
        _layout?.SaveLayout(LayoutPath, OpacitySlider.Value, dpadMode);
        SaveConfirmPanel.Visibility = Visibility.Visible;
    }

    private void SaveConfirmYes_Click(object sender, RoutedEventArgs e)
    {
        SaveConfirmPanel.Visibility = Visibility.Collapsed;
        _editActive = false;
        _editor?.Exit();
        EditPanel.Visibility = Visibility.Collapsed;
    }

    private void SaveConfirmNo_Click(object sender, RoutedEventArgs e)
    {
        SaveConfirmPanel.Visibility = Visibility.Collapsed;
    }

    private void SyncDpadModeButtons()
    {
        bool is8 = _layout?.Dpad?.Mode == DpadZone.DpadMode.Eight;
        Dpad4Btn.Background = is8
            ? new SolidColorBrush(Color.FromArgb(0x80, 0x40, 0x60, 0x90))
            : new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x80, 0x60));
        Dpad8Btn.Background = is8
            ? new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x80, 0x60))
            : new SolidColorBrush(Color.FromArgb(0x80, 0x40, 0x60, 0x90));
    }

    private void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        _layout?.Reset();
        OpacitySlider.Value = 1.0;
        _editor?.Refresh();
    }

    private void DpadModeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_layout?.Dpad is null) return;
        var btn = (Button)sender;
        bool is8 = (string)btn.Tag == "8";
        _layout.Dpad.Mode = is8 ? DpadZone.DpadMode.Eight : DpadZone.DpadMode.Four;

        Dpad4Btn.Background = is8
            ? new SolidColorBrush(Color.FromArgb(0x80, 0x40, 0x60, 0x90))
            : new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x80, 0x60));
        Dpad8Btn.Background = is8
            ? new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x80, 0x60))
            : new SolidColorBrush(Color.FromArgb(0x80, 0x40, 0x60, 0x90));
    }

    // ── 비활성화 버튼 ────────────────────────────────────────────────

    private void SetOverlayEnabled(bool enabled)
    {
        if (_layout is null) return;
        var vis = enabled ? Visibility.Visible : Visibility.Collapsed;
        foreach (var el in _layout.AllEditable)
            el.Visibility = vis;
        if (!enabled)
            _pad?.Reset();
    }

    // ── XInput 입력 ──────────────────────────────────────────────────

    private void OnButtonDown(FrameworkElement el, string tag, TouchEventArgs te)
    {
        switch (tag)
        {
            case "A":  _pad?.PressButton(HMButton.A);           break;
            case "B":  _pad?.PressButton(HMButton.B);           break;
            case "X":  _pad?.PressButton(HMButton.X);           break;
            case "Y":  _pad?.PressButton(HMButton.Y);           break;
            case "LB": _pad?.PressButton(HMButton.LeftBumper);  break;
            case "RB": _pad?.PressButton(HMButton.RightBumper); break;
            case "LT": _pad?.SetLeftTrigger(1.0f);              break;
            case "RT": _pad?.SetRightTrigger(1.0f);             break;
            case "⊟": _pad?.PressButton(HMButton.Back);        break;
            case "⊞": _pad?.PressButton(HMButton.Start);       break;
            case "⊙": _pad?.PressButton(HMButton.Guide);       break;
        }
    }

    private void OnButtonUp(string tag)
    {
        switch (tag)
        {
            case "A":  _pad?.ReleaseButton(HMButton.A);           break;
            case "B":  _pad?.ReleaseButton(HMButton.B);           break;
            case "X":  _pad?.ReleaseButton(HMButton.X);           break;
            case "Y":  _pad?.ReleaseButton(HMButton.Y);           break;
            case "LB": _pad?.ReleaseButton(HMButton.LeftBumper);  break;
            case "RB": _pad?.ReleaseButton(HMButton.RightBumper); break;
            case "LT": _pad?.SetLeftTrigger(0f);                  break;
            case "RT": _pad?.SetRightTrigger(0f);                 break;
            case "⊟": _pad?.ReleaseButton(HMButton.Back);        break;
            case "⊞": _pad?.ReleaseButton(HMButton.Start);       break;
            case "⊙": _pad?.ReleaseButton(HMButton.Guide);       break;
        }
    }

    // ── 애니메이션 ───────────────────────────────────────────────────

    private static void AnimateIn(UIElement el, double delayMs)
    {
        el.Visibility = Visibility.Visible;
        el.Opacity    = 0;

        var scale = new ScaleTransform(0.5, 0.5);
        ((FrameworkElement)el).RenderTransform       = scale;
        ((FrameworkElement)el).RenderTransformOrigin = new Point(0.5, 0.5);

        var delay = TimeSpan.FromMilliseconds(delayMs);
        var dur   = new Duration(TimeSpan.FromMilliseconds(180));
        var ease  = new CubicEase { EasingMode = EasingMode.EaseOut };

        el.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, dur) { BeginTime = delay });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.5, 1, dur) { BeginTime = delay, EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.5, 1, dur) { BeginTime = delay, EasingFunction = ease });
    }

    private static void AnimateOut(UIElement el)
    {
        var anim = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(140)));
        anim.Completed += (_, _) =>
        {
            el.Visibility = Visibility.Collapsed;
            el.BeginAnimation(OpacityProperty, null);
            ((FrameworkElement)el).RenderTransform = Transform.Identity;
        };
        el.BeginAnimation(OpacityProperty, anim);
    }

    // ── Window ───────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        _pad?.Dispose();
        base.OnClosed(e);
    }
}
