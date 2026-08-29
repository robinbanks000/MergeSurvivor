using System;
using MergeSurvivor.Core.Player;
using NUnit.Framework;

namespace MergeSurvivor.Core.Tests
{
    [TestFixture]
    public class PlayerMotorTests
    {
        private static PlayerMotor Motor(float speed = 5f, float minX = -8f, float maxX = 8f, float startX = 0f)
            => new PlayerMotor(speed, minX, maxX, startX);

        [Test]
        public void MovesAtSpeedTimesDeltaTime()
        {
            var motor = Motor();

            motor.Tick(1f, 1f);

            Assert.That(motor.PositionX, Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void ClampsToBounds()
        {
            var motor = Motor();

            motor.Tick(10f, 1f);
            Assert.That(motor.PositionX, Is.EqualTo(8f).Within(1e-4f));

            motor.Tick(10f, -1f);
            Assert.That(motor.PositionX, Is.EqualTo(-8f).Within(1e-4f));
        }

        [Test]
        public void ClampsAxisSoAKeyboardCannotOutrunAGamepad()
        {
            var normal = Motor();
            var cheating = Motor();

            normal.Tick(1f, 1f);
            cheating.Tick(1f, 50f);

            Assert.That(cheating.PositionX, Is.EqualTo(normal.PositionX).Within(1e-4f));
        }

        [Test]
        public void FirstShotIsAllowedImmediately()
        {
            var motor = Motor();

            Assert.That(motor.TryFire(2f), Is.True);
        }

        [Test]
        public void SecondShotIsRefusedUntilTheCooldownElapses()
        {
            var motor = Motor();
            motor.TryFire(2f); // 2 shots/sec => 0.5s cooldown

            Assert.That(motor.TryFire(2f), Is.False);

            motor.Tick(0.49f, 0f);
            Assert.That(motor.TryFire(2f), Is.False);

            motor.Tick(0.02f, 0f);
            Assert.That(motor.TryFire(2f), Is.True);
        }

        [Test]
        public void FireRateGovernsShotsPerSecond()
        {
            var motor = Motor();
            int shots = 0;

            // One simulated second at 60fps with a 10/sec weapon.
            for (int i = 0; i < 60; i++)
            {
                motor.Tick(1f / 60f, 0f);
                if (motor.TryFire(10f))
                {
                    shots++;
                }
            }

            Assert.That(shots, Is.InRange(9, 11));
        }

        [Test]
        public void RejectsInvalidConstruction()
        {
            Assert.Throws<ArgumentException>(() => Motor(minX: 5f, maxX: 5f));
            Assert.Throws<ArgumentException>(() => Motor(minX: 5f, maxX: 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Motor(speed: -1f));
        }

        [Test]
        public void RejectsNonPositiveFireRate()
        {
            var motor = Motor();

            Assert.Throws<ArgumentOutOfRangeException>(() => motor.TryFire(0f));
        }

        [Test]
        public void StartPositionIsClampedIntoBounds()
        {
            var motor = Motor(startX: 999f);

            Assert.That(motor.PositionX, Is.EqualTo(8f).Within(1e-4f));
        }

        [Test]
        public void RejectsNaNDeltaTime()
        {
            // The specification requires dt to be a finite number >= 0. NaN is not
            // finite, so Tick must reject it and leave PositionX and FireCooldown
            // exactly as they were before the call.
            var motor = Motor();
            float positionBefore = motor.PositionX;
            float cooldownBefore = motor.FireCooldown;

            Assert.Throws<ArgumentOutOfRangeException>(() => motor.Tick(float.NaN, 1f));

            Assert.That(motor.PositionX, Is.EqualTo(positionBefore));
            Assert.That(motor.FireCooldown, Is.EqualTo(cooldownBefore));
        }

        [Test]
        public void NaNDeltaTimeDoesNotPermanentlyBreakFireRate()
        {
            // The specification requires a rejected Tick call to leave FireCooldown
            // exactly as it was, so TryFire must keep respecting whatever cooldown was
            // already in effect rather than firing on every call from then on.
            var motor = Motor();
            Assert.That(motor.TryFire(2f), Is.True); // 2 shots/sec => 0.5s cooldown

            Assert.Throws<ArgumentOutOfRangeException>(() => motor.Tick(float.NaN, 0f));

            Assert.That(motor.TryFire(2f), Is.False,
                "the cooldown in effect before the rejected Tick call must still apply");
        }

        [Test]
        public void RejectsPositiveInfinityDeltaTime()
        {
            // The specification requires dt to be a finite number >= 0.
            // float.PositiveInfinity is not finite, so Tick must reject it and leave
            // PositionX and FireCooldown exactly as they were before the call.
            var motor = Motor();
            float positionBefore = motor.PositionX;
            float cooldownBefore = motor.FireCooldown;

            Assert.Throws<ArgumentOutOfRangeException>(() => motor.Tick(float.PositiveInfinity, 1f));

            Assert.That(motor.PositionX, Is.EqualTo(positionBefore));
            Assert.That(motor.FireCooldown, Is.EqualTo(cooldownBefore));
        }

        [Test]
        public void PositiveInfinityDeltaTimeDoesNotPermanentlyBreakFireRate()
        {
            // Same unchanged-state and cooldown-respecting guarantee as the NaN case,
            // for float.PositiveInfinity.
            var motor = Motor();
            Assert.That(motor.TryFire(2f), Is.True); // 2 shots/sec => 0.5s cooldown

            Assert.Throws<ArgumentOutOfRangeException>(() => motor.Tick(float.PositiveInfinity, 0f));

            Assert.That(motor.TryFire(2f), Is.False,
                "the cooldown in effect before the rejected Tick call must still apply");
        }

        [Test]
        public void RejectsNegativeInfinityDeltaTime()
        {
            // Already throws pre-fix via the old `dt < 0f` guard; this is not a
            // verdict-changing case, but the corrected guard must keep rejecting it.
            var motor = Motor();

            Assert.Throws<ArgumentOutOfRangeException>(() => motor.Tick(float.NegativeInfinity, 0f));
        }
    }
}
