using System;
using MergeSurvivor.Core.Run;
using NUnit.Framework;

namespace MergeSurvivor.Core.Tests
{
    /// <summary>
    /// Independent verification tests for WO-0009 acceptance criteria.
    /// WO-0009 makes RunState.RegisterKill atomic on its failure path: a rejected
    /// call must leave every field exactly as it found it, and the exception it
    /// throws must name RegisterKill's own parameter, not AddScore's.
    ///
    /// Every "unchanged" assertion below is made against a value captured from a
    /// non-default RunState immediately before the rejecting call, per RUL-0001
    /// matter 3(a): comparing only against a fresh RunState's defaults cannot
    /// distinguish "left unchanged" from "reset to zero".
    /// </summary>
    [TestFixture]
    public class WO0009CriteriaVerificationTests
    {
        // ============================================================================
        // Criterion 1: RegisterKill validates scoreValue before mutating Kills or Score
        // ============================================================================

        [Test]
        public void Criterion1_NegativeScoreValueThrowsWithoutIncrementingKills()
        {
            var run = new RunState();

            Assert.Throws<ArgumentOutOfRangeException>(() => run.RegisterKill(-1));

            Assert.That(run.Kills, Is.EqualTo(0), "A rejected RegisterKill must not increment Kills");
            Assert.That(run.Score, Is.EqualTo(0), "A rejected RegisterKill must not change Score");
        }

        // ============================================================================
        // Criterion 2: the exception's ParamName is "scoreValue", not "amount"
        // ============================================================================

        [Test]
        public void Criterion2_ExceptionParamNameIsScoreValueNotAmount()
        {
            var run = new RunState();

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => run.RegisterKill(-5));

            Assert.That(ex.ParamName, Is.EqualTo("scoreValue"),
                "RegisterKill's own exception must name its own parameter, scoreValue, not AddScore's amount");
        }

        // ============================================================================
        // Criterion 3: the RUL-0001 regression test -- non-default state, captured
        // locals, then a rejecting call, then exact equality against the captured
        // values (not against a fresh run's defaults).
        // ============================================================================

        [Test]
        public void Criterion3_RejectedRegisterKillLeavesNonDefaultStateExactlyUnchanged()
        {
            var run = new RunState();

            run.RegisterKill(10);
            run.RegisterKill(15);
            run.Tick(2.5f);

            var killsBefore = run.Kills;
            var scoreBefore = run.Score;
            var elapsedBefore = run.ElapsedSeconds;
            var tickCountBefore = run.TickCount;
            var isOverBefore = run.IsOver;

            Assert.That(killsBefore, Is.EqualTo(2));
            Assert.That(scoreBefore, Is.GreaterThan(0));
            Assert.That(elapsedBefore, Is.GreaterThan(0f));
            Assert.That(tickCountBefore, Is.EqualTo(1));
            Assert.That(isOverBefore, Is.False);

            Assert.Throws<ArgumentOutOfRangeException>(() => run.RegisterKill(-1));

            Assert.That(run.Kills, Is.EqualTo(killsBefore),
                "Kills must be exactly the captured pre-call value, not a fresh run's default");
            Assert.That(run.Score, Is.EqualTo(scoreBefore),
                "Score must be exactly the captured pre-call value, not a fresh run's default");
            Assert.That(run.ElapsedSeconds, Is.EqualTo(elapsedBefore),
                "ElapsedSeconds must be exactly the captured pre-call value, not a fresh run's default");
            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore),
                "TickCount must be exactly the captured pre-call value, not a fresh run's default");
            Assert.That(run.IsOver, Is.EqualTo(isOverBefore),
                "IsOver must be exactly the captured pre-call value, not a fresh run's default");
        }

        // ============================================================================
        // Criterion 4: RegisterKill(0) does not throw; increments Kills by 1 and
        // leaves Score unchanged (0 added is not a decrease).
        // ============================================================================

        [Test]
        public void Criterion4_RegisterKillWithZeroScoreValueIncrementsKillsAndDoesNotThrow()
        {
            var run = new RunState();
            run.AddScore(7);
            var scoreBefore = run.Score;

            Assert.DoesNotThrow(() => run.RegisterKill(0));

            Assert.That(run.Kills, Is.EqualTo(1));
            Assert.That(run.Score, Is.EqualTo(scoreBefore));
        }

        // ============================================================================
        // Criterion 5: for every valid scoreValue, Kills += 1 and Score += scoreValue,
        // via delegation to AddScore -- behaviour unchanged from before this fix.
        // ============================================================================

        [Test]
        public void Criterion5_ValidRegisterKillCallsIncrementKillsAndAddScoreByExactAmount()
        {
            var run = new RunState();

            run.RegisterKill(10);
            Assert.That(run.Kills, Is.EqualTo(1));
            Assert.That(run.Score, Is.EqualTo(10));

            run.RegisterKill(5);
            Assert.That(run.Kills, Is.EqualTo(2));
            Assert.That(run.Score, Is.EqualTo(15));
        }

        // ============================================================================
        // Criterion 6: RegisterKillRaisesBothKillsAndScore in RunStateTests.cs
        // continues to pass unmodified (verified by the suite run, not duplicated
        // here as a separate assertion against RunState internals).
        // ============================================================================

        // ============================================================================
        // Criterion 7: AddScore's own validation, message and ParamName stay
        // byte-for-byte untouched.
        // ============================================================================

        [Test]
        public void Criterion7_AddScoreCalledDirectlyStillThrowsWithAmountParamNameAndOriginalMessage()
        {
            var run = new RunState();

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => run.AddScore(-1));

            Assert.That(ex.ParamName, Is.EqualTo("amount"));
            Assert.That(ex.Message, Does.StartWith(
                "Score never decreases in this game; use a separate penalty concept if that changes."));
        }

        // ============================================================================
        // Criterion 8: RegisterKill's IsOver check remains the first statement,
        // ahead of the negative-value validation, so a negative scoreValue after
        // EndRun() still silently no-ops rather than throwing.
        // ============================================================================

        [Test]
        public void Criterion8_NegativeScoreValueAfterEndRunNoOpsWithoutThrowing()
        {
            var run = new RunState();
            run.RegisterKill(10);
            run.Tick(3f);
            run.EndRun();

            var killsBefore = run.Kills;
            var scoreBefore = run.Score;
            var elapsedBefore = run.ElapsedSeconds;
            var tickCountBefore = run.TickCount;

            Assert.DoesNotThrow(() => run.RegisterKill(-1),
                "IsOver must be checked before the negative-value validation, so a negative " +
                "scoreValue on an ended run is a silent no-op, not a throw");

            Assert.That(run.Kills, Is.EqualTo(killsBefore));
            Assert.That(run.Score, Is.EqualTo(scoreBefore));
            Assert.That(run.ElapsedSeconds, Is.EqualTo(elapsedBefore));
            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore));
        }

        // ============================================================================
        // Criterion 9: Criterion9_RegisterKillIncrementsKills,
        // Criterion9_RegisterKillAlsoAddsScore (WO0008CriteriaVerificationTests.cs)
        // and Criterion6_RegisterKillDoesNotChangeTickCountOrSeed
        // (RunStateVerificationTests.cs) continue to pass unmodified -- verified by
        // the suite run, not duplicated here.
        // ============================================================================

        // ============================================================================
        // Criterion 10: RegisterKill never changes TickCount, Seed or ElapsedSeconds
        // on any path, successful or throwing.
        // ============================================================================

        [Test]
        public void Criterion10_RegisterKillNeverChangesTickCountSeedOrElapsedSeconds()
        {
            var run = new RunState(77u);
            run.Tick(4f);
            var tickCountBefore = run.TickCount;
            var seedBefore = run.Seed;
            var elapsedBefore = run.ElapsedSeconds;

            run.RegisterKill(1);

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore));
            Assert.That(run.Seed, Is.EqualTo(seedBefore));
            Assert.That(run.ElapsedSeconds, Is.EqualTo(elapsedBefore));

            Assert.Throws<ArgumentOutOfRangeException>(() => run.RegisterKill(-1));

            Assert.That(run.TickCount, Is.EqualTo(tickCountBefore));
            Assert.That(run.Seed, Is.EqualTo(seedBefore));
            Assert.That(run.ElapsedSeconds, Is.EqualTo(elapsedBefore));
        }

        // ============================================================================
        // Criterion 11: the regression test for the defect itself. Against the
        // pre-fix RunState.cs (Kills++ executed before AddScore's guard runs), this
        // fails: the post-throw Kills observed is one more than the captured
        // pre-call value. Against the fix, RegisterKill validates before mutating,
        // so the post-throw Kills equals the pre-call value exactly.
        // ============================================================================

        [Test]
        public void Criterion11_RejectedRegisterKillDoesNotLeaveKillsIncrementedAcrossTheThrow()
        {
            var run = new RunState();
            run.RegisterKill(10);

            var killsBeforeRejectedCall = run.Kills;

            Assert.Throws<ArgumentOutOfRangeException>(() => run.RegisterKill(-1));

            var killsAfterRejectedCall = run.Kills;

            Assert.That(killsAfterRejectedCall, Is.EqualTo(killsBeforeRejectedCall),
                $"RegisterKill(-1) must not survive its own throw as an increment: " +
                $"observed Kills={killsBeforeRejectedCall} before the rejected call and " +
                $"Kills={killsAfterRejectedCall} after it.");
        }

        // ============================================================================
        // Criterion 12: RunState.cs contains no reference to UnityEngine, and
        // RegisterKill continues to take scoreValue as an explicit int parameter
        // with no ambient state read.
        // ============================================================================

        [Test]
        public void Criterion12_RegisterKillTakesExplicitScoreValueParameter()
        {
            var run = new RunState();

            // scoreValue is supplied explicitly by the caller; nothing about the
            // outcome depends on any ambient or hidden state.
            run.RegisterKill(scoreValue: 3);

            Assert.That(run.Kills, Is.EqualTo(1));
            Assert.That(run.Score, Is.EqualTo(3));
        }

        // ============================================================================
        // Criterion 13: the full suite builds and passes -- verified by running the
        // suite, not by an assertion in this file.
        // ============================================================================
    }
}
