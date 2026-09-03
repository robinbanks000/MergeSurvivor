using MergeSurvivor.Core.Merge;

namespace MergeSurvivor.Core.Combat
{
    /// <summary>
    /// Derives combat numbers from a weapon's tier. These constants are the seed values
    /// for the Balance Simulator to tune later; they live in code for now so Phase 0 has
    /// no data-loading dependency, and move to Assets/Data/Tuning once G4 exists.
    /// </summary>
    public static class WeaponStats
    {
        public const float BaseDamage = 10f;
        public const float DamagePerTierMultiplier = 2.0f;

        public const float BaseFireRate = 2f;
        public const float FireRatePerTierMultiplier = 1.10f;

        /// <summary>
        /// The invariant these multipliers exist to satisfy: because DPS is damage times
        /// fire rate, one tier is worth DamagePerTierMultiplier * FireRatePerTierMultiplier
        /// = 2.2x the previous one. Merging two weapons into one therefore yields 110% of
        /// what the pair produced, so combining is a 10% gain rather than a sacrifice.
        /// Drop the product below 2.0 and merging becomes a downgrade — players would
        /// rationally fill the board and never merge, which would break the core loop.
        /// WeaponStatsTests guards this; the Balance Simulator may tune within it.
        /// </summary>
        public const float MergePremium = DamagePerTierMultiplier * FireRatePerTierMultiplier;

        public static float DamageFor(Weapon weapon) =>
            BaseDamage * IntPow(DamagePerTierMultiplier, weapon.Tier - 1);

        /// <summary>Shots per second.</summary>
        public static float FireRateFor(Weapon weapon) =>
            BaseFireRate * IntPow(FireRatePerTierMultiplier, weapon.Tier - 1);

        public static float DpsFor(Weapon weapon) =>
            DamageFor(weapon) * FireRateFor(weapon);

        /// <summary>
        /// Repeated multiplication rather than MathF.Pow. Pow is implemented in native
        /// code and is not guaranteed bit-identical across platforms; balance runs must
        /// produce the same numbers in CI as on the founder's machine, so exponentiation
        /// by a small integer is done the boring, exactly-reproducible way.
        /// </summary>
        private static float IntPow(float value, int exponent)
        {
            float result = 1f;
            for (int i = 0; i < exponent; i++)
            {
                result *= value;
            }

            return result;
        }
    }
}
