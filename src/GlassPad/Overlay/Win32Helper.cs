using System.Runtime.InteropServices;

namespace GlassPad.Overlay;

internal static class Win32Helper
{
    // ── Window style ────────────────────────────────────────────────
    internal const int GWL_EXSTYLE       = -20;
    internal const int WS_EX_LAYERED     = 0x00080000;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_NOACTIVATE  = 0x08000000;
    internal const int WS_EX_TOPMOST     = 0x00000008;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    internal static void ApplyNoActivate(IntPtr hwnd)
    {
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE);
    }

    // ── WM_TOUCH ────────────────────────────────────────────────────
    // WPF는 기본적으로 RegisterTouchWindow를 호출하므로
    // 터치가 WM_POINTER 대신 WM_TOUCH로 전달됨
    internal const int WM_TOUCH = 0x0240;

    internal const int TOUCHEVENTF_DOWN = 0x0002;
    internal const int TOUCHEVENTF_UP   = 0x0004;
    internal const int TOUCHEVENTF_MOVE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOUCHINPUT
    {
        public int    x;           // 화면 픽셀의 1/100 단위
        public int    y;
        public IntPtr hSource;
        public int    dwID;        // 터치 포인트 ID
        public int    dwFlags;
        public int    dwMask;
        public int    dwTime;
        public IntPtr dwExtraInfo;
        public int    cxContact;
        public int    cyContact;
    }

    // WPF가 핸들을 닫으므로 우리는 GetTouchInputInfo만 호출 (CloseTouchInputHandle 호출 안 함)
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetTouchInputInfo(
        IntPtr            hTouchInput,
        int               cInputs,
        [Out] TOUCHINPUT[] pInputs,
        int               cbSize);

}
