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
        /// <summary>
        /// The seed used when no seed is supplied explicitly. A fixed, documented
        /// constant rather than wall-clock time or a global counter, so a run created
        /// through the parameterless constructor is exactly as reproducible as one
        /// created with an explicit seed -- it is simply less varied.
        /// </summary>
        public const uint DefaultSeed = 1u;

        /// <summary>
        /// This run's identity. Set once at construction and never remapped or
        /// substituted -- unlike XorShiftRng, which remaps a zero seed because a zero
        /// xorshift state is a fixed point. That is an RNG-algorithm concern; here,
        /// Seed is an identity, so it must return exactly what was passed, including
        /// zero, so a bug can be reported and replayed as "seed X at tick Y".
        /// </summary>
        public uint Seed { get; }

        public int Score { get; private set; }

        public int Kills { get; private set; }

        public float ElapsedSeconds { get; private set; }

        /// <summary>
        /// A direct count of completed Tick(dt) calls. Never derived from
        /// ElapsedSeconds or any other float-accumulated value, since that would
        /// reintroduce the lossy accumulation this counter exists to avoid.
        /// </summary>
        public long TickCount { get; private set; }

        public bool IsOver { get; private set; }

        /// <summary>
        /// Constructs a run with the fixed, documented default seed. Kept so
        /// existing call sites -- notably Assets/Unity/GameManager.cs -- keep
        /// compiling with `new RunState()`. The seeded constructor below is the
        /// primary API; this is a compatibility shim, not the encouraged path.
        /// </summary>
        public RunState() : this(DefaultSeed)
        {
        }

        /// <summary>
        /// Constructs a run with an explicit seed. uint matches XorShiftRng's
        /// constructor exactly, so a run's seed can be handed to the RNG with no
        /// conversion or range question.
        /// </summary>
        public RunState(uint seed)
        {
            Seed = seed;
        }

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
            TickCount++;
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
            TickCount = 0;
            IsOver = false;
            // Seed is deliberately left untouched: it identifies the run across a
            // Reset, and two RunState instances constructed with the same seed must
            // report the same Seed regardless of call history.
        }
    }
}
