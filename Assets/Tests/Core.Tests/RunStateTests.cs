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
            Assert.That(run.IsOver, Is.False);
        }
    }
}
