namespace MergeSurvivor.Core.Rng
{
    /// <summary>
    /// Every source of randomness in Core goes through this interface so a run can be
    /// replayed exactly from its seed. UnityEngine.Random is banned in Core precisely
    /// because it is global mutable state that cannot be reproduced.
    /// </summary>
    public interface IRng
    {
        /// <summary>Advances the stream and returns the raw 32-bit state.</summary>
        uint NextUInt();

        /// <summary>Uniform float in [0, 1).</summary>
        float NextFloat();

        /// <summary>Uniform float in [min, max).</summary>
        float NextRange(float min, float max);

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        int NextInt(int minInclusive, int maxExclusive);
    }
}
