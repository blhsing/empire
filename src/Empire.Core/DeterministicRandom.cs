namespace Empire.Core;

/// <summary>Mulberry32-compatible generator used by the browser game.</summary>
public sealed class DeterministicRandom
{
    public DeterministicRandom(int seed) => Seed = seed;

    public int Seed { get; private set; }

    public double NextDouble()
    {
        Seed = unchecked(Seed + 0x6D2B79F5);
        var value = Seed;
        value = unchecked((value ^ (int)((uint)value >> 15)) * (value | 1));
        value ^= unchecked(value + ((value ^ (int)((uint)value >> 7)) * (value | 61)));
        return (uint)(value ^ (int)((uint)value >> 14)) / 4294967296d;
    }

    public int Next(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        return (int)(NextDouble() * exclusiveMax);
    }
}
