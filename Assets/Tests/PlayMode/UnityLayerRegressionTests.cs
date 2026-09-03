using System.Collections;
using System.Reflection;
using MergeSurvivor.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MergeSurvivor.PlayMode.Tests
{
    /// <summary>
    /// Regression tests for the four defects found when porting the original scripts
    /// in Phase 0. Each was fixed at the time but shipped without a test, which breaks
    /// the ratchet rule: a fix without a regression test leaves the bug free to return.
    /// This file pays that debt.
    ///
    /// PlayMode rather than EditMode because every one of these depends on Awake,
    /// OnDestroy or Update actually running.
    /// </summary>
    [TestFixture]
    public class UnityLayerRegressionTests
    {
        /// <summary>
        /// Builds a component with its [SerializeField] fields already populated.
        /// The object is created inactive so the fields are in place before Awake
        /// runs — setting them afterwards would be too late.
        /// </summary>
        private static T CreateConfigured<T>(string name, params (string field, object value)[] fields)
            where T : Component
        {
            var go = new GameObject(name);
            go.SetActive(false);

            T component = go.AddComponent<T>();

            foreach ((string field, object value) in fields)
            {
                FieldInfo info = typeof(T).GetField(
                    field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                Assert.That(info, Is.Not.Null, $"{typeof(T).Name} has no field '{field}'.");
                info.SetValue(component, value);
            }

            go.SetActive(true);
            return component;
        }

        private static GameObject NewPrefab(string name)
        {
            var prefab = new GameObject(name);
            prefab.SetActive(false);
            return prefab;
        }

        [TearDown]
        public void TearDown()
        {
            // GameManager.Instance is static, so a leaked instance would silently change
            // the outcome of the next test in the run.
            foreach (GameManager leftover in Object.FindObjectsByType<GameManager>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(leftover.gameObject);
            }
        }

        // ---- Defect 1: singleton left live duplicates ----

        [UnityTest]
        public IEnumerator DuplicateGameManagerIsDestroyedAndTheOriginalSurvives()
        {
            var first = new GameObject("gm-1").AddComponent<GameManager>();
            yield return null;

            var second = new GameObject("gm-2").AddComponent<GameManager>();
            yield return null;

            Assert.That(GameManager.Instance, Is.EqualTo(first),
                "The first GameManager must remain the instance.");
            Assert.That(second == null, Is.True,
                "The duplicate must be destroyed; the original guard only skipped assignment and left it ticking.");
        }

        // ---- Defect 2: static reference dangled after scene reload ----

        [UnityTest]
        public IEnumerator InstanceIsClearedWhenTheGameManagerIsDestroyed()
        {
            var manager = new GameObject("gm").AddComponent<GameManager>();
            yield return null;
            Assert.That(GameManager.Instance, Is.EqualTo(manager));

            Object.DestroyImmediate(manager.gameObject);
            yield return null;

            Assert.That(GameManager.Instance == null, Is.True,
                "A stale static reference makes every access after a scene reload throw.");
        }

        [UnityTest]
        public IEnumerator ScoreFlowsThroughToTheCoreRunState()
        {
            var manager = new GameObject("gm").AddComponent<GameManager>();
            yield return null;

            manager.RegisterKill(scoreValue: 25);

            Assert.That(manager.Run.Kills, Is.EqualTo(1));
            Assert.That(manager.Run.Score, Is.EqualTo(25));
        }

        // ---- Defect 3: unpooled Instantiate every spawn ----

        [UnityTest]
        public IEnumerator PoolReusesAReturnedInstanceInsteadOfInstantiating()
        {
            GameObject prefab = NewPrefab("bullet");
            SimplePool pool = CreateConfigured<SimplePool>("pool", ("prefab", prefab), ("prewarm", 1));
            yield return null;

            GameObject first = pool.Get(Vector3.zero);
            pool.Return(first);
            GameObject second = pool.Get(Vector3.one);

            Assert.That(second, Is.SameAs(first),
                "A returned instance must come back out; Instantiate/Destroy at this cadence is the genre's biggest GC spike.");
            Assert.That(second.activeSelf, Is.True);

            Object.DestroyImmediate(pool.gameObject);
            Object.DestroyImmediate(prefab);
        }

        [UnityTest]
        public IEnumerator PrewarmCreatesInactiveInstancesUpFront()
        {
            GameObject prefab = NewPrefab("enemy");
            SimplePool pool = CreateConfigured<SimplePool>("pool", ("prefab", prefab), ("prewarm", 3));
            yield return null;

            Assert.That(pool.transform.childCount, Is.EqualTo(3));
            foreach (Transform child in pool.transform)
            {
                Assert.That(child.gameObject.activeSelf, Is.False);
            }

            Object.DestroyImmediate(pool.gameObject);
            Object.DestroyImmediate(prefab);
        }

        [UnityTest]
        public IEnumerator AnExhaustedPoolStillReturnsAnInstance()
        {
            GameObject prefab = NewPrefab("bullet");
            SimplePool pool = CreateConfigured<SimplePool>("pool", ("prefab", prefab), ("prewarm", 0));
            yield return null;

            GameObject spawned = pool.Get(Vector3.zero);

            Assert.That(spawned, Is.Not.Null, "Running dry must grow the pool, not return null mid-fight.");
            Assert.That(spawned.activeSelf, Is.True);

            Object.DestroyImmediate(pool.gameObject);
            Object.DestroyImmediate(prefab);
        }

        [UnityTest]
        public IEnumerator ReturningNullIsIgnoredRatherThanThrowing()
        {
            GameObject prefab = NewPrefab("bullet");
            SimplePool pool = CreateConfigured<SimplePool>("pool", ("prefab", prefab), ("prewarm", 1));
            yield return null;

            Assert.DoesNotThrow(() => pool.Return(null));

            Object.DestroyImmediate(pool.gameObject);
            Object.DestroyImmediate(prefab);
        }

        // ---- Defect 4: string-based InvokeRepeating dropped spawns ----

        [UnityTest]
        public IEnumerator SpawnerDrivesThePoolOnceTheFirstDelayHasElapsed()
        {
            GameObject prefab = NewPrefab("enemy");
            SimplePool pool = CreateConfigured<SimplePool>("pool", ("prefab", prefab), ("prewarm", 4));

            SimplePool spawnerPool = pool;
            EnemySpawner spawner = CreateConfigured<EnemySpawner>(
                "spawner",
                ("enemyPool", spawnerPool),
                ("firstSpawnDelay", 0.01f),
                ("spawnInterval", 0.01f),
                ("spawnHalfWidth", 8f),
                ("seed", 12345));

            // Real frame time drives Update, so wait on elapsed seconds rather than a
            // fixed frame count — asserting an exact spawn count here would be flaky.
            float waited = 0f;
            while (waited < 0.5f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            int active = 0;
            foreach (Transform child in pool.transform)
            {
                if (child.gameObject.activeSelf)
                {
                    active++;
                }
            }

            Assert.That(active, Is.GreaterThan(0),
                "The adapter must feed WaveScheduler's requests into the pool.");

            Object.DestroyImmediate(spawner.gameObject);
            Object.DestroyImmediate(pool.gameObject);
            Object.DestroyImmediate(prefab);
        }
    }
}
