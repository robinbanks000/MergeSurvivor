using System;

namespace MergeSurvivor.Core.Rng
{
    /// <summary>
    /// xorshift32. Chosen over System.Random because System.Random's algorithm is not
    /// contractually stable across runtimes — the same seed can produce different
    /// sequences on different platforms, which would silently break replay and make
    /// simulation results non-comparable between a developer machine and CI.
    /// This implementation is bit-exact everywhere.
    /// </summary>
    public sealed class XorShiftRng : IRng
    {
        // 2^32 / phi. Any non-zero constant works; a zero state would make xorshift
        // degenerate to a fixed point, so seed 0 is remapped rather than rejected.
        private const uint SeedForZero = 0x9E3779B9u;

        private uint _state;

        public XorShiftRng(uint seed)
        {
            _state = seed == 0u ? SeedForZero : seed;
        }

        /// <summary>Current stream position. Persist this to resume a run mid-flight.</summary>
        public uint State => _state;

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public float NextFloat()
        {
            // Use the top 24 bits so the result lands exactly on float's mantissa width.
            // Taking the low bits instead would expose xorshift's weaker low-order bits.
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        public float NextRange(float min, float max)
        {
            if (max < min)
            {
                throw new ArgumentException($"max ({max}) must be >= min ({min}).", nameof(max));
            }

            return min + ((max - min) * NextFloat());
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentException(
                    $"maxExclusive ({maxExclusive}) must be > minInclusive ({minInclusive}).",
                    nameof(maxExclusive));
            }

            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }
    }
}
