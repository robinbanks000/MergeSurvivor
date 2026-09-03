using System;
using MergeSurvivor.Core.Run;
using NUnit.Framework;

namespace MergeSurvivor.Core.Tests
{
    /// <summary>
    /// Independent verification tests for RunState against WO-0007 acceptance criteria.
    /// These tests verify the specification (WO-0007.json), not just the implementation's own tests.
    /// </summary>
    [TestFixture]
    public class RunStateVerificationTests
    {
        // ============================================================================
        // Criterion 1: Seed value preservation for every value representable by uint
        // ============================================================================

        [Test]
        public void Criterion1_SeedZeroPreserved()
        {
            var run = new RunState(0u);
            Assert.That(run.Seed, Is.EqualTo(0u), "Seed 0 must be preserved exactly");
        }

        [Test]
        public void Criterion1_SeedOnePreserved()
        {
            var run = new RunState(1u);
            Assert.That(run.Seed, Is.EqualTo(1u), "Seed 1 must be preserved exactly");
        }

        [Test]
        public void Criterion1_SeedMaxValuePreserved()
        {
            var run = new RunState(uint.MaxValue);
            Assert.That(run.Seed, Is.EqualTo(uint.MaxValue), "Seed uint.MaxValue must be preserved exactly");
        }

        [Test]
        public void Criterion1_SeedRandomValuesPreserved()
        {
            uint[] seeds = { 42u, 999u, 12345u, 2147483647u, 2147483648u, 4000000000u };
            foreach (uint seed in seeds)
            {
                var run = new RunState(seed);
                Assert.That(run.Seed, Is.EqualTo(seed), $"Seed {seed} must be preserved exactly");
            }
        }

        // ============================================================================
        // Criterion 2: TickCount starts at zero
        // ============================================================================

        [Test]
        public void Criterion2_NewRunHasZeroTickCount()
        {
            var run = new RunState(12345u);
            Assert.That(run.TickCount, Is.EqualTo(0), "New RunState must have TickCount == 0");
        }

        [Test]
        public void Criterion2_ParameterlessConstructorHasZeroTickCount()
        {
            var run = new RunState();
            Assert.That(run.TickCount, Is.EqualTo(0), "Parameterless RunState must have TickCount == 0");
        }

        // ============================================================================
        // Criterion 3: TickCount increments by exactly 1 for every Tick call
        //             regardless of dt value (including dt == 0)
        // ============================================================================

        [Test]
        public void Criterion3_TickIncrementsCountByOne()
        {
            var run = new RunState();
            run.Tick(1.0f);
            Assert.That(run.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Criterion3_MultipleTicks()
        {
            var run = new RunState();
            for (int i = 0; i < 100; i++)
            {
                run.Tick(0.1f);
                Assert.That(run.TickCount, Is.EqualTo(i + 1), $"After tick {i + 1}, TickCount should be {i + 1}");
            }
        }

        [Test]
        public void Criterion3_ZeroDtStillIncrements()
        {
            var run = new RunState();
            run.Tick(0f);
            Assert.That(run.TickCount, Is.EqualTo(1), "Tick(0) must still increment TickCount by 1");
        }

        [Test]
        public void Criterion3_VerySmallPositiveDtIncrements()
        {
            var run = new RunState();
            run.Tick(0.0001f);
            Assert.That(run.TickCount, Is.EqualTo(1), "Tick with very small positive dt must increment");
        }

        [Test]
        public void Criterion3_LargeDtIncrements()
        {
            var run = new RunState();
            run.Tick(1000000f);
            Assert.That(run.TickCount, Is.EqualTo(1), "Tick with large dt must increment");
        }

        [Test]
        public void Criterion3_VeryLargeDtIncrements()
        {
            var run = new RunState();
            run.Tick(float.MaxValue);
            Assert.That(run.TickCount, Is.EqualTo(1), "Tick with float.MaxValue dt must increment");
        }

        [Test]
        public void Criterion3_PositiveInfinityDtThrowsAndLeavesStateUnchanged()
        {
            // The specification requires dt to be a finite, non-negative number.
            // float.PositiveInfinity is not finite, so Tick must reject it and
            // leave TickCount and ElapsedSeconds exactly as they were.
            var run = new RunState();
            var tickCountBefore = run.TickCount;
            var elapsedBefore = run.ElapsedSeconds;

            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.PositiveInfinity));

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore),
                "Tick with float.PositiveInfinity dt must not increment TickCount");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(elapsedBefore),
                "Tick with float.PositiveInfinity dt must not change ElapsedSeconds");
        }

        [Test]
        public void Criterion3_NaNDtThrowsAndLeavesStateUnchanged()
        {
            // The specification requires dt to be a finite, non-negative number.
            // float.NaN is not finite, so Tick must reject it and leave TickCount
            // and ElapsedSeconds exactly as they were.
            var run = new RunState();
            var tickCountBefore = run.TickCount;
            var elapsedBefore = run.ElapsedSeconds;

            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.NaN));

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore),
                "Tick with float.NaN dt must not increment TickCount");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(elapsedBefore),
                "Tick with float.NaN dt must not change ElapsedSeconds");
        }

        // ============================================================================
        // Regression: a non-finite dt must never reach ElapsedSeconds. Tick(dt) is
        // specified to reject any dt for which dt >= 0f does not hold, or which is
        // an infinity; a caller that catches the resulting exception must find
        // ElapsedSeconds exactly as it was before the call.
        // ============================================================================

        [Test]
        public void Regression_NaNDtLeavesElapsedSecondsFiniteAndUnchanged()
        {
            var run = new RunState();

            try
            {
                run.Tick(float.NaN);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Expected: the specification requires Tick to reject this dt.
            }

            Assert.That(float.IsFinite(run.ElapsedSeconds), Is.True,
                "ElapsedSeconds must remain finite after a rejected Tick call");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(0f),
                "ElapsedSeconds must be unchanged from its pre-call value after a rejected Tick call");
        }

        [Test]
        public void Criterion3_ManyTicksWithVariedDt()
        {
            var run = new RunState();
            float[] dtValues = { 0f, 0.1f, 1000f, float.MaxValue };
            foreach (float dt in dtValues)
            {
                run.Tick(dt);
            }
            Assert.That(run.TickCount, Is.EqualTo(dtValues.Length),
                "TickCount must increment for every Tick call with a finite, non-negative dt");
        }

        [Test]
        public void Criterion3_ManyTicksWithVariedDt_NonFiniteEntriesThrowAndDoNotIncrement()
        {
            var run = new RunState();
            float[] nonFiniteDtValues = { float.PositiveInfinity, float.NaN };
            foreach (float dt in nonFiniteDtValues)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(dt));
            }
            Assert.That(run.TickCount, Is.EqualTo(0),
                "TickCount must not increment for any non-finite dt");
        }

        // ============================================================================
        // Criterion 4: Negative dt throws ArgumentOutOfRangeException and doesn't increment
        // ============================================================================

        [Test]
        public void Criterion4_NegativeDtThrows()
        {
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(-0.1f));
        }

        [Test]
        public void Criterion4_NegativeInfinityThrows()
        {
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(float.NegativeInfinity));
        }

        [Test]
        public void Criterion4_NegativeDtDoesNotIncrement()
        {
            var run = new RunState();
            try
            {
                run.Tick(-0.5f);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Expected
            }
            Assert.That(run.TickCount, Is.EqualTo(0), "Negative dt must not increment TickCount");
        }

        // ============================================================================
        // Criterion 5: Tick after EndRun doesn't increment TickCount
        // ============================================================================

        [Test]
        public void Criterion5_TickAfterEndRunIsNoop()
        {
            var run = new RunState();
            run.Tick(1f);
            Assert.That(run.TickCount, Is.EqualTo(1));

            run.EndRun();
            run.Tick(1f);
            Assert.That(run.TickCount, Is.EqualTo(1), "Tick after EndRun must not increment TickCount");
        }

        [Test]
        public void Criterion5_MultipleTicksAfterEndRun()
        {
            var run = new RunState();
            run.Tick(1f);
            run.EndRun();

            run.Tick(1f);
            run.Tick(2f);
            run.Tick(3f);

            Assert.That(run.TickCount, Is.EqualTo(1), "Multiple Ticks after EndRun must not increment");
        }

        // ============================================================================
        // Criterion 6: AddScore, RegisterKill, EndRun never change TickCount or Seed
        // ============================================================================

        [Test]
        public void Criterion6_AddScoreDoesNotChangeTickCountOrSeed()
        {
            var run = new RunState(42u);
            run.Tick(1f);
            var tickCountBefore = run.TickCount;
            var seedBefore = run.Seed;

            run.AddScore(100);

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore), "AddScore must not change TickCount");
            Assert.That(run.Seed, Is.EqualTo(seedBefore), "AddScore must not change Seed");
        }

        [Test]
        public void Criterion6_RegisterKillDoesNotChangeTickCountOrSeed()
        {
            var run = new RunState(42u);
            run.Tick(1f);
            var tickCountBefore = run.TickCount;
            var seedBefore = run.Seed;

            run.RegisterKill(50);

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore), "RegisterKill must not change TickCount");
            Assert.That(run.Seed, Is.EqualTo(seedBefore), "RegisterKill must not change Seed");
        }

        [Test]
        public void Criterion6_EndRunDoesNotChangeTickCountOrSeed()
        {
            var run = new RunState(42u);
            run.Tick(1f);
            var tickCountBefore = run.TickCount;
            var seedBefore = run.Seed;

            run.EndRun();

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore), "EndRun must not change TickCount");
            Assert.That(run.Seed, Is.EqualTo(seedBefore), "EndRun must not change Seed");
        }

        [Test]
        public void Criterion6_AllThreeMethodsTogether()
        {
            var run = new RunState(99u);
            run.Tick(2f);
            run.Tick(3f);
            var tickCountBefore = run.TickCount;
            var seedBefore = run.Seed;

            run.AddScore(10);
            run.RegisterKill(5);
            run.EndRun();

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore),
                "AddScore, RegisterKill, and EndRun must not change TickCount");
            Assert.That(run.Seed, Is.EqualTo(seedBefore),
                "AddScore, RegisterKill, and EndRun must not change Seed");
        }

        // ============================================================================
        // Criterion 7: Reset returns TickCount to zero, but Seed is unchanged
        // ============================================================================

        [Test]
        public void Criterion7_ResetReturnsTickCountToZero()
        {
            var run = new RunState(55u);
            run.Tick(1f);
            run.Tick(2f);
            run.Tick(3f);
            Assert.That(run.TickCount, Is.EqualTo(3));

            run.Reset();
            Assert.That(run.TickCount, Is.EqualTo(0), "Reset must return TickCount to 0");
        }

        [Test]
        public void Criterion7_ResetPreservesSeed()
        {
            var run = new RunState(77u);
            run.Tick(1f);
            run.AddScore(100);
            run.RegisterKill(10);
            run.EndRun();

            run.Reset();

            Assert.That(run.Seed, Is.EqualTo(77u), "Reset must not change Seed");
        }

        [Test]
        public void Criterion7_ResetAfterEndRunThenTick()
        {
            // This tests the interaction between criteria 5 and 7
            var run = new RunState(88u);
            run.Tick(1f);
            run.EndRun();

            // Tick doesn't work after EndRun
            run.Tick(1f);
            Assert.That(run.TickCount, Is.EqualTo(1));

            // Reset clears TickCount and IsOver
            run.Reset();
            Assert.That(run.TickCount, Is.EqualTo(0));
            Assert.That(run.Seed, Is.EqualTo(88u));

            // Now Tick should work again
            run.Tick(1f);
            Assert.That(run.TickCount, Is.EqualTo(1),
                "After Reset following EndRun, Tick should work and increment TickCount");
        }

        // ============================================================================
        // Criterion 8: Two instances with same seed report same Seed regardless of history
        // ============================================================================

        [Test]
        public void Criterion8_SameSeedWithDifferentOperations()
        {
            var run1 = new RunState(123u);

            var run2 = new RunState(123u);
            run2.Tick(1f);
            run2.AddScore(10);
            run2.RegisterKill(5);
            run2.EndRun();
            run2.Reset();
            run2.Tick(2f);

            Assert.That(run1.Seed, Is.EqualTo(run2.Seed),
                "Two instances with same seed must report same Seed regardless of call history");
        }

        [Test]
        public void Criterion8_DifferentInstancesCannotAffectEachOthersSeed()
        {
            var run1 = new RunState(456u);
            var run2 = new RunState(456u);

            run1.Tick(100f);
            run1.AddScore(1000);
            run1.RegisterKill(50);
            run1.EndRun();

            // run2 has never been called, but should still have same seed as run1
            Assert.That(run2.Seed, Is.EqualTo(run1.Seed));
        }

        // ============================================================================
        // Criterion 9: Seed is deterministic, parameterless constructor uses DefaultSeed
        // ============================================================================

        [Test]
        public void Criterion9_ParameterlessConstructorUsesDefaultSeed()
        {
            var run1 = new RunState();
            var run2 = new RunState();

            Assert.That(run1.Seed, Is.EqualTo(RunState.DefaultSeed));
            Assert.That(run2.Seed, Is.EqualTo(RunState.DefaultSeed));
            Assert.That(run1.Seed, Is.EqualTo(run2.Seed),
                "Parameterless constructor must use fixed DefaultSeed constant");
        }

        [Test]
        public void Criterion9_DefaultSeedIsDocumentedAndFixed()
        {
            // This verifies that DefaultSeed is a public constant that we can reference
            Assert.That(RunState.DefaultSeed, Is.InstanceOf<uint>());
            Assert.That(RunState.DefaultSeed, Is.EqualTo(1u), "DefaultSeed should be 1u as documented");
        }

        // ============================================================================
        // Criterion 10: Public parameterless constructor exists
        // ============================================================================

        [Test]
        public void Criterion10_ParameterlessConstructorExists()
        {
            // This just verifies the constructor is accessible and works
            var run = new RunState();
            Assert.That(run, Is.Not.Null);
        }

        // ============================================================================
        // Criterion 11: Pre-existing RunState contract is unchanged
        // ============================================================================

        [Test]
        public void Criterion11_ScoreAccumulation()
        {
            var run = new RunState();
            run.AddScore(10);
            run.AddScore(20);
            Assert.That(run.Score, Is.EqualTo(30));
        }

        [Test]
        public void Criterion11_KillsAccumulation()
        {
            var run = new RunState();
            run.RegisterKill(5);
            run.RegisterKill(10);
            Assert.That(run.Kills, Is.EqualTo(2));
        }

        [Test]
        public void Criterion11_ElapsedSecondsAccumulation()
        {
            var run = new RunState();
            run.Tick(0.5f);
            run.Tick(0.3f);
            Assert.That(run.ElapsedSeconds, Is.EqualTo(0.8f).Within(1e-4f));
        }

        [Test]
        public void Criterion11_EndRunStopsAccumulation()
        {
            var run = new RunState();
            run.AddScore(10);
            run.EndRun();

            run.AddScore(100);
            Assert.That(run.Score, Is.EqualTo(10), "After EndRun, AddScore should be no-op");
        }

        [Test]
        public void Criterion11_ResetClearsAllExceptSeed()
        {
            var run = new RunState(111u);
            run.AddScore(50);
            run.RegisterKill(5);
            run.Tick(2f);
            run.EndRun();

            run.Reset();

            Assert.That(run.Score, Is.EqualTo(0), "Reset must clear Score");
            Assert.That(run.Kills, Is.EqualTo(0), "Reset must clear Kills");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(0f), "Reset must clear ElapsedSeconds");
            Assert.That(run.IsOver, Is.False, "Reset must clear IsOver");
            Assert.That(run.TickCount, Is.EqualTo(0), "Reset must clear TickCount");
            Assert.That(run.Seed, Is.EqualTo(111u), "Reset must preserve Seed");
        }

        [Test]
        public void Criterion11_NegativeScoreThrows()
        {
            var run = new RunState();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.AddScore(-1));
        }

        // ============================================================================
        // Criterion 12: No UnityEngine reference, dt is explicit parameter
        // ============================================================================

        [Test]
        public void Criterion12_TickTakesDtAsParameter()
        {
            // This verifies that Tick takes dt as an explicit parameter
            // (by verifying the method exists with the correct signature)
            var run = new RunState();
            run.Tick(1.0f);  // Must take explicit float parameter
            Assert.That(run.TickCount, Is.EqualTo(1));
        }

        // ============================================================================
        // Additional edge case tests
        // ============================================================================

        [Test]
        public void EdgeCase_VeryManyTicks()
        {
            var run = new RunState();
            long expectedCount = 0;

            for (int i = 0; i < 10000; i++)
            {
                run.Tick(0.001f);
                expectedCount++;
            }

            Assert.That(run.TickCount, Is.EqualTo(expectedCount),
                "TickCount must accurately count even with many ticks");
        }

        [Test]
        public void EdgeCase_TickCountDoesNotOverflow()
        {
            // Verify TickCount is long (not int), so it can handle many ticks
            var run = new RunState();
            Assert.That(run.TickCount, Is.TypeOf<long>());
        }

        [Test]
        public void EdgeCase_ZeroDtDoesNotCauseIssues()
        {
            var run = new RunState();
            run.Tick(0f);
            run.Tick(0f);
            run.Tick(0f);
            run.Tick(0.5f);

            Assert.That(run.TickCount, Is.EqualTo(4));
            Assert.That(run.ElapsedSeconds, Is.EqualTo(0.5f).Within(1e-4f));
        }
    }
}
