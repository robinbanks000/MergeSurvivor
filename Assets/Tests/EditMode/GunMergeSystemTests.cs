using MergeSurvivor.Core.Combat;
using MergeSurvivor.Core.Merge;
using MergeSurvivor.Unity;
using NUnit.Framework;
using UnityEngine;

namespace MergeSurvivor.EditMode.Tests
{
    /// <summary>
    /// The adapter over MergeSystem. The merge rules themselves are covered by
    /// Core.Tests; what is checked here is that the MonoBehaviour delegates to them
    /// rather than reimplementing them, which is how the old public int gunLevel
    /// drifted away from having any rules at all.
    ///
    /// EditMode is enough because GunMergeSystem has no Awake or Update — its state
    /// comes from a field initialiser that runs on AddComponent.
    /// </summary>
    [TestFixture]
    public class GunMergeSystemTests
    {
        private GameObject _host;
        private GunMergeSystem _guns;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("guns");
            _guns = _host.AddComponent<GunMergeSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void StartsAtTheStarterTier()
        {
            Assert.That(_guns.CurrentWeapon, Is.EqualTo(Weapon.Starter));
        }

        [Test]
        public void MergingWithAnIdenticalWeaponRaisesTheTier()
        {
            bool merged = _guns.TryMergeWithHeldDuplicate();

            Assert.That(merged, Is.True);
            Assert.That(_guns.CurrentWeapon.Tier, Is.EqualTo(Weapon.MinTier + 1));
        }

        [Test]
        public void MergingWithADifferentTierIsRefusedAndChangesNothing()
        {
            Weapon before = _guns.CurrentWeapon;

            bool merged = _guns.TryMergeWith(new Weapon(Weapon.MinTier + 3));

            Assert.That(merged, Is.False);
            Assert.That(_guns.CurrentWeapon, Is.EqualTo(before));
        }

        [Test]
        public void MergingStopsAtMaxTier()
        {
            while (_guns.CurrentWeapon.Tier < Weapon.MaxTier)
            {
                Assert.That(_guns.TryMergeWithHeldDuplicate(), Is.True);
            }

            Assert.That(_guns.TryMergeWithHeldDuplicate(), Is.False);
            Assert.That(_guns.CurrentWeapon.Tier, Is.EqualTo(Weapon.MaxTier));
        }

        [Test]
        public void WeaponChangedFiresOnlyOnASuccessfulMerge()
        {
            int fired = 0;
            _guns.WeaponChanged += _ => fired++;

            _guns.TryMergeWith(new Weapon(Weapon.MaxTier));   // refused
            Assert.That(fired, Is.Zero);

            _guns.TryMergeWithHeldDuplicate();                // accepted
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void ReportedStatsTrackTheCurrentTier()
        {
            _guns.TryMergeWithHeldDuplicate();
            Weapon current = _guns.CurrentWeapon;

            Assert.That(_guns.CurrentDamage, Is.EqualTo(WeaponStats.DamageFor(current)).Within(1e-3f));
            Assert.That(_guns.CurrentFireRate, Is.EqualTo(WeaponStats.FireRateFor(current)).Within(1e-3f));
            Assert.That(_guns.CurrentDps, Is.EqualTo(WeaponStats.DpsFor(current)).Within(1e-2f));
        }
    }
}
