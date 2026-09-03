using System;

namespace MergeSurvivor.Core.Merge
{
    /// <summary>
    /// A weapon is nothing but its tier. Modelled as a readonly struct so merging in a
    /// hot loop allocates nothing; equality is by value so tests can compare directly.
    /// </summary>
    public readonly struct Weapon : IEquatable<Weapon>
    {
        public const int MinTier = 1;
        public const int MaxTier = 10;

        public Weapon(int tier)
        {
            if (tier < MinTier || tier > MaxTier)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tier), tier, $"Tier must be within [{MinTier}, {MaxTier}].");
            }

            Tier = tier;
        }

        public int Tier { get; }

        public bool IsMaxTier => Tier >= MaxTier;

        public static Weapon Starter => new Weapon(MinTier);

        public bool Equals(Weapon other) => Tier == other.Tier;

        public override bool Equals(object obj) => obj is Weapon other && Equals(other);

        public override int GetHashCode() => Tier;

        public override string ToString() => $"Weapon(T{Tier})";

        public static bool operator ==(Weapon left, Weapon right) => left.Equals(right);

        public static bool operator !=(Weapon left, Weapon right) => !left.Equals(right);
    }
}
