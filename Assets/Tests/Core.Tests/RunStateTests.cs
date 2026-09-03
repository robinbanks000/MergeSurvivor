using System;
using MergeSurvivor.Core.Run;
using NUnit.Framework;

namespace MergeSurvivor.Core.Tests
{
    [TestFixture]
    public class RunStateTests
    {
        [Test]
        public void StartsEmpty()
        {
            var run = new RunState();

            Assert.That(run.Score, Is.Zero);
            Assert.That(run.Kills, Is.Zero);
            Assert.That(run.ElapsedSeconds, Is.Zero);
            Assert.That(run.IsOver, Is.False);
        }

        [Test]
        public void TickAccumulatesElapsedTime()
        {
            var run = new RunState();

            run.Tick(0.5f);
            run.Tick(0.25f);

            Assert.That(run.ElapsedSeconds, Is.EqualTo(0.75f).Within(1e-4f));
        }

        [Test]
        public void RegisterKillRaisesBothKillsAndScore()
        {
            var run = new RunState();

            run.RegisterKill(scoreValue: 25);

            Assert.That(run.Kills, Is.EqualTo(1));
            Assert.That(run.Score, Is.EqualTo(25));
        }

        [Test]
        public void EndedRunStopsAccumulating()
        {
            // Kills resolving after death must not inflate the final score, which is
            // exactly the kind of thing a physics callback does one frame too late.
            var run = new RunState();
            run.RegisterKill(10);
            run.EndRun();

            run.Tick(5f);
            run.AddScore(100);
            run.RegisterKill(100);

            Assert.That(run.Score, Is.EqualTo(10));
            Assert.That(run.Kills, Is.EqualTo(1));
            Assert.That(run.ElapsedSeconds, Is.Zero);
        }

        [Test]
        public void RejectsNegativeScore()
        {
            var run = new RunState();

            Assert.Throws<ArgumentOutOfRangeException>(() => run.AddScore(-1));
        }

        [Test]
        public void RejectsNegativeDeltaTime()
        {
            var run = new RunState();

            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(-0.1f));
        }

        [Test]
        public void ResetClearsEverythingIncludingTheOverFlag()
        {
            var run = new RunState();
            run.RegisterKill(50);
            run.Tick(3f);
            run.EndRun();

            run.Reset();

            Assert.That(run.Score, Is.Zero);
            Assert.That(run.Kills, Is.Zero);
            Assert.That(run.ElapsedSeconds, Is.Zero);
            Assert.That(run.TickCount, Is.Zero);
            Assert.That(run.IsOver, Is.False);
        }

        [Test]
        public void SeedIsReturnedUnchangedForEveryValueIncludingZero()
        {
            Assert.That(new RunState(0u).Seed, Is.EqualTo(0u));
            Assert.That(new RunState(1u).Seed, Is.EqualTo(1u));
            Assert.That(new RunState(uint.MaxValue).Seed, Is.EqualTo(uint.MaxValue));
            Assert.That(new RunState(12345u).Seed, Is.EqualTo(12345u));
        }

        [Test]
        public void ParameterlessConstructorUsesFixedDocumentedDefaultSeed()
        {
            var first = new RunState();
            var second = new RunState();

            Assert.That(first.Seed, Is.EqualTo(RunState.DefaultSeed));
            Assert.That(second.Seed, Is.EqualTo(RunState.DefaultSeed));
        }

        [Test]
        public void NewRunStartsWithZeroTickCount()
        {
            var run = new RunState(7u);

            Assert.That(run.TickCount, Is.Zero);
        }

        [Test]
        public void TickIncrementsTickCountByExactlyOnePerCall()
        {
            var run = new RunState(7u);

            run.Tick(0.5f);
            run.Tick(0f);
            run.Tick(2f);

            Assert.That(run.TickCount, Is.EqualTo(3));
        }

        [Test]
        public void TickWithZeroDeltaTimeStillIncrementsTickCount()
        {
            var run = new RunState(7u);

            run.Tick(0f);

            Assert.That(run.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void NegativeDeltaTimeDoesNotIncrementTickCount()
        {
            var run = new RunState(7u);

            Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(-0.1f));

            Assert.That(run.TickCount, Is.Zero);
        }

        [Test]
        public void TickAfterEndRunDoesNotIncrementTickCount()
        {
            var run = new RunState(7u);
            run.Tick(1f);
            run.EndRun();

            run.Tick(1f);
            run.Tick(1f);

            Assert.That(run.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void AddScoreRegisterKillAndEndRunDoNotChangeTickCountOrSeed()
        {
            var run = new RunState(42u);
            run.Tick(1f);

            run.AddScore(10);
            run.RegisterKill(5);
            run.EndRun();

            Assert.That(run.TickCount, Is.EqualTo(1));
            Assert.That(run.Seed, Is.EqualTo(42u));
        }

        [Test]
        public void ResetReturnsTickCountToZeroButLeavesSeedUnchanged()
        {
            var run = new RunState(99u);
            run.Tick(1f);
            run.Tick(1f);
            run.EndRun();

            run.Reset();

            Assert.That(run.TickCount, Is.Zero);
            Assert.That(run.Seed, Is.EqualTo(99u));
        }

        [Test]
        public void SameSeedProducesSameSeedRegardlessOfCallHistory()
        {
            var untouched = new RunState(55u);

            var exercised = new RunState(55u);
            exercised.Tick(1f);
            exercised.AddScore(10);
            exercised.RegisterKill(5);
            exercised.EndRun();
            exercised.Reset();
            exercised.Tick(2f);

            Assert.That(exercised.Seed, Is.EqualTo(untouched.Seed));
        }
    }
}
