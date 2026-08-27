using System;
using System.Collections.Generic;
using MergeSurvivor.Core.Rng;
using MergeSurvivor.Core.Spawning;
using NUnit.Framework;

namespace MergeSurvivor.Core.Tests
{
    [TestFixture]
    public class WaveSchedulerTests
    {
        private static WaveScheduler Scheduler(float firstDelay = 1f, float interval = 2f, float halfWidth = 8f, uint seed = 1u)
            => new WaveScheduler(new XorShiftRng(seed), firstDelay, interval, halfWidth);

        [Test]
        public void NoSpawnsBeforeTheFirstDelayElapses()
        {
            var scheduler = Scheduler(firstDelay: 1f);
            var buffer = new List<SpawnRequest>();

            int count = scheduler.Tick(0.5f, buffer);

            Assert.That(count, Is.Zero);
            Assert.That(buffer, Is.Empty);
        }

        [Test]
        public void SpawnsExactlyOnceWhenTheFirstDelayIsReached()
        {
            var scheduler = Scheduler(firstDelay: 1f, interval: 2f);
            var buffer = new List<SpawnRequest>();

            scheduler.Tick(1f, buffer);

            Assert.That(buffer.Count, Is.EqualTo(1));
        }

        [Test]
        public void ALongFrameProducesEverySpawnThatCameDue()
        {
            // The regression that motivated replacing InvokeRepeating: a single 5s
            // hitch must still yield the spawns due at t=1, t=3 and t=5, not just one.
            var scheduler = Scheduler(firstDelay: 1f, interval: 2f);
            var buffer = new List<SpawnRequest>();

            int count = scheduler.Tick(5f, buffer);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(buffer.Count, Is.EqualTo(3));
        }

        [Test]
        public void ManySmallTicksAndOneBigTickAgreeOnSpawnCount()
        {
            var stepped = Scheduler(firstDelay: 1f, interval: 2f);
            var jumped = Scheduler(firstDelay: 1f, interval: 2f);
            var steppedBuffer = new List<SpawnRequest>();
            var jumpedBuffer = new List<SpawnRequest>();

            for (int i = 0; i < 100; i++)
            {
                stepped.Tick(0.1f, steppedBuffer);
            }

            jumped.Tick(10f, jumpedBuffer);

            // Frame rate must not change how much content the player sees.
            Assert.That(steppedBuffer.Count, Is.EqualTo(jumpedBuffer.Count));
        }

        [Test]
        public void SameSeedProducesSamePositions()
        {
            var a = Scheduler(seed: 4242u);
            var b = Scheduler(seed: 4242u);
            var bufferA = new List<SpawnRequest>();
            var bufferB = new List<SpawnRequest>();

            a.Tick(21f, bufferA);
            b.Tick(21f, bufferB);

            Assert.That(bufferB.Count, Is.EqualTo(bufferA.Count));
            for (int i = 0; i < bufferA.Count; i++)
            {
                Assert.That(bufferB[i].X, Is.EqualTo(bufferA[i].X));
            }
        }

        [Test]
        public void SpawnPositionsStayInsideTheConfiguredWidth()
        {
            var scheduler = Scheduler(halfWidth: 8f);
            var buffer = new List<SpawnRequest>();

            scheduler.Tick(201f, buffer);

            Assert.That(buffer, Is.Not.Empty);
            foreach (SpawnRequest request in buffer)
            {
                Assert.That(request.X, Is.GreaterThanOrEqualTo(-8f));
                Assert.That(request.X, Is.LessThan(8f));
            }
        }

        [Test]
        public void RejectsNonPositiveInterval()
        {
            // A zero interval would spin forever inside a single Tick.
            Assert.Throws<ArgumentOutOfRangeException>(() => Scheduler(interval: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Scheduler(interval: -1f));
        }

        [Test]
        public void RejectsNegativeDeltaTime()
        {
            var scheduler = Scheduler();

            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Tick(-0.1f, new List<SpawnRequest>()));
        }

        [Test]
        public void RejectsNullBuffer()
        {
            var scheduler = Scheduler();

            Assert.Throws<ArgumentNullException>(() => scheduler.Tick(1f, null));
        }
    }
}
