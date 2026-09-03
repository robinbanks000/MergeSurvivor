using System;
using MergeSurvivor.Core.Combat;
using MergeSurvivor.Core.Merge;
using UnityEngine;

namespace MergeSurvivor.Unity
{
    /// <summary>
    /// Adapter over <see cref="MergeSystem"/>. The old version incremented a public int
    /// with no upper bound and no notion of what a level was worth; the tier rules and
    /// the stat curve now live in Core where they are tested and simulatable.
    /// </summary>
    public sealed class GunMergeSystem : MonoBehaviour
    {
        /// <summary>Raised whenever the tier changes, so VFX and UI can react.</summary>
        public event Action<Weapon> WeaponChanged;

        public Weapon CurrentWeapon { get; private set; } = Weapon.Starter;

        public float CurrentDamage => WeaponStats.DamageFor(CurrentWeapon);

        public float CurrentFireRate => WeaponStats.FireRateFor(CurrentWeapon);

        public float CurrentDps => WeaponStats.DpsFor(CurrentWeapon);

        /// <summary>
        /// Attempts to merge the held weapon with a picked-up one.
        /// </summary>
        /// <returns>False when the tiers differ or the weapon is already maxed.</returns>
        public bool TryMergeWith(Weapon other)
        {
            MergeResult result = MergeSystem.Merge(CurrentWeapon, other);
            if (!result.Success)
            {
                return false;
            }

            CurrentWeapon = result.Merged;
            WeaponChanged?.Invoke(CurrentWeapon);
            return true;
        }

        /// <summary>Convenience for the common case of merging with an identical weapon.</summary>
        public bool TryMergeWithHeldDuplicate() => TryMergeWith(CurrentWeapon);
    }
}
