using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    public GameObject enemyPrefab;
    public float spawnInterval = 2f;
    public float spawnRange = 8f;

    void Start()
    {
        InvokeRepeating("SpawnEnemy", 1f, spawnInterval);
    }

    void SpawnEnemy()
    {
        float x = Random.Range(-spawnRange, spawnRange);
        Vector3 pos = new Vector3(x, transform.position.y, 0);
        Instantiate(enemyPrefab, pos, Quaternion.identity);
    }
}
