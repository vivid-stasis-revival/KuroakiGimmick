using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

internal static class HomPreviewChecks
{
    internal static int Run()
    {
        Check("cold seek", 60000, -1, false, 59996, false, 4);
        Check("forward scrub", 60000, 100, true, 59996, false, 4);
        Check("backward scrub", 60000, 70000, true, 59996, false, 4);
        Check("normal playback", 60000, 59999, true, 59999, true, 1);
        Check("duplicate target", 60000, 60000, true, 60000, true, 0);
        Check("initial tick", 0, -1, false, -1, false, 1);
        Check("zero-HOM destination", 60000, 100, true, 59999, false, 1, 60000);
        Check("zero-HOM in tail", 60000, 100, true, 59997, false, 3, 59998);
        Console.WriteLine("PASS HOM preview: bounded seek/rewind, sequential history, and zero-HOM reset.");
        return 0;
    }

    static void Check(string name, long target, long previous, bool valid, long expected, bool keep,
        int lookups, long zeroAt = -1)
    {
        int calls = 0;
        var plan = SceneRenderer.PlanInteractiveFeedback(target, previous, valid,
            tick =>
{
    calls++;
    return tick == zeroAt ? 0 : .98;
});
        if (plan != (expected, keep) || calls != lookups || target - plan.PreviousTick > 4)
            throw new InvalidOperationException($"HOM preview: unexpected replay plan for {name}.");
    }
}
