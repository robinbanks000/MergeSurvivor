using System;

namespace MergeSurvivor.Core.Run
{
    /// <summary>
    /// Score, kills and elapsed time for a single run. This is what the old
    /// GameManager singleton held; keeping it here means a run can be advanced ten
    /// thousand times in a simulation without a scene, a GameObject or an Update loop.
    /// </summary>
    public sealed class RunState
    {
        public int Score { get; private set; }

        public int Kills { get; private set; }

        public float ElapsedSeconds { get; private set; }

        public bool IsOver { get; private set; }

        /// <summary>
        /// Advances run time. Takes dt as a parameter rather than reading Time.deltaTime
        /// so a simulation can step at a fixed rate far faster than real time.
        /// </summary>
        public void Tick(float dt)
        {
            if (dt < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(dt), dt, "dt must be >= 0.");
            }

            if (IsOver)
            {
                return;
            }

            ElapsedSeconds += dt;
        }

        public void AddScore(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount), amount, "Score never decreases in this game; use a separate penalty concept if that changes.");
            }

            if (IsOver)
            {
                return;
            }

            Score += amount;
        }

        public void RegisterKill(int scoreValue)
        {
            if (IsOver)
            {
                return;
            }

            Kills++;
            AddScore(scoreValue);
        }

        public void EndRun() => IsOver = true;

        public void Reset()
        {
            Score = 0;
            Kills = 0;
            ElapsedSeconds = 0f;
            IsOver = false;
        }
    }
}
