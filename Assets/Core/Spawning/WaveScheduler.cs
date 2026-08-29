using System;
using System.Collections.Generic;
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
        /// </summary>
        /// <returns>How many requests were appended.</returns>
        public int Tick(float dt, IList<SpawnRequest> into)
        {
            MergeSurvivor.Core.DtGuard.RequireFiniteNonNegative(dt, nameof(dt));

            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            _timeUntilNextSpawn -= dt;

            int spawned = 0;
            while (_timeUntilNextSpawn <= 0f)
            {
                into.Add(new SpawnRequest(_rng.NextRange(-_halfWidth, _halfWidth), enemyTier: 1));
                spawned++;
                _timeUntilNextSpawn += _interval;
            }

            return spawned;
        }
    }
}
