using System;
using System.Collections;
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

        /// <summary>
        /// Thrown by <see cref="BoundedSpawnRequestBuffer"/> once its cap is exceeded.
        /// Distinct from ArgumentOutOfRangeException so a test asserting
        /// Assert.Throws&lt;ArgumentOutOfRangeException&gt; fails with a clear "wrong
        /// exception type" mismatch -- fast and deterministic -- rather than hanging,
        /// against a Tick implementation whose guard lets a non-finite dt reach the
        /// spawn loop.
        /// </summary>
        private sealed class BoundedBufferCapacityExceededException : Exception
        {
            public BoundedBufferCapacityExceededException(string message) : base(message)
            {
            }
        }

        /// <summary>
        /// A test-local IList&lt;SpawnRequest&gt; that throws
        /// <see cref="BoundedBufferCapacityExceededException"/> from Add once it holds
        /// more than <see cref="Cap"/> items, so a test driving WaveScheduler.Tick with
        /// a non-finite dt fails fast against a non-terminating spawn loop instead of
        /// hanging the test run or exhausting memory.
        /// </summary>
        private sealed class BoundedSpawnRequestBuffer : IList<SpawnRequest>
        {
            private const int Cap = 1000;
            private readonly List<SpawnRequest> _inner = new List<SpawnRequest>();

            public SpawnRequest this[int index]
            {
                get => _inner[index];
                set => _inner[index] = value;
            }

            public int Count => _inner.Count;

            public bool IsReadOnly => false;

            public void Add(SpawnRequest item)
            {
                if (_inner.Count >= Cap)
                {
                    throw new BoundedBufferCapacityExceededException(
                        $"Bounded test buffer exceeded its cap of {Cap} items without the spawn loop terminating.");
                }

                _inner.Add(item);
            }

            public void Clear() => _inner.Clear();

            public bool Contains(SpawnRequest item) => _inner.Contains(item);

            public void CopyTo(SpawnRequest[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);

            public IEnumerator<SpawnRequest> GetEnumerator() => _inner.GetEnumerator();

            public int IndexOf(SpawnRequest item) => _inner.IndexOf(item);

            public void Insert(int index, SpawnRequest item) => _inner.Insert(index, item);

            public bool Remove(SpawnRequest item) => _inner.Remove(item);

            public void RemoveAt(int index) => _inner.RemoveAt(index);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

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

        [Test]
        public void RejectsNaNDeltaTime()
        {
            // The specification requires dt to be a finite number >= 0. NaN is not
            // finite, so Tick must reject it and append nothing to the buffer.
            var scheduler = Scheduler();
            var buffer = new List<SpawnRequest>();

            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Tick(float.NaN, buffer));

            Assert.That(buffer, Is.Empty);
        }

        [Test]
        public void NaNDeltaTimeDoesNotAffectSubsequentSpawns()
        {
            // The specification requires a rejected Tick call to leave the scheduler's
            // internal schedule exactly as it was, so a later valid Tick must spawn
            // exactly as if the NaN call had never happened.
            var untouched = Scheduler(firstDelay: 1f, interval: 2f);
            var exercised = Scheduler(firstDelay: 1f, interval: 2f);
            var untouchedBuffer = new List<SpawnRequest>();
            var exercisedBuffer = new List<SpawnRequest>();

            Assert.Throws<ArgumentOutOfRangeException>(() => exercised.Tick(float.NaN, exercisedBuffer));
            Assert.That(exercisedBuffer, Is.Empty);

            untouched.Tick(5f, untouchedBuffer);
            exercised.Tick(5f, exercisedBuffer);

            Assert.That(exercisedBuffer.Count, Is.EqualTo(untouchedBuffer.Count));
        }

        [Test]
        public void PositiveInfinityDeltaTimeThrowsInsteadOfLoopingForever()
        {
            // The specification requires dt to be a finite number >= 0, rejected before
            // the spawn loop is ever entered, so dt = +Infinity must never reach
            // `while (_timeUntilNextSpawn <= 0f)`. Against the unmodified guard
            // (`dt < 0f`), +Infinity passes, `_timeUntilNextSpawn -= dt` becomes
            // -Infinity, and the loop never terminates because -Infinity plus any
            // finite interval is still -Infinity; against that pre-fix code this test
            // fails fast with BoundedBufferCapacityExceededException (message: "Bounded
            // test buffer exceeded its cap of 1000 items without the spawn loop
            // terminating."), not ArgumentOutOfRangeException and not a hang, because
            // the bounded buffer throws once the loop runs past 1000 iterations.
            var scheduler = Scheduler();
            var buffer = new BoundedSpawnRequestBuffer();

            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Tick(float.PositiveInfinity, buffer));

            Assert.That(buffer.Count, Is.Zero);
        }

        [Test]
        public void PositiveInfinityDeltaTimeDoesNotAffectSubsequentSpawns()
        {
            // The specification requires a rejected Tick call to leave the scheduler's
            // internal schedule exactly as it was, so a later valid Tick must spawn
            // exactly as if the PositiveInfinity call had never happened. The bounded
            // buffer is used for the throwing call only, so this test cannot hang even
            // if the guard regresses.
            var untouched = Scheduler(firstDelay: 1f, interval: 2f);
            var exercised = Scheduler(firstDelay: 1f, interval: 2f);
            var untouchedBuffer = new List<SpawnRequest>();
            var exercisedBuffer = new List<SpawnRequest>();
            var boundedBuffer = new BoundedSpawnRequestBuffer();

            Assert.Throws<ArgumentOutOfRangeException>(() => exercised.Tick(float.PositiveInfinity, boundedBuffer));
            Assert.That(boundedBuffer.Count, Is.Zero);

            untouched.Tick(5f, untouchedBuffer);
            exercised.Tick(5f, exercisedBuffer);

            Assert.That(exercisedBuffer.Count, Is.EqualTo(untouchedBuffer.Count));
        }

        [Test]
        public void RejectsNegativeInfinityDeltaTime()
        {
            // Already throws pre-fix via the old `dt < 0f` guard; this is not a
            // verdict-changing case, but the corrected guard must keep rejecting it.
            var scheduler = Scheduler();
            var buffer = new List<SpawnRequest>();

            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Tick(float.NegativeInfinity, buffer));

            Assert.That(buffer, Is.Empty);
        }
    }
}
