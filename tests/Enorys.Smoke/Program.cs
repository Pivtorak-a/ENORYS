using System.Diagnostics;
using Enorys.Core.Epistemics;
using Enorys.Core.Decision;
using Enorys.Core.Runtime;

int passed = 0, failed = 0;
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
}
void Near(double got, double expected, double tolerance = 1e-7)
{
    if (!double.IsFinite(got) || Math.Abs(got - expected) > tolerance)
        throw new Exception($"Expected {expected:R}, actual {got:R}");
}
void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

Check("Noisy-OR known 2-cause reference", () =>
{
    Cause[] c = [new(.3, .9), new(.2, .9)];
    var p = NoisyOr.Solve(c, .05);
    Near(p.Causes[0], .64134190);
    Near(p.Causes[1], .43152575);
    Near(p.NoListedCause, .06491549);
    Near(p.LeakActivated, .11592052);
});
Check("Noisy-OR explaining away from LR 3", () =>
{
    Cause[] c = [new(.3, .9), new(.2, .9, 3)];
    var p = NoisyOr.Solve(c, .05);
    Near(p.Causes[0], .49215396);
    Near(p.Causes[1], .69486927);
});
Check("Noisy-OR 8-cause bounded outputs", () =>
{
    Cause[] c = Enumerable.Range(0, 8).Select(i => new Cause(.1 + i * .06, .1 + i * .1, 0.5 + i)).ToArray();
    var p = NoisyOr.Solve(c, .03);
    Require(p.Causes.All(x => double.IsFinite(x) && x >= 0 && x <= 1), "Invalid posterior");
    Require(p.NoListedCause is >= 0 and <= 1, "Invalid unlisted mass");
    Require(p.LeakActivated is >= 0 and <= 1, "Invalid leak mass");
});
Check("Noisy-OR property tests on 5000 seeded pairs", () =>
{
    var r = new Random(41);
    for (int i = 0; i < 5000; i++)
    {
        double p1 = .01 + r.NextDouble() * .98, p2 = .01 + r.NextDouble() * .98;
        double q1 = .01 + r.NextDouble() * .98, q2 = .01 + r.NextDouble() * .98;
        double leak = .01 + r.NextDouble() * .18;
        var old = NoisyOr.Solve([new(p1,q1),new(p2,q2)],leak);
        var newer = NoisyOr.Solve([new(p1,q1),new(p2,q2,3)],leak);
        Require(old.Causes.All(x=>x>=0 && x<=1) && newer.Causes.All(x=>x>=0 && x<=1), "Posterior out of range");
        Require(newer.Causes[1] >= old.Causes[1] - 1e-12, "LR did not favor target");
        Require(newer.Causes[0] <= old.Causes[0] + 1e-12, "Explaining away failed");
    }
});
Check("Evidence root deduplication", () =>
{
    var ledger = new EvidenceLedger();
    Require(ledger.TryAdd(new(1, 100, 22, Math.Log(3))), "Missing original");
    Require(!ledger.TryAdd(new(2, 100, 22, Math.Log(3))), "Rumor loop duplicated claim");
    Require(ledger.TryAdd(new(3, 101, 22, Math.Log(3))), "Distinct original should count");
    Require(ledger.IndependentClaimCount == 2, "Unexpected independent evidence count");
    Require(ledger.TryAdd(new(4, 100, 23, Math.Log(2))), "Different claim not recorded");
    Require(ledger.IndependentClaimCount == 3, "New content missing");
});
Check("Noisy-OR invalid inputs and impossible conditioning", () =>
{
    bool invalid = false, impossible = false;
    try { NoisyOr.Solve([new(.5, 1.5)], .1); } catch (ArgumentOutOfRangeException) { invalid = true; }
    try { NoisyOr.Solve([new(.5, 0)], 0); } catch (InvalidOperationException) { impossible = true; }
    Require(invalid && impossible, "Expected validation failures");
});
Check("Softmax stable for extreme utilities", () =>
{
    var p = Choice.Probabilities([-10000, 10000], 1);
    Near(p[0], 0); Near(p[1], 1);
    var equal = Choice.Probabilities([4,4,4], .01);
    foreach (double x in equal) Near(x, 1d / 3);
    var frozen = Choice.Probabilities([3, 3, 1], 0);
    Near(frozen[0], 1); Near(frozen[1], 0);
});
Check("Softmax rejects NaN and negative temperature", () =>
{
    bool invalid = false;
    try { Choice.Probabilities([double.NaN, 1], 1); } catch(ArgumentException) { invalid = true; }
    Require(invalid, "NaN accepted");
    invalid = false;
    try { Choice.Probabilities([0,1], -1); } catch(ArgumentOutOfRangeException) { invalid = true; }
    Require(invalid, "Negative temperature accepted");
});
Check("Keyed RNG replay and order independence", () =>
{
    double reference = CounterRandom.Sample(42, 7, 10, 3);
    var other = CounterRandom.Sample(42, 8, 10, 3);
    Near(reference, CounterRandom.Sample(42, 7, 10, 3), 0);
    Require(reference >= 0 && reference < 1 && other >= 0 && other < 1, "Outside [0,1)");
    Require(reference != other, "Key collision in sample");
});
Check("8-cause enumeration short indicative timing", () =>
{
    var causes = Enumerable.Range(0, 8).Select(i => new Cause(.1 + i * .06, .1 + i * .09)).ToArray();
    var sw = Stopwatch.StartNew();
    double checksum = 0;
    for (int i = 0; i < 2000; i++)
        checksum += NoisyOr.Solve(causes, .05).Causes[0];
    sw.Stop();
    Require(double.IsFinite(checksum), "Nonfinite output");
    Console.WriteLine($"INFO 2,000 × 8-cause exact solves: {sw.Elapsed.TotalMilliseconds:F1} ms (runner only; not a real-game benchmark)");
});
Console.WriteLine($"TOTAL {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
