using MergeSurvivor.Core.Run;
using UnityEngine;

namespace MergeSurvivor.Unity
{
    /// <summary>
    /// Adapter over <see cref="RunState"/>. All the scoring rules live in Core; this
    /// class only owns the singleton lifetime and pumps real frame time into the run.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>The run being played. Read it for score, kills and elapsed time.</summary>
        public RunState Run { get; } = new RunState();

        private void Awake()
        {
            // The original guard only assigned when null, so a second GameManager in a
            // scene left a live duplicate ticking its own score. Destroy it instead.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            // Without this the static field keeps pointing at a destroyed object after a
            // scene reload, and every access throws the "object has been destroyed" error.
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            Run.Tick(Time.deltaTime);
        }

        public void AddScore(int amount) => Run.AddScore(amount);

        public void RegisterKill(int scoreValue) => Run.RegisterKill(scoreValue);

        public void EndRun() => Run.EndRun();
    }
}
