using MergeSurvivor.Core.Combat;
using MergeSurvivor.Core.Merge;
using NUnit.Framework;

namespace MergeSurvivor.Core.Tests
{
    [TestFixture]
    public class WeaponStatsTests
    {
        [Test]
        public void TierOneUsesTheBaseValues()
        {
            var starter = Weapon.Starter;

            Assert.That(WeaponStats.DamageFor(starter), Is.EqualTo(WeaponStats.BaseDamage).Within(1e-4f));
            Assert.That(WeaponStats.FireRateFor(starter), Is.EqualTo(WeaponStats.BaseFireRate).Within(1e-4f));
        }

        [Test]
        public void DamageAndFireRateRiseWithEveryTier()
        {
            for (int tier = Weapon.MinTier; tier < Weapon.MaxTier; tier++)
            {
                var lower = new Weapon(tier);
                var higher = new Weapon(tier + 1);

                Assert.That(WeaponStats.DamageFor(higher), Is.GreaterThan(WeaponStats.DamageFor(lower)));
                Assert.That(WeaponStats.FireRateFor(higher), Is.GreaterThan(WeaponStats.FireRateFor(lower)));
            }
        }

        [Test]
        public void DpsIsDamageTimesFireRate()
        {
            var weapon = new Weapon(6);

            float expected = WeaponStats.DamageFor(weapon) * WeaponStats.FireRateFor(weapon);

            Assert.That(WeaponStats.DpsFor(weapon), Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void MergingIsAlwaysWorthMoreThanTheTwoInputsSeparately()
        {
            // The core progression promise of a merge game: combining must beat keeping
            // the pair. If tuning ever breaks this, merging becomes a trap and the whole
            // loop stops making sense.
            for (int tier = Weapon.MinTier; tier < Weapon.MaxTier; tier++)
            {
                var pair = new Weapon(tier);
                MergeResult merged = MergeSystem.Merge(pair, pair);

                Assert.That(merged.Success, Is.True);
                Assert.That(
                    WeaponStats.DpsFor(merged.Merged),
                    Is.GreaterThan(2f * WeaponStats.DpsFor(pair)),
                    $"Merging two tier-{tier} weapons is not worth it.");
            }
        }

        [Test]
        public void StatsAreDeterministicAcrossCalls()
        {
            var weapon = new Weapon(8);

            Assert.That(WeaponStats.DpsFor(weapon), Is.EqualTo(WeaponStats.DpsFor(weapon)));
        }
    }
}
