using System.Collections.Generic;
using UnityEngine;

namespace MergeSurvivor.Unity
{
    /// <summary>
    /// A minimal reuse pool. Bullets and enemies are spawned every fraction of a second,
    /// and Instantiate/Destroy on that cadence is the single biggest source of GC spikes
    /// in this genre — which is what the T4 performance gate would otherwise flag.
    /// </summary>
    public sealed class SimplePool : MonoBehaviour
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private int prewarm = 32;

        private readonly Stack<GameObject> _idle = new Stack<GameObject>();

        private void Awake()
        {
            for (int i = 0; i < prewarm; i++)
            {
                GameObject instance = Instantiate(prefab, transform);
                instance.SetActive(false);
                _idle.Push(instance);
            }
        }

        public GameObject Get(Vector3 position)
        {
            GameObject instance = _idle.Count > 0
                ? _idle.Pop()
                : Instantiate(prefab, transform);

            instance.transform.position = position;
            instance.SetActive(true);
            return instance;
        }

        public void Return(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            instance.SetActive(false);
            _idle.Push(instance);
        }
    }
}
