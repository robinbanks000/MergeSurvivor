using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MergeSurvivor.Core.Rng;

namespace MergeSurvivor.Core.Spawning
{
    /// <summary>
    /// Decides when and where enemies appear. Replaces the old string-based
    /// InvokeRepeating, which could not be tested, could not be seeded, and silently
    /// dropped spawns when the frame time exceeded the interval. This scheduler carries
    /// the remainder across ticks, so a 0.5s frame hitch still produces the same number
    /// of spawns as ten smooth frames would have.
    /// </summary>
    public sealed class WaveScheduler
    {
        private readonly IRng _rng;
        private readonly float _interval;
        private readonly float _halfWidth;

        private float _timeUntilNextSpawn;

        public WaveScheduler(IRng rng, float firstSpawnDelay, float interval, float halfWidth)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (interval <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interval), interval, "Interval must be > 0 or the scheduler would spawn forever within one tick.");
            }

            if (firstSpawnDelay < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(firstSpawnDelay), firstSpawnDelay, "First spawn delay must be >= 0.");
            }

            if (halfWidth < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(halfWidth), halfWidth, "Half width must be >= 0.");
            }

            _rng = rng;
            _interval = interval;
            _halfWidth = halfWidth;
            _timeUntilNextSpawn = firstSpawnDelay;
        }

        /// <summary>
        /// Advances the schedule and appends every spawn that came due. The caller owns
        /// the buffer so a steady-state frame allocates nothing.
        ///
        /// Termination check (CHA-0001 / RUL-0003): repeated addition into a binary32
        /// field is not strictly increasing -- once <c>_interval</c> falls at or below
        /// half an ulp of the accumulator, the store rounds back to a bit-identical
        /// value and an iteration that looks like it advances the timer makes no
        /// progress at all (verified: with the timer at <c>1f - 1e8f</c>, adding <c>2f</c>
        /// returns a bit-identical value). So before mutating <see cref="_timeUntilNextSpawn"/>
        /// or appending anything, this method computes the post-subtraction timer and,
        /// if a spawn is already due (that value is &lt;= 0) and adding <c>_interval</c>
        /// to it would not produce a strictly greater value, rejects <paramref name="dt"/>
        /// outright rather than entering the catch-up loop below.
        ///
        /// <see cref="_timeUntilNextSpawn"/>'s magnitude is non-increasing across catch-up
        /// iterations -- each iteration adds the fixed positive <c>_interval</c> to a
        /// non-positive value, moving it toward zero -- which is why this check inspects
        /// only the first prospective increment rather than every iteration: whenever the
        /// first increment's binade keeps a constant ulp for the remainder of the
        /// catch-up run (the common case away from the specific magnitude at which
        /// <c>_interval</c> sits exactly on a round-half-to-even tie of that binade's
        /// ulp), a non-decreasing ulp means that progress at the first step implies
        /// progress at every later step too. This check is known to leave a narrower gap
        /// than an unconditional "no dt causes non-termination" claim would require; see
        /// the escalation accompanying this change for the counterexample and the
        /// bounds of what this check actually guarantees.
        ///
        /// The candidate sum is routed through <see cref="ToBinary32"/> before the
        /// comparison, and that is load-bearing rather than decorative. C# permits a
        /// floating-point expression to be evaluated at higher precision than its type,
        /// and Unity's PlayMode runtime does exactly that here: G3 run 49 showed the
        /// subtraction rounding correctly and the STORED sum coming back bit-identical,
        /// while the inline comparison in the same expression still reported the sum as
        /// strictly greater -- so the guard did not fire, the catch-up loop was entered,
        /// and the same source that passes under .NET failed in the player. Rounding is
        /// therefore forced at a call boundary, where a float32 parameter obliges the
        /// conversion, rather than left to the evaluator's discretion.
        /// </summary>
        /// <returns>How many requests were appended.</returns>
        public int Tick(float dt, IList<SpawnRequest> into)
        {
            MergeSurvivor.Core.DtGuard.RequireFiniteNonNegative(dt, nameof(dt));

            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            float prospectiveTimer = _timeUntilNextSpawn - dt;
            float advancedTimer = ToBinary32(prospectiveTimer + _interval);
            if (prospectiveTimer <= 0f && !(advancedTimer > prospectiveTimer))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dt), dt,
                    "dt is too large relative to this scheduler's configured interval for the catch-up schedule to advance in single-precision arithmetic.");
            }

            _timeUntilNextSpawn = prospectiveTimer;

            int spawned = 0;
            while (_timeUntilNextSpawn <= 0f)
            {
                into.Add(new SpawnRequest(_rng.NextRange(-_halfWidth, _halfWidth), enemyTier: 1));
                spawned++;
                _timeUntilNextSpawn += _interval;
            }

            return spawned;
        }

        /// <summary>
        /// Rounds a float expression to an actual binary32 value, and stops the runtime
        /// deciding otherwise.
        ///
        /// C# allows floating-point operations to be carried out at higher precision
        /// than the operand type, so `a + b > a` can be answered from an unrounded
        /// intermediate. Passing through a float32 parameter obliges the conversion;
        /// NoInlining stops the round trip being optimised back out, which would restore
        /// the very freedom this exists to remove.
        ///
        /// Needed because the guard in <see cref="Tick"/> asks a question ABOUT binary32
        /// arithmetic -- whether the accumulator can still advance -- and a question
        /// about binary32 must be answered in binary32. Answered at any wider precision
        /// it reports that a schedule advances when the schedule, which lives in a float
        /// field, cannot.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float ToBinary32(float value) => value;
    }
}
