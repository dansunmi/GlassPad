using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using GlassPad.Input;
using HIDMaestro;

internal static class LiveChecks
{
    private static int _checks;

    internal static int Run(string[] args)
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Run --live from an elevated terminal.");
        Console.WriteLine($"Live XInput test started {DateTimeOffset.Now:O}; PID {Environment.ProcessId}; elevated=True");
        Console.WriteLine($"SDK {typeof(HMContext).Assembly.GetName().Version}");
        var baseline = ConnectedSlots();
        Console.WriteLine($"Existing XInput slots: {string.Join(",", baseline)}");
        if (baseline.Count == 4) throw new InvalidOperationException("All four XInput slots are occupied.");

        // This is the application's actual PadService source, including startup,
        // screen-coordinate conversion, shared axes, reset and disposal.
        uint slot;
        using (var pad = StartPadService())
        {
            slot = WaitForNewSlot(baseline);
            Console.WriteLine($"PadService controller connected in XInput slot {slot}.");
            Exercise(pad, slot);
        }
        WaitForDisconnect(slot);

        using (var pad = StartPadService())
        {
            slot = WaitForNewSlot(baseline);
            pad.PressButton(HMButton.A);
            Expect(slot, new() { Buttons = 0x1000 }, "restart: A pressed");
            pad.Reset();
            Expect(slot, new(), "restart: reset");
        }
        WaitForDisconnect(slot);

        // Also exercise the fallback profile against the real driver.
        using (var context = new HMContext())
        {
            context.LoadDefaultProfiles();
            var profile = context.GetProfile("xbox-360-wired") ?? throw new Exception("Fallback profile missing.");
            var axes = new GamepadAxes(profile);
            using (var controller = context.CreateController(profile))
            {
                Console.WriteLine($"Fallback driver: {controller.DriverVersion}");
                slot = WaitForNewSlot(baseline);
                controller.SubmitState(new HMGamepadState {
                    Axes = axes.Update(1, 0, 0, 1, .25f, .75f),
                    Buttons = HMButton.A | HMButton.RightStick, Hat = HMHat.NorthEast });
                Expect(slot, new() { Buttons = 0x1089, LX = 32767, LY = 32767,
                    RX = -32768, RY = -32768, LT = 64, RT = 191 }, "fallback: simultaneous buttons, hat, sticks, triggers");
                controller.SubmitState(new HMGamepadState { Axes = axes.Update(.5f, .5f, .5f, .5f, 0, 0) });
                Expect(slot, new(), "fallback: reset");
            }
        }
        WaitForDisconnect(slot);
        Console.WriteLine($"PASS: {_checks} live XInput checks, including disconnect and reconnect.");

        int appIndex = Array.IndexOf(args, "--app");
        if (appIndex >= 0 && appIndex + 1 < args.Length)
        {
            var process = Process.Start(new ProcessStartInfo(args[appIndex + 1]) {
                UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(args[appIndex + 1])! })
                ?? throw new Exception("GlassPad could not be started.");
            Console.WriteLine($"Published GlassPad started; PID {process.Id}.");
            slot = WaitForNewSlot(baseline, 60000);
            Expect(slot, new(), "published GlassPad: controller connected and neutral");
            Console.WriteLine($"APP_READY: PID {process.Id}; XInput slot {slot}. The app remains open for UI checks.");
        }
        return 0;
    }

    private static PadService StartPadService()
    {
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pad = new PadService();
        pad.Init(status => {
            Console.WriteLine($"PadService: {status}");
            if (status.StartsWith("✓")) ready.TrySetResult(status);
            else if (status.StartsWith("PadService 오류:")) ready.TrySetException(new Exception(status));
        });
        try { ready.Task.WaitAsync(TimeSpan.FromMinutes(3)).GetAwaiter().GetResult(); return pad; }
        catch { pad.Dispose(); throw; }
    }

    private static void Exercise(PadService pad, uint slot)
    {
        pad.Reset();
        Expect(slot, new(), "initial neutral");
        foreach (var (button, mask) in new (HMButton, ushort)[] {
            (HMButton.A, 0x1000), (HMButton.B, 0x2000), (HMButton.X, 0x4000), (HMButton.Y, 0x8000),
            (HMButton.LeftBumper, 0x0100), (HMButton.RightBumper, 0x0200),
            (HMButton.Back, 0x0020), (HMButton.Start, 0x0010),
            (HMButton.LeftStick, 0x0040), (HMButton.RightStick, 0x0080) })
        {
            pad.PressButton(button);
            Expect(slot, new() { Buttons = mask }, $"{button} down");
            pad.ReleaseButton(button);
            Expect(slot, new(), $"{button} up");
        }
        foreach (var (hat, mask) in new (HMHat, ushort)[] {
            (HMHat.North, 1), (HMHat.NorthEast, 9), (HMHat.East, 8), (HMHat.SouthEast, 10),
            (HMHat.South, 2), (HMHat.SouthWest, 6), (HMHat.West, 4), (HMHat.NorthWest, 5), (HMHat.None, 0) })
        {
            pad.SetDpad(hat);
            Expect(slot, new() { Buttons = mask }, $"D-pad {hat}");
        }
        foreach (bool left in new[] { true, false })
        {
            foreach (var (x, y, expectedX, expectedY, direction) in new[] {
                (-1d, 0d, (short)-32768, (short)0, "left"), (1d, 0d, (short)32767, (short)0, "right"),
                (0d, -1d, (short)0, (short)32767, "up"), (0d, 1d, (short)0, (short)-32768, "down"),
                (.25d, -.5d, (short)8192, (short)16384, "partial"), (0d, 0d, (short)0, (short)0, "center") })
            {
                if (left) pad.SetLeftStick(x, y); else pad.SetRightStick(x, y);
                var expected = new Gamepad();
                if (left) { expected.LX = expectedX; expected.LY = expectedY; }
                else { expected.RX = expectedX; expected.RY = expectedY; }
                Expect(slot, expected, $"{(left ? "left" : "right")} stick {direction}");
            }
        }
        foreach (float value in new[] { 0f, .25f, .5f, 1f, 0f })
        {
            pad.SetLeftTrigger(value);
            Expect(slot, new() { LT = (byte)Math.Round(value * 255) }, $"left trigger {value}");
        }
        foreach (float value in new[] { 0f, .25f, .5f, 1f, 0f })
        {
            pad.SetRightTrigger(value);
            Expect(slot, new() { RT = (byte)Math.Round(value * 255) }, $"right trigger {value}");
        }
        pad.PressButton(HMButton.A | HMButton.B | HMButton.LeftBumper | HMButton.RightStick);
        pad.SetDpad(HMHat.SouthWest);
        pad.SetLeftStick(1, -1);
        pad.SetRightStick(-1, 1);
        pad.SetLeftTrigger(.25f);
        pad.SetRightTrigger(.75f);
        Expect(slot, new() { Buttons = 0x3186, LX = 32767, LY = 32767, RX = -32768,
            RY = -32768, LT = 64, RT = 191 }, "all controls combined");
        pad.Reset();
        Expect(slot, new(), "reset clears all controls");
        for (int i = 0; i < 100; i++)
        {
            pad.PressButton(HMButton.A);
            Expect(slot, new() { Buttons = 0x1000 }, $"repeat {i + 1} down", false);
            pad.ReleaseButton(HMButton.A);
            Expect(slot, new(), $"repeat {i + 1} up", false);
        }
        Console.WriteLine("PASS 100 A-button press/release cycles read back through XInput.");
        Console.WriteLine("Guide is not tested: it is outside documented XInputGetState button bits.");
    }

    private static HashSet<uint> ConnectedSlots() => Enumerable.Range(0, 4).Select(i => (uint)i)
        .Where(i => XInputGetState(i, out _) == 0).ToHashSet();

    private static uint WaitForNewSlot(HashSet<uint> baseline, int timeoutMs = 15000)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            var added = ConnectedSlots().Except(baseline).ToArray();
            if (added.Length == 1) return added[0];
            if (added.Length > 1) throw new Exception("Multiple new XInput devices; cannot identify test controller.");
            Thread.Sleep(50);
        }
        throw new TimeoutException("The created controller never appeared in XInput.");
    }

    private static void WaitForDisconnect(uint slot)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 15000)
        {
            if (XInputGetState(slot, out _) == 1167) { _checks++; Console.WriteLine($"PASS slot {slot} disconnected after disposal."); return; }
            Thread.Sleep(50);
        }
        throw new TimeoutException($"XInput slot {slot} stayed connected after disposal.");
    }

    private static void Expect(uint slot, Gamepad expected, string label, bool log = true)
    {
        var timer = Stopwatch.StartNew();
        State actual = default;
        uint error = 0;
        while (timer.ElapsedMilliseconds < 2000)
        {
            error = XInputGetState(slot, out actual);
            if (error == 0 && Matches(actual.Pad, expected))
            {
                _checks++;
                if (log) Console.WriteLine($"PASS {label}: {actual.Pad}");
                return;
            }
            Thread.Sleep(5);
        }
        throw new Exception($"{label}: expected {expected}; got {actual.Pad}; XInput error {error}.");
    }

    private static bool Matches(Gamepad actual, Gamepad expected) =>
        (actual.Buttons & 0xF3FF) == expected.Buttons &&
        Math.Abs(actual.LT - expected.LT) <= 2 && Math.Abs(actual.RT - expected.RT) <= 2 &&
        Math.Abs((int)actual.LX - expected.LX) <= 512 && Math.Abs((int)actual.LY - expected.LY) <= 512 &&
        Math.Abs((int)actual.RX - expected.RX) <= 512 && Math.Abs((int)actual.RY - expected.RY) <= 512;

    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetState(uint index, out State state);

    [StructLayout(LayoutKind.Sequential)]
    private struct State { public uint Packet; public Gamepad Pad; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Gamepad
    {
        public ushort Buttons;
        public byte LT, RT;
        public short LX, LY, RX, RY;
        public override readonly string ToString() => $"buttons=0x{Buttons:X4} LT={LT} RT={RT} L=({LX},{LY}) R=({RX},{RY})";
    }
}
