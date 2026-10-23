using System;
using System.Threading.Tasks;
using HIDMaestro;

namespace GlassPad.Input;

internal sealed class PadService : IDisposable
{
    private HMContext?    _ctx;
    private HMController? _ctrl;
    private GamepadAxes?  _axes;
    private readonly object _stateLock = new();
    private bool _started;
    private bool _disposed;

    private HMButton _buttons = HMButton.None;
    private HMHat    _hat     = HMHat.None;
    private float    _lsX = 0.5f, _lsY = 0.5f;
    private float    _rsX = 0.5f, _rsY = 0.5f;
    private float    _lt  = 0f,   _rt  = 0f;

    internal void Init(Action<string> onStatus)
    {
        lock (_stateLock)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        Task.Run(() =>
        {
            HMContext? context = null;
            HMController? controller = null;
            try
            {
                context = new HMContext();
                context.LoadDefaultProfiles();
                var profile = context.GetProfile("xbox-one-s")
                        ?? context.GetProfile("xbox-360-wired")
                        ?? throw new InvalidOperationException("Xbox 프로파일 없음");
                var axes = new GamepadAxes(profile);

                // 2.x checks the installed version and reclaims devices whose owners exited.
                // Keep recovery for OEM name overrides left by earlier GlassPad builds.
                HMOemNameOverride.RecoverOrphans();
                onStatus("Checking / updating controller driver...");
                context.InstallDriver();

                lock (_stateLock)
                {
                    if (_disposed) return;
                }
                controller = context.CreateController(profile);

                lock (_stateLock)
                {
                    if (_disposed) return;
                    _ctx = context;
                    _ctrl = controller;
                    _axes = axes;
                    context = null;
                    controller = null;
                    Submit();
                }
                onStatus($"✓ {profile.Name}");
            }
            catch (Exception ex) { onStatus($"PadService 오류: {ex.Message}"); }
            finally
            {
                // If the overlay closed during startup, release unpublished resources here.
                try { controller?.Dispose(); }
                finally { context?.Dispose(); }
            }
        });
    }

    internal void PressButton(HMButton btn)   { lock (_stateLock) { _buttons |= btn;  Submit(); } }
    internal void ReleaseButton(HMButton btn) { lock (_stateLock) { _buttons &= ~btn; Submit(); } }

    internal void SetDpad(HMHat hat) { lock (_stateLock) { _hat = hat; Submit(); } }

    internal void SetLeftStick(double x, double y)
    {
        lock (_stateLock)
        {
            _lsX = ToHM(x);
            _lsY = ToHM(y);
            Submit();
        }
    }

    internal void SetRightStick(double x, double y)
    {
        lock (_stateLock)
        {
            _rsX = ToHM(x);
            _rsY = ToHM(y);
            Submit();
        }
    }

    internal void SetLeftTrigger(float v)  { lock (_stateLock) { _lt = v; Submit(); } }
    internal void SetRightTrigger(float v) { lock (_stateLock) { _rt = v; Submit(); } }

    internal void Reset()
    {
        lock (_stateLock)
        {
            _buttons = HMButton.None;
            _hat     = HMHat.None;
            _lsX = _lsY = _rsX = _rsY = 0.5f;
            _lt  = _rt  = 0f;
            Submit();
        }
    }

    private void Submit()
    {
        // All callers hold _stateLock through the synchronous SDK submit: the shared
        // dictionary cannot be changed by startup, input or disposal while it is read.
        if (_disposed || _ctrl is null || _axes is null) return;
        var axes = _axes.Update(_lsX, _lsY, _rsX, _rsY, _lt, _rt);
        _ctrl.SubmitState(new HMGamepadState { Axes = axes, Buttons = _buttons, Hat = _hat });
    }

    private static float ToHM(double v) => (float)((v + 1.0) / 2.0);

    public void Dispose()
    {
        HMContext? context;
        HMController? controller;
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            (context, controller) = (_ctx, _ctrl);
            _ctx = null;
            _ctrl = null;
            _axes = null;
        }
        try { controller?.Dispose(); }
        finally { context?.Dispose(); }
    }
}
