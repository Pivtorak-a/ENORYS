namespace Enorys.Core.Runtime;

/// <summary>
/// Stateless keyed generator: adding draws in another agent/subsystem does not
/// affect this agent's results. Deterministic integer hashing, 53-bit float.
/// Not a security-sensitive RNG.
/// </summary>
public static class CounterRandom
{
    public static double Sample(ulong seed, ulong entityId, ulong tick, ulong channel)
    {
        ulong state = unchecked(seed ^ (entityId * 0xD6E8FEB86659FD93UL)
            ^ (tick * 0xA0761D6478BD642FUL)
            ^ (channel * 0xE7037ED1A0B428DBUL));
        state = unchecked(state + 0x9E3779B97F4A7C15UL);
        state = unchecked((state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL);
        state = unchecked((state ^ (state >> 27)) * 0x94D049BB133111EBUL);
        state ^= state >> 31;
        return (state >> 11) * (1.0 / (1UL << 53));
    }
}
