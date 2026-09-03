namespace MergeSurvivor.Core.Spawning
{
    /// <summary>
    /// A decision to spawn, not a spawned thing. Core says "an enemy of this tier belongs
    /// at this x"; the Unity shell decides which prefab that is and pulls it from a pool.
    /// </summary>
    public readonly struct SpawnRequest
    {
        public SpawnRequest(float x, int enemyTier)
        {
            X = x;
            EnemyTier = enemyTier;
        }

        public float X { get; }

        public int EnemyTier { get; }

        public override string ToString() => $"SpawnRequest(x={X}, tier={EnemyTier})";
    }
}
