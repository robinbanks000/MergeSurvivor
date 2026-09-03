using System;
using MergeSurvivor.Core.Merge;
using NUnit.Framework;

namespace MergeSurvivor.Core.Tests
{
    [TestFixture]
    public class MergeSystemTests
    {
        [Test]
        public void Merge_TwoWeaponsOfSameTier_ProducesNextTier()
        {
            MergeResult result = MergeSystem.Merge(new Weapon(3), new Weapon(3));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Merged.Tier, Is.EqualTo(4));
        }

        [Test]
        public void Merge_DifferentTiers_FailsWithTierMismatch()
        {
            MergeResult result = MergeSystem.Merge(new Weapon(2), new Weapon(5));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure, Is.EqualTo(MergeFailure.TierMismatch));
        }

        [Test]
        public void Merge_AtMaxTier_FailsWithAtMaxTier()
        {
            var maxed = new Weapon(Weapon.MaxTier);

            MergeResult result = MergeSystem.Merge(maxed, maxed);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure, Is.EqualTo(MergeFailure.AtMaxTier));
        }

        [Test]
        public void Merge_NeverProducesATierAboveMax()
        {
            // Walk the whole ladder rather than spot-checking, so a future change to
            // MaxTier cannot quietly open a hole at the top.
            for (int tier = Weapon.MinTier; tier <= Weapon.MaxTier; tier++)
            {
                MergeResult result = MergeSystem.Merge(new Weapon(tier), new Weapon(tier));

                if (result.Success)
                {
                    Assert.That(result.Merged.Tier, Is.LessThanOrEqualTo(Weapon.MaxTier));
                }
            }
        }

        [Test]
        public void CanMerge_AgreesWithMerge()
        {
            Assert.That(MergeSystem.CanMerge(new Weapon(1), new Weapon(1)), Is.True);
            Assert.That(MergeSystem.CanMerge(new Weapon(1), new Weapon(2)), Is.False);
        }

        [Test]
        public void Weapon_RejectsTierOutsideRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Weapon(Weapon.MinTier - 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Weapon(Weapon.MaxTier + 1));
        }

        [Test]
        public void Weapon_EqualityIsByTier()
        {
            Assert.That(new Weapon(4), Is.EqualTo(new Weapon(4)));
            Assert.That(new Weapon(4) == new Weapon(4), Is.True);
            Assert.That(new Weapon(4) != new Weapon(5), Is.True);
        }
    }
}
