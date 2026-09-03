using System.Collections.Generic;
using MergeSurvivor.Core.Rng;
using MergeSurvivor.Core.Spawning;
using UnityEngine;

namespace MergeSurvivor.Unity
{
    /// <summary>
    /// Adapter over <see cref="WaveScheduler"/>. Replaces the old
    /// InvokeRepeating("SpawnEnemy", ...) call, which referenced the method by string
    /// (so a rename broke it silently at runtime), could not be seeded, and lost spawns
    /// whenever a frame ran longer than the interval.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private SimplePool enemyPool;
        [SerializeField] private float firstSpawnDelay = 1f;
        [SerializeField] private float spawnInterval = 2f;
        [SerializeField] private float spawnHalfWidth = 8f;

        [Tooltip("Fixed seed keeps a run reproducible. Set 0 to randomise per session.")]
        [SerializeField] private int seed = 12345;

        private WaveScheduler _scheduler;

        // Reused across frames so a steady-state frame allocates nothing.
        private readonly List<SpawnRequest> _pending = new List<SpawnRequest>(8);

        private void Awake()
        {
            uint effectiveSeed = seed != 0
                ? (uint)seed
                : (uint)System.DateTime.UtcNow.Ticks;

            _scheduler = new WaveScheduler(
                new XorShiftRng(effectiveSeed),
                firstSpawnDelay,
                spawnInterval,
                spawnHalfWidth);
        }

        private void Update()
        {
            _pending.Clear();
            _scheduler.Tick(Time.deltaTime, _pending);

            for (int i = 0; i < _pending.Count; i++)
            {
                Spawn(_pending[i]);
            }
        }

        private void Spawn(SpawnRequest request)
        {
            if (enemyPool == null)
            {
                return;
            }

            enemyPool.Get(new Vector3(request.X, transform.position.y, 0f));
        }
    }
}
