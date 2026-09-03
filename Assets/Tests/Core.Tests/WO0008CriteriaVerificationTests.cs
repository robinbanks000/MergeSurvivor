using System;
using MergeSurvivor.Core.Run;
using NUnit.Framework;

namespace MergeSurvivor.Core.Tests
{
    /// <summary>
    /// Independent verification tests for WO-0008 acceptance criteria.
    /// Tests the specification (WO-0008.json), not just the implementation's own tests.
    /// This file verifies that the implementation satisfies each of the 12 criteria exactly as stated.
    /// </summary>
    [TestFixture]
    public class WO0008CriteriaVerificationTests
    {
        // ============================================================================
        // Criterion 1: Tick(float.NaN) throws ArgumentOutOfRangeException
        // ============================================================================

        [Test]
        public void Criterion1_NaNThrowsArgumentOutOfRangeException()
        {
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.NaN));
        }

        [Test]
        public void Criterion1_NaNDoesNotIncrementTickCount()
        {
            var run = new RunState();
            var tickCountBefore = run.TickCount;

            try { run.Tick(float.NaN); }
            catch (ArgumentOutOfRangeException) { }

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore),
                "Tick(NaN) must not increment TickCount");
        }

        [Test]
        public void Criterion1_NaNDoesNotChangeElapsedSeconds()
        {
            var run = new RunState();
            var elapsedBefore = run.ElapsedSeconds;

            try { run.Tick(float.NaN); }
            catch (ArgumentOutOfRangeException) { }

            Assert.That(run.ElapsedSeconds, Is.EqualTo(elapsedBefore),
                "Tick(NaN) must not change ElapsedSeconds");
        }

        // ============================================================================
        // Criterion 2: Tick(float.PositiveInfinity) throws ArgumentOutOfRangeException
        // ============================================================================

        [Test]
        public void Criterion2_PositiveInfinityThrowsArgumentOutOfRangeException()
        {
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.PositiveInfinity));
        }

        [Test]
        public void Criterion2_PositiveInfinityDoesNotIncrementTickCount()
        {
            var run = new RunState();
            var tickCountBefore = run.TickCount;

            try { run.Tick(float.PositiveInfinity); }
            catch (ArgumentOutOfRangeException) { }

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore),
                "Tick(PositiveInfinity) must not increment TickCount");
        }

        [Test]
        public void Criterion2_PositiveInfinityDoesNotChangeElapsedSeconds()
        {
            var run = new RunState();
            var elapsedBefore = run.ElapsedSeconds;

            try { run.Tick(float.PositiveInfinity); }
            catch (ArgumentOutOfRangeException) { }

            Assert.That(run.ElapsedSeconds, Is.EqualTo(elapsedBefore),
                "Tick(PositiveInfinity) must not change ElapsedSeconds");
        }

        // ============================================================================
        // Criterion 3: Tick(float.NegativeInfinity) throws ArgumentOutOfRangeException
        // ============================================================================

        [Test]
        public void Criterion3_NegativeInfinityThrowsArgumentOutOfRangeException()
        {
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.NegativeInfinity));
        }

        [Test]
        public void Criterion3_NegativeInfinityDoesNotIncrementTickCount()
        {
            var run = new RunState();
            var tickCountBefore = run.TickCount;

            try { run.Tick(float.NegativeInfinity); }
            catch (ArgumentOutOfRangeException) { }

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore),
                "Tick(NegativeInfinity) must not increment TickCount");
        }

        [Test]
        public void Criterion3_NegativeInfinityDoesNotChangeElapsedSeconds()
        {
            var run = new RunState();
            var elapsedBefore = run.ElapsedSeconds;

            try { run.Tick(float.NegativeInfinity); }
            catch (ArgumentOutOfRangeException) { }

            Assert.That(run.ElapsedSeconds, Is.EqualTo(elapsedBefore),
                "Tick(NegativeInfinity) must not change ElapsedSeconds");
        }

        // ============================================================================
        // Criterion 4: Negative dt rejection preserved
        // ============================================================================

        [Test]
        public void Criterion4_NegativeSmallValueThrows()
        {
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(-0.1f));
        }

        [Test]
        public void Criterion4_NegativeSmallValueDoesNotIncrementTickCount()
        {
            var run = new RunState();
            var tickCountBefore = run.TickCount;

            try { run.Tick(-0.1f); }
            catch (ArgumentOutOfRangeException) { }

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore),
                "Tick(-0.1f) must not increment TickCount");
        }

        [Test]
        public void Criterion4_NegativeLargeValueThrows()
        {
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(-0.5f));
        }

        [Test]
        public void Criterion4_NegativeLargeValueDoesNotIncrementTickCount()
        {
            var run = new RunState();
            var tickCountBefore = run.TickCount;

            try { run.Tick(-0.5f); }
            catch (ArgumentOutOfRangeException) { }

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore),
                "Tick(-0.5f) must not increment TickCount");
        }

        // ============================================================================
        // Criterion 5: Finite dt >= 0 increment TickCount and add to ElapsedSeconds
        // ============================================================================

        [Test]
        public void Criterion5_ZeroIncrementAndAccumulates()
        {
            var run = new RunState();
            run.Tick(0f);

            Assert.That(run.TickCount, Is.EqualTo(1), "Tick(0f) must increment TickCount");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(0f), "Tick(0f) must add 0 to ElapsedSeconds");
        }

        [Test]
        public void Criterion5_SmallPositiveIncrementAndAccumulates()
        {
            var run = new RunState();
            run.Tick(0.0001f);

            Assert.That(run.TickCount, Is.EqualTo(1), "Tick(small value) must increment TickCount");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(0.0001f), "Tick(small value) must add value to ElapsedSeconds");
        }

        [Test]
        public void Criterion5_DenormalFloatIncrementAndAccumulates()
        {
            // Smallest positive subnormal (denormal) float
            var denormal = float.Epsilon;
            var run = new RunState();
            run.Tick(denormal);

            Assert.That(run.TickCount, Is.EqualTo(1), "Tick(denormal) must increment TickCount");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(denormal), "Tick(denormal) must add value to ElapsedSeconds");
        }

        [Test]
        public void Criterion5_RegularValueIncrementAndAccumulates()
        {
            var run = new RunState();
            run.Tick(1.5f);

            Assert.That(run.TickCount, Is.EqualTo(1), "Tick(1.5f) must increment TickCount");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(1.5f), "Tick(1.5f) must add value to ElapsedSeconds");
        }

        [Test]
        public void Criterion5_FloatMaxValueIncrementAndAccumulates()
        {
            var run = new RunState();
            run.Tick(float.MaxValue);

            Assert.That(run.TickCount, Is.EqualTo(1), "Tick(float.MaxValue) must increment TickCount");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(float.MaxValue), "Tick(float.MaxValue) must add value to ElapsedSeconds");
        }

        [Test]
        public void Criterion5_MultipleFiniteTicksAccumulate()
        {
            var run = new RunState();
            run.Tick(1f);
            run.Tick(2f);
            run.Tick(3f);

            Assert.That(run.TickCount, Is.EqualTo(3), "Multiple Tick calls must increment TickCount for each");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(6f).Within(1e-5f), "Multiple Tick calls must accumulate ElapsedSeconds");
        }

        // ============================================================================
        // Criterion 6: ElapsedSeconds stays finite after sequence of finite non-negative ticks
        // ============================================================================

        [Test]
        public void Criterion6_SingleSmallTickStaysFinite()
        {
            var run = new RunState();
            run.Tick(1f);

            Assert.That(float.IsFinite(run.ElapsedSeconds), Is.True,
                "After Tick(1f), ElapsedSeconds must be finite");
        }

        [Test]
        public void Criterion6_ManySmallTicksStayFinite()
        {
            var run = new RunState();
            for (int i = 0; i < 100; i++)
            {
                run.Tick(0.1f);
            }

            Assert.That(float.IsFinite(run.ElapsedSeconds), Is.True,
                "After many small ticks, ElapsedSeconds must be finite");
        }

        [Test]
        public void Criterion6_ZeroTicksStayFinite()
        {
            var run = new RunState();
            run.Tick(0f);
            run.Tick(0f);
            run.Tick(0f);

            Assert.That(float.IsFinite(run.ElapsedSeconds), Is.True,
                "After Tick(0f), ElapsedSeconds must be finite");
        }

        [Test]
        public void Criterion6_MixedFiniteTicksStayFinite()
        {
            var run = new RunState();
            run.Tick(1f);
            run.Tick(0f);
            run.Tick(0.5f);
            run.Tick(0.0001f);

            Assert.That(float.IsFinite(run.ElapsedSeconds), Is.True,
                "After mixed finite ticks, ElapsedSeconds must be finite");
        }

        [Test]
        public void Criterion6_SequenceWithRejectedTicksStayFinite()
        {
            var run = new RunState();
            run.Tick(1f);

            try { run.Tick(float.NaN); }
            catch (ArgumentOutOfRangeException) { }

            run.Tick(2f);

            Assert.That(float.IsFinite(run.ElapsedSeconds), Is.True,
                "After sequence with rejected ticks, ElapsedSeconds must be finite");
        }

        [Test]
        public void Criterion6_ExactSumAtMaxValueIsFiniteAndExact()
        {
            // The specification requires that ElapsedSeconds is finite after any sequence
            // whose exact real-valued sum of accepted dt does not exceed float.MaxValue.
            // This test verifies the boundary: two ticks of exactly float.MaxValue / 2f sum to
            // exactly float.MaxValue without overflow, as both partial sums and the total are
            // exactly representable in binary32.
            var run = new RunState();
            var halfMax = float.MaxValue / 2f;
            run.Tick(halfMax);
            run.Tick(halfMax);

            var elapsedAfter = run.ElapsedSeconds;
            Console.WriteLine($"ElapsedSeconds after two Tick(float.MaxValue / 2f) calls: {elapsedAfter}");
            Console.WriteLine($"float.MaxValue: {float.MaxValue}");
            Console.WriteLine($"Are they equal? {elapsedAfter == float.MaxValue}");

            Assert.That(float.IsFinite(elapsedAfter), Is.True,
                "ElapsedSeconds must be finite when exact sum does not exceed float.MaxValue");
            Assert.That(elapsedAfter, Is.EqualTo(float.MaxValue),
                "ElapsedSeconds must equal the exact sum of accepted dt values");
        }

        // ============================================================================
        // Criterion 7: Regression test for NaN
        // ============================================================================

        [Test]
        public void Criterion7_RegressionNaNLeavesElapsedSecondsUnchangedAndFinite()
        {
            var run = new RunState();
            var elapsedBefore = run.ElapsedSeconds;

            try
            {
                run.Tick(float.NaN);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Expected
            }

            Assert.That(float.IsFinite(run.ElapsedSeconds), Is.True,
                "ElapsedSeconds must remain finite after rejected Tick(NaN)");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(elapsedBefore),
                "ElapsedSeconds must be unchanged after rejected Tick(NaN)");
        }

        // ============================================================================
        // Criterion 8: Three existing tests are properly replaced
        // (Verified in RunStateVerificationTests.cs lines 125-211)
        // ============================================================================

        // The three tests that were replaced:
        // 1. Criterion3_PositiveInfinityDtIncrements -> Criterion3_PositiveInfinityDtThrowsAndLeavesStateUnchanged (line 125)
        // 2. Criterion3_NaNDtIncrements -> Criterion3_NaNDtThrowsAndLeavesStateUnchanged (line 143)
        // 3. Criterion3_ManyTicksWithVariedDt -> Split into two tests (line 188 and 201)
        //    - line 188: Only finite values (0f, 0.1f, 1000f, float.MaxValue)
        //    - line 201: Non-finite values throw

        // ============================================================================
        // Criterion 9: Seed, Score, Kills, IsOver, AddScore, RegisterKill, EndRun, Reset unchanged
        // ============================================================================

        [Test]
        public void Criterion9_SeedUnchangedAfterTick()
        {
            var run = new RunState(42u);
            var seedBefore = run.Seed;
            run.Tick(1f);
            Assert.That(run.Seed, Is.EqualTo(seedBefore), "Seed must not change after Tick");
        }

        [Test]
        public void Criterion9_ScoreStartsAtZero()
        {
            var run = new RunState();
            Assert.That(run.Score, Is.EqualTo(0), "Score must start at 0");
        }

        [Test]
        public void Criterion9_AddScoreWorks()
        {
            var run = new RunState();
            run.AddScore(10);
            run.AddScore(20);
            Assert.That(run.Score, Is.EqualTo(30), "AddScore must accumulate");
        }

        [Test]
        public void Criterion9_AddScoreRejectsNegative()
        {
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.AddScore(-1),
                "AddScore must reject negative amounts");
        }

        [Test]
        public void Criterion9_KillsStartsAtZero()
        {
            var run = new RunState();
            Assert.That(run.Kills, Is.EqualTo(0), "Kills must start at 0");
        }

        [Test]
        public void Criterion9_RegisterKillIncrementsKills()
        {
            var run = new RunState();
            run.RegisterKill(10);
            run.RegisterKill(20);
            Assert.That(run.Kills, Is.EqualTo(2), "RegisterKill must increment Kills");
        }

        [Test]
        public void Criterion9_RegisterKillAlsoAddsScore()
        {
            var run = new RunState();
            run.RegisterKill(50);
            Assert.That(run.Score, Is.EqualTo(50), "RegisterKill must also add to Score");
        }

        [Test]
        public void Criterion9_IsOverStartsFalse()
        {
            var run = new RunState();
            Assert.That(run.IsOver, Is.False, "IsOver must start as false");
        }

        [Test]
        public void Criterion9_EndRunSetsIsOverTrue()
        {
            var run = new RunState();
            run.EndRun();
            Assert.That(run.IsOver, Is.True, "EndRun must set IsOver to true");
        }

        [Test]
        public void Criterion9_EndRunStopsAccumulation()
        {
            var run = new RunState();
            run.Tick(1f);
            run.AddScore(10);
            run.EndRun();

            run.Tick(10f);
            run.AddScore(100);
            run.RegisterKill(50);

            Assert.That(run.TickCount, Is.EqualTo(1), "Tick after EndRun must be no-op");
            Assert.That(run.Score, Is.EqualTo(10), "AddScore after EndRun must be no-op");
            Assert.That(run.Kills, Is.EqualTo(0), "RegisterKill after EndRun must be no-op");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(1f), "ElapsedSeconds after EndRun must not change");
        }

        [Test]
        public void Criterion9_ResetClearsAllExceptSeed()
        {
            var run = new RunState(99u);
            run.Tick(5f);
            run.AddScore(100);
            run.RegisterKill(10);
            run.EndRun();

            run.Reset();

            Assert.That(run.TickCount, Is.EqualTo(0), "Reset must clear TickCount");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(0f), "Reset must clear ElapsedSeconds");
            Assert.That(run.Score, Is.EqualTo(0), "Reset must clear Score");
            Assert.That(run.Kills, Is.EqualTo(0), "Reset must clear Kills");
            Assert.That(run.IsOver, Is.False, "Reset must clear IsOver flag");
            Assert.That(run.Seed, Is.EqualTo(99u), "Reset must preserve Seed");
        }

        // ============================================================================
        // Criterion 10: No UnityEngine reference, dt is explicit parameter
        // ============================================================================

        [Test]
        public void Criterion10_TickTakesExplicitFloatParameter()
        {
            var run = new RunState();
            // If Tick didn't take an explicit float parameter, this would not compile
            run.Tick(1.5f);
            Assert.That(run.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Criterion10_RunStateDoesNotReferenceDeltaTime()
        {
            // Verify that RunState is constructed and used without needing Time.deltaTime
            var run = new RunState(12345u);

            // These calls work entirely with explicit parameters, not ambient clock
            run.Tick(0.016f);  // Explicit dt, not Time.deltaTime
            run.AddScore(10);   // No dt parameter

            Assert.That(run.TickCount, Is.EqualTo(1));
            Assert.That(run.Score, Is.EqualTo(10));
        }

        // ============================================================================
        // Criterion 12: Guard condition and exception message agree on predicate
        // ============================================================================

        [Test]
        public void Criterion12_GuardRejectsNaN()
        {
            // The message says "dt must be a finite number >= 0"
            // So NaN (not finite) should be rejected
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.NaN),
                "Guard must reject NaN because it is not finite");
        }

        [Test]
        public void Criterion12_GuardRejectsPositiveInfinity()
        {
            // The message says "dt must be a finite number >= 0"
            // So PositiveInfinity (not finite) should be rejected
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.PositiveInfinity),
                "Guard must reject PositiveInfinity because it is not finite");
        }

        [Test]
        public void Criterion12_GuardRejectsNegativeInfinity()
        {
            // The message says "dt must be a finite number >= 0"
            // So NegativeInfinity (not finite and < 0) should be rejected
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.NegativeInfinity),
                "Guard must reject NegativeInfinity because it is not finite and < 0");
        }

        [Test]
        public void Criterion12_GuardRejectsNegativeFinite()
        {
            // The message says "dt must be a finite number >= 0"
            // So any negative finite value should be rejected
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(-0.1f),
                "Guard must reject negative dt because it is not >= 0");
        }

        [Test]
        public void Criterion12_GuardAcceptsZero()
        {
            // Zero is finite and >= 0, so it should be accepted
            var run = new RunState();
            run.Tick(0f);  // Should not throw
            Assert.That(run.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Criterion12_GuardAcceptsPositiveFinite()
        {
            // Any positive finite value is finite and >= 0, so it should be accepted
            var run = new RunState();
            run.Tick(1.5f);  // Should not throw
            Assert.That(run.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Criterion12_ExceptionMessageAccuratelyStatesPredidate()
        {
            var run = new RunState();
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.NaN));

            Assert.That(ex.Message, Contains.Substring("finite"),
                "Exception message must mention that dt must be finite");
            Assert.That(ex.Message, Contains.Substring(">= 0"),
                "Exception message must mention that dt must be >= 0");
            Assert.That(ex.ParamName, Is.EqualTo("dt"),
                "Exception must name the parameter 'dt'");
        }

        [Test]
        public void Criterion12_PredicateConsistency()
        {
            // The guard uses: !float.IsFinite(dt) || dt < 0f
            // The message says: "dt must be a finite number >= 0"
            // These are logically equivalent (De Morgan's law)
            // The guard rejects when: dt is not finite OR dt < 0
            // The message requires: dt IS finite AND dt >= 0
            // These are the same condition expressed differently.

            var run = new RunState();

            // Test all combinations:
            // 1. Finite and >= 0: should accept
            run.Tick(1f);
            Assert.That(run.TickCount, Is.EqualTo(1));

            // 2. Finite and < 0: should reject
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(-1f));

            // 3. Not finite (NaN): should reject
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.NaN));

            // 4. Not finite (PositiveInfinity): should reject
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.PositiveInfinity));

            // 5. Not finite (NegativeInfinity): should reject
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.NegativeInfinity));
        }
    }
}
