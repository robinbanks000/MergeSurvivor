using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        /// <summary>
        /// Mirrors the exact shape of Tick's guard -- a float FIELD minus a float
        /// PARAMETER, then a field added to the result -- so the probes below exercise
        /// what Tick actually does. The first version of this diagnostic used locals
        /// initialised from literals; it passed in PlayMode while Tick still failed
        /// there, which proved only that I had tested the wrong thing. NoInlining keeps
        /// the JIT from folding the arithmetic away and answering a question nobody
        /// asked.
        /// </summary>
        private sealed class GuardShapeProbe
        {
            public float Timer;
            public float Interval;

            public GuardShapeProbe(float timer, float interval)
            {
                Timer = timer;
                Interval = interval;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public float ProspectiveTimer(float dt) => Timer - dt;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public float Advanced(float prospective) => prospective + Interval;

            /// <summary>The guard exactly as WaveScheduler.Tick writes it.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            public bool SaysAbsorbed(float dt)
            {
                float prospective = Timer - dt;
                return prospective <= 0f && !(prospective + Interval > prospective);
            }
        }

        private static string Bits(float value) =>
            $"{value:R} (0x{BitConverter.SingleToInt32Bits(value):X8})";

        [Test]
        public void ProbeA_FieldMinusParameterRoundsToBinary32()
        {
            // Link one. 1f - 1e8f must round to exactly -1e8f: ulp is 8 at that
            // magnitude, and -99999999 is 1 away from -1e8 and 7 from -99999992.
            var probe = new GuardShapeProbe(1f, 2f);
            float prospective = probe.ProspectiveTimer(1e8f);

            Assert.That(
                BitConverter.SingleToInt32Bits(prospective),
                Is.EqualTo(BitConverter.SingleToInt32Bits(-1e8f)),
                $"the subtraction did not round to binary32. got {Bits(prospective)}, expected {Bits(-1e8f)}");
        }

        [Test]
        public void ProbeB_AddingTheIntervalFieldIsAbsorbed()
        {
            // Link two. -1e8f + 2f must round back to bit-identical: 2 is below half
            // an ulp of 8.
            var probe = new GuardShapeProbe(1f, 2f);
            float prospective = probe.ProspectiveTimer(1e8f);
            float advanced = probe.Advanced(prospective);

            Assert.That(
                BitConverter.SingleToInt32Bits(advanced),
                Is.EqualTo(BitConverter.SingleToInt32Bits(prospective)),
                $"the addition was not absorbed. prospective {Bits(prospective)}, advanced {Bits(advanced)}");
        }

        [Test]
        public void ProbeC_TheGuardExpressionSaysAbsorbed()
        {
            // Link three: the composite expression. If A and B pass and this fails, the
            // operands were rounded when stored but the comparison itself was evaluated
            // at wider precision -- which is a fact about expression evaluation, not
            // about the arithmetic, and needs a different remedy from either.
            var probe = new GuardShapeProbe(1f, 2f);

            Assert.That(
                probe.SaysAbsorbed(1e8f),
                Is.True,
                "the guard expression, in the exact shape Tick writes it, did not "
                + "detect absorption -- so Tick will not reject this dt on this runtime.");
        }

        [Test]
        public void Binary32AdditionAbsorbsTheIntervalAtTheScaleCriterion11Names()
        {
            // The PREMISE the guard in Tick rests on, pinned as a test because nothing
            // tested it and it turns out not to hold everywhere.
            //
            // Criterion 11 clause (2) states it as settled fact -- "verified: with
            // _timeUntilNextSpawn at 1f - 1e8f, adding 2f returns a bit-identical value"
            // -- and Tick's doc comment repeats it. Under .NET on x64 that is true:
            // 1f - 1e8f rounds to -1e8f exactly (ulp is 8 at that magnitude), and
            // -1e8f + 2f rounds back to the same bits. The guard is built on it.
            //
            // G3 run 46 says it does not hold in Unity PlayMode: the guard did not fire
            // and WaveSchedulerTests.cs:311 failed on the very dt this premise covers,
            // while the same source passed under .NET and under Unity EditMode.
            //
            // This test separates the two ways that can happen, which need different
            // fixes: if `advanced`'s BITS differ from `timer`'s, the runtime's binary32
            // addition genuinely produces a different value; if the bits are identical
            // but the comparison still says greater, the addition and comparison were
            // evaluated at wider precision and never rounded to binary32 -- a fact about
            // expression evaluation, not about the arithmetic. The message prints both,
            // so the failure names which one rather than leaving it to be inferred.
            float timer = 1f - 1e8f;
            float interval = 2f;
            float advanced = timer + interval;

            int timerBits = BitConverter.SingleToInt32Bits(timer);
            int advancedBits = BitConverter.SingleToInt32Bits(advanced);

            Assert.That(
                advanced > timer,
                Is.False,
                $"binary32 addition was expected to absorb the interval at this scale. "
                + $"timer={timer:R} (bits 0x{timerBits:X8}), advanced={advanced:R} (bits 0x{advancedBits:X8}), "
                + $"bits {(timerBits == advancedBits ? "IDENTICAL -- so the comparison was evaluated at wider precision than binary32" : "DIFFER -- so this runtime's binary32 addition genuinely advances here")}.");
        }

        [Test]
        public void RejectsAbsorbingDeltaTimeThatWouldNeverAdvanceTheSchedule()
        {
            // RATCHET (CHA-0001 / RUL-0003 matter 4(i)): the specification requires
            // Tick to reject, before mutating state or appending anything, any dt for
            // which binary32 addition of _interval to the post-subtraction timer would
            // not strictly increase it -- because that is precisely the condition under
            // which the catch-up loop below would never terminate. With
            // Scheduler(firstDelay: 1f, interval: 2f), dt = 1e8f is such a value: `1f -
            // 1e8f` and `(1f - 1e8f) + 2f` are bit-identical in binary32. Rejection must
            // leave the buffer untouched and the internal schedule exactly as it was, so
            // a later valid Tick call spawns exactly as if the rejected call had never
            // happened.
            //
            // Against a Tick that lacks this check, this fails fast and deterministically
            // with BoundedBufferCapacityExceededException ("Bounded test buffer exceeded
            // its cap of 1000 items without the spawn loop terminating."), not
            // ArgumentOutOfRangeException and not a hang, because the bounded buffer
            // throws once the loop runs past 1000 iterations -- see recorded evidence.
            var untouched = Scheduler(firstDelay: 1f, interval: 2f);
            var exercised = Scheduler(firstDelay: 1f, interval: 2f);
            var untouchedBuffer = new List<SpawnRequest>();
            var boundedBuffer = new BoundedSpawnRequestBuffer();

            Assert.Throws<ArgumentOutOfRangeException>(() => exercised.Tick(1e8f, boundedBuffer));
            Assert.That(boundedBuffer.Count, Is.Zero);

            int untouchedCount = untouched.Tick(5f, untouchedBuffer);
            var exercisedBuffer = new List<SpawnRequest>();
            int exercisedCount = exercised.Tick(5f, exercisedBuffer);

            Assert.That(exercisedCount, Is.EqualTo(untouchedCount));
            Assert.That(exercisedBuffer.Count, Is.EqualTo(untouchedBuffer.Count));
        }

        [Test]
        public void AcceptsALargeDeltaTimeThatCanStillAdvanceTheSchedule()
        {
            // NON-OVER-REJECTION (amended criterion 11 clause (4); RUL-0003 matter
            // 4(ii)): the specification requires that a large finite dt for which the
            // schedule can still genuinely advance must be accepted, not rejected, and
            // Tick must return the full catch-up count with every due spawn appended.
            // Without this test, the ratchet above is satisfiable by an implementation
            // that rejects any dt above some conservative fixed constant, which would
            // defeat the scheduler while still appearing to fix the hang.
            //
            // dt = 1e5 seconds at interval 2f is nowhere near the absorption relation
            // (full absorption requires roughly 2^24 due spawns; this dt due only
            // 50000), so the schedule provably still advances through ordinary,
            // unabsorbed binary32 addition.
            var scheduler = Scheduler(firstDelay: 1f, interval: 2f);
            var buffer = new List<SpawnRequest>();

            int count = scheduler.Tick(100_000f, buffer);

            Assert.That(count, Is.EqualTo(50_000));
            Assert.That(buffer.Count, Is.EqualTo(50_000));
        }

        [Test]
        public void NaNDeltaTimeDoesNotMutateInternalTimerWithNonDefaultState()
        {
            // Criterion 2: A rejected Tick call must leave _timeUntilNextSpawn exactly
            // as it was. This test verifies this against a plausible-wrong implementation:
            // one that subtracts dt from _timeUntilNextSpawn before calling the DtGuard.
            // The test NaNDeltaTimeDoesNotAffectSubsequentSpawns verifies the effect
            // indirectly by comparing two schedulers; this test verifies directly by
            // checking that after a rejected call with NaN, the scheduler's behavior
            // is identical to one that never received the call. We set non-default
            // initial delay so any state mutation would be observable.
            // Plausible-wrong implementation: modifies _timeUntilNextSpawn before validating dt.
            var withoutNaN = Scheduler(firstDelay: 0.5f, interval: 2f);
            var withNaN = Scheduler(firstDelay: 0.5f, interval: 2f);
            var bufferWithoutNaN = new List<SpawnRequest>();
            var bufferWithNaN = new List<SpawnRequest>();

            // Tick both with 0.3f; neither should spawn yet (0.5f delay not met)
            int countWithoutNaN1 = withoutNaN.Tick(0.3f, bufferWithoutNaN);
            Assert.That(countWithoutNaN1, Is.Zero);

            int countWithNaN1 = withNaN.Tick(0.3f, bufferWithNaN);
            Assert.That(countWithNaN1, Is.Zero);

            // Now hit withNaN with NaN - this should NOT advance its internal timer
            var naNBuffer = new List<SpawnRequest>();
            Assert.Throws<ArgumentOutOfRangeException>(() => withNaN.Tick(float.NaN, naNBuffer));
            Assert.That(naNBuffer, Is.Empty);

            // Now tick both with 0.3f more; they should spawn at the same time (0.6f total)
            int countWithoutNaN2 = withoutNaN.Tick(0.3f, bufferWithoutNaN);
            int countWithNaN2 = withNaN.Tick(0.3f, bufferWithNaN);

            // If the NaN call modified the timer before throwing, countWithNaN2 would differ
            Assert.That(countWithNaN2, Is.EqualTo(countWithoutNaN2),
                "Internal timer must not be modified by a rejected Tick call");
            Assert.That(bufferWithNaN.Count, Is.EqualTo(bufferWithoutNaN.Count),
                "Spawn count must not be affected by a rejected Tick call");
        }

        [Test]
        public void PositiveInfinityDeltaTimeDoesNotMutateInternalTimerWithNonDefaultState()
        {
            // Criterion 5: Similar to NaN case above, for float.PositiveInfinity.
            // A rejected Tick call must leave _timeUntilNextSpawn exactly as it was.
            // Plausible-wrong implementation: modifies _timeUntilNextSpawn before validating dt.
            var withoutInfinity = Scheduler(firstDelay: 0.5f, interval: 2f);
            var withInfinity = Scheduler(firstDelay: 0.5f, interval: 2f);
            var bufferWithoutInfinity = new List<SpawnRequest>();
            var bufferWithInfinity = new List<SpawnRequest>();

            // Tick both with 0.3f; neither should spawn yet (0.5f delay not met)
            int countWithoutInfinity1 = withoutInfinity.Tick(0.3f, bufferWithoutInfinity);
            Assert.That(countWithoutInfinity1, Is.Zero);

            int countWithInfinity1 = withInfinity.Tick(0.3f, bufferWithInfinity);
            Assert.That(countWithInfinity1, Is.Zero);

            // Now hit withInfinity with PositiveInfinity - this should NOT advance its internal timer
            var infinityBuffer = new BoundedSpawnRequestBuffer();
            Assert.Throws<ArgumentOutOfRangeException>(() => withInfinity.Tick(float.PositiveInfinity, infinityBuffer));
            Assert.That(infinityBuffer.Count, Is.Zero);

            // Now tick both with 0.3f more; they should spawn at the same time (0.6f total)
            int countWithoutInfinity2 = withoutInfinity.Tick(0.3f, bufferWithoutInfinity);
            int countWithInfinity2 = withInfinity.Tick(0.3f, bufferWithInfinity);

            // If the PositiveInfinity call modified the timer before throwing, countWithInfinity2 would differ
            Assert.That(countWithInfinity2, Is.EqualTo(countWithoutInfinity2),
                "Internal timer must not be modified by a rejected Tick call");
            Assert.That(bufferWithInfinity.Count, Is.EqualTo(bufferWithoutInfinity.Count),
                "Spawn count must not be affected by a rejected Tick call");
        }
    }
}
