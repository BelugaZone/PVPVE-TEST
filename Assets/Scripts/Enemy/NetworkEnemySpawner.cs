using UnityEngine;
using FishNet.Object;
using FishNet;

public class NetworkEnemySpawner : NetworkBehaviour
{
    [SerializeField] private GameObject enemyPrefab;

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (enemyPrefab != null)
        {
            GameObject spawnedEnemy = Instantiate(enemyPrefab, transform.position, transform.rotation);
            InstanceFinder.ServerManager.Spawn(spawnedEnemy);
            ServerLogger.LogSpawn($"Spawned enemy {spawnedEnemy.name} at {transform.position}");
        }

        // Destroy the spawner GameObject since its job is done
        Destroy(gameObject);
    }
}
