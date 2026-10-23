using GlassPad.Input;
using HIDMaestro;

internal static class OfflineChecks
{
    // Load the real SDK's catalog without installing a driver or creating a device.
    internal static int Run()
    {
        using var context = new HMContext();
        int loaded = context.LoadDefaultProfiles();
        if (loaded == 0) throw new Exception("The SDK catalog is empty.");
        Console.WriteLine($"SDK {typeof(HMContext).Assembly.GetName().Version}: loaded {loaded} profiles.");
        foreach (string id in new[] { "xbox-one-s", "xbox-360-wired" })
        {
            var profile = context.GetProfile(id) ?? throw new Exception($"Missing profile: {id}");
            var axes = new GamepadAxes(profile);
            var original = axes.Update(.5f, .5f, .5f, .5f, 0, 0);
            var random = new Random(42);
            for (int i = 0; i < 1000; i++)
            {
                float lx = random.NextSingle(), ly = random.NextSingle();
                float rx = random.NextSingle(), ry = random.NextSingle();
                float lt = random.NextSingle(), rt = random.NextSingle();
                Compare(lx, ly, rx, ry, lt, rt);
            }
            Compare(0, 1, 1, 0, 1, 0);
            Compare(1, 0, 0, 1, 0, 1);
            Compare(.5f, .5f, .5f, .5f, 0, 0); // reset after non-neutral input

            for (int i = 0; i < 10000; i++) axes.Update(.1f, .2f, .3f, .4f, .5f, .6f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100000; i++) axes.Update(.6f, .5f, .4f, .3f, .2f, .1f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated != 0) throw new Exception($"{id}: allocated {allocated} bytes on the hot path.");
            Console.WriteLine($"PASS {id}: 1,003 states match StandardAxes; 100,000 updates allocate 0 bytes.");

            void Compare(float lx, float ly, float rx, float ry, float lt, float rt)
            {
                var expected = HMGamepadStateHelpers.StandardAxes(profile, lx, ly, rx, ry, lt, rt);
                var actual = axes.Update(lx, ly, rx, ry, lt, rt);
                if (!ReferenceEquals(original, actual)) throw new Exception($"{id}: dictionary replaced.");
                if (actual.Count != expected.Count || expected.Any(kv => !actual.TryGetValue(kv.Key, out var value) || value != kv.Value))
                    throw new Exception($"{id}: axis mapping differs from the SDK helper.");
            }
        }
        return 0;
    }
}
