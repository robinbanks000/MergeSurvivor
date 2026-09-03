using System;
using MergeSurvivor.Core.Rng;
using NUnit.Framework;

namespace MergeSurvivor.Core.Tests
{
    [TestFixture]
    public class XorShiftRngTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalSequence()
        {
            // This is the property the entire balance-simulation and bug-replay story
            // rests on. If it ever fails, seeded bug reports stop being reproducible.
            var a = new XorShiftRng(12345u);
            var b = new XorShiftRng(12345u);

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(b.NextUInt(), Is.EqualTo(a.NextUInt()), $"Diverged at draw {i}.");
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new XorShiftRng(1u);
            var b = new XorShiftRng(2u);

            bool anyDifference = false;
            for (int i = 0; i < 100; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.That(anyDifference, Is.True);
        }

        [Test]
        public void SeedZero_DoesNotCollapseToAFixedPoint()
        {
            // xorshift has a zero fixed point; seed 0 must be remapped or the stream
            // would return 0 forever and every "random" spawn would land dead centre.
            var rng = new XorShiftRng(0u);

            uint first = rng.NextUInt();
            uint second = rng.NextUInt();

            Assert.That(first, Is.Not.EqualTo(0u));
            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void NextFloat_StaysWithinUnitInterval()
        {
            var rng = new XorShiftRng(99u);

            for (int i = 0; i < 10000; i++)
            {
                float value = rng.NextFloat();
                Assert.That(value, Is.GreaterThanOrEqualTo(0f));
                Assert.That(value, Is.LessThan(1f));
            }
        }

        [Test]
        public void NextInt_StaysWithinRequestedRange()
        {
            var rng = new XorShiftRng(7u);

            for (int i = 0; i < 10000; i++)
            {
                int value = rng.NextInt(-5, 5);
                Assert.That(value, Is.GreaterThanOrEqualTo(-5));
                Assert.That(value, Is.LessThan(5));
            }
        }

        [Test]
        public void NextInt_RejectsEmptyRange()
        {
            var rng = new XorShiftRng(1u);

            Assert.Throws<ArgumentException>(() => rng.NextInt(5, 5));
            Assert.Throws<ArgumentException>(() => rng.NextInt(5, 4));
        }

        [Test]
        public void NextRange_RejectsInvertedBounds()
        {
            var rng = new XorShiftRng(1u);

            Assert.Throws<ArgumentException>(() => rng.NextRange(1f, 0f));
        }

        [Test]
        public void State_ReflectsStreamPosition()
        {
            var rng = new XorShiftRng(42u);
            rng.NextUInt();
            uint captured = rng.State;

            uint expectedNext = rng.NextUInt();

            // Resuming from a captured state must continue the same stream, which is
            // what makes mid-run save/resume possible later.
            var resumed = new XorShiftRng(captured);
            Assert.That(resumed.NextUInt(), Is.EqualTo(expectedNext));
        }
    }
}
