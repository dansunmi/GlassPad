using System;
using System.IO;
using System.Threading.Tasks;
using HIDMaestro;

namespace GlassPad.Input;

internal sealed class PadService : IDisposable
{
    private HMContext?    _ctx;
    private HMController? _ctrl;
    private HMProfile?    _profile;

    private HMButton _buttons = HMButton.None;
    private HMHat    _hat     = HMHat.None;
    private float    _lsX = 0.5f, _lsY = 0.5f;
    private float    _rsX = 0.5f, _rsY = 0.5f;
    private float    _lt  = 0f,   _rt  = 0f;

    // 이전 세션 비정상 종료 여부를 추적
    private static string LockPath => Path.Combine(Path.GetTempPath(), "GlassPad.session.lock");

    internal void Init(Action<string> onStatus)
    {
        Task.Run(() =>
        {
            // 락 파일이 이미 있으면 → 이전 세션 크래시 → 고아 장치 정리 필요
            bool crashed = File.Exists(LockPath);
            try { File.WriteAllText(LockPath, Environment.ProcessId.ToString()); } catch { }

            try
            {
                if (crashed)
                    HMOemNameOverride.RecoverOrphans(); // 크래시 후 고아 장치 정리

                _ctx = new HMContext();
                _ctx.LoadDefaultProfiles();
                _profile = _ctx.GetProfile("xbox-one-s")
                        ?? _ctx.GetProfile("xbox-360-wired")
                        ?? throw new InvalidOperationException("Xbox 프로파일 없음");

                // 드라이버가 이미 설치된 경우 바로 연결 (최대 3회 재시도)
                if (!TryCreateController(retries: 3, delayMs: 500))
                {
                    // 최초 실행 또는 드라이버 제거 후 재설치
                    if (!crashed) HMOemNameOverride.RecoverOrphans();
                    onStatus("Installing driver (~18s)...");
                    _ctx.InstallDriver();

                    if (!TryCreateController(retries: 5, delayMs: 800))
                        throw new InvalidOperationException("Controller creation failed");
                }

                Submit();
                onStatus($"✓ {_profile.Name}");
            }
            catch (Exception ex) { onStatus($"PadService 오류: {ex.Message}"); }
        });
    }

    private bool TryCreateController(int retries = 1, int delayMs = 0)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                _ctrl = _ctx!.CreateController(_profile!);
                if (_ctrl is not null) return true;
            }
            catch { _ctrl = null; }

            if (i < retries - 1 && delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);
        }
        return false;
    }

    internal void PressButton(HMButton btn)   { _buttons |= btn;  Submit(); }
    internal void ReleaseButton(HMButton btn) { _buttons &= ~btn; Submit(); }

    internal void SetDpad(HMHat hat) { _hat = hat; Submit(); }

    internal void SetLeftStick(double x, double y)
    {
        _lsX = ToHM(x);
        _lsY = ToHM(y);
        Submit();
    }

    internal void SetRightStick(double x, double y)
    {
        _rsX = ToHM(x);
        _rsY = ToHM(y);
        Submit();
    }

    internal void SetLeftTrigger(float v)  { _lt = v; Submit(); }
    internal void SetRightTrigger(float v) { _rt = v; Submit(); }

    internal void Reset()
    {
        _buttons = HMButton.None;
        _hat     = HMHat.None;
        _lsX = _lsY = _rsX = _rsY = 0.5f;
        _lt  = _rt  = 0f;
        Submit();
    }

    private void Submit()
    {
        if (_ctrl is null || _profile is null) return;
        var axes = HMGamepadStateHelpers.StandardAxes(_profile,
            leftStickX:  _lsX, leftStickY:  _lsY,
            rightStickX: _rsX, rightStickY: _rsY,
            leftTrigger: _lt,  rightTrigger: _rt);
        _ctrl.SubmitState(new HMGamepadState { Axes = axes, Buttons = _buttons, Hat = _hat });
    }

    private static float ToHM(double v) => (float)((v + 1.0) / 2.0);

    public void Dispose()
    {
        _ctrl?.Dispose();
        _ctx?.Dispose();
        try { File.Delete(LockPath); } catch { } // 정상 종료 → 락 파일 삭제
    }
}
