using FishNet;
using FishNet.Connection;
using FishNet.Object;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ServerDataManager : NetworkBehaviour
{
    [Tooltip("The player prefab to spawn for authenticated clients.")]
    public NetworkObject playerPrefab;

    [Tooltip("Default spawn point for new players.")]
    public Transform defaultSpawnPoint;

    private Dictionary<string, PlayerSaveData> _playerDataMap = new Dictionary<string, PlayerSaveData>();
    private string _saveFilePath;

    private CustomAuthenticator _authenticator;

    public static ServerDataManager Instance;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        _saveFilePath = Path.Combine(Application.persistentDataPath, "server_players_data.json");
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        LoadAllData();

        _authenticator = FindObjectOfType<CustomAuthenticator>();
        if (_authenticator == null)
        {
            Debug.LogError("[ServerDataManager] Could not find CustomAuthenticator in scene!");
        }

        // Listen for when a client finishes loading the scene so we can spawn them
        base.NetworkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;
        
        // Listen for disconnects to save their state
        base.NetworkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (base.NetworkManager != null)
        {
            base.NetworkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
            base.NetworkManager.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
        }
        SaveAllData();
    }

    private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
    {
        if (!asServer) return; // Only run this logic on the server

        if (_authenticator == null) return;

        string playerID = _authenticator.GetIDForConnection(conn);
        if (string.IsNullOrEmpty(playerID))
        {
            Debug.LogWarning($"[ServerDataManager] Connection {conn.ClientId} has no authenticated ID. Cannot spawn player.");
            return;
        }

        // FIX: When connecting from a Clone project in Editor without Build Settings synced, 
        // FishNet may fail to add the client connection to the server's scene.
        // Explicitly adding the connection to the active scene ensures the Default Scene Condition passes,
        // allowing the client to observe enemies and other players.
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        base.NetworkManager.SceneManager.AddConnectionToScene(conn, activeScene);

        SpawnPlayerForConnection(conn, playerID);
    }

    private void SpawnPlayerForConnection(NetworkConnection conn, string playerID)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[ServerDataManager] Player Prefab is not assigned!");
            return;
        }

        bool isNewProfile = false;
        // 1. Get or Create Data
        if (!_playerDataMap.TryGetValue(playerID, out PlayerSaveData data))
        {
            data = new PlayerSaveData(playerID);
            data.health = 100; // Default health
            data.position = defaultSpawnPoint != null ? defaultSpawnPoint.position : Vector3.zero;
            data.rotation = defaultSpawnPoint != null ? defaultSpawnPoint.rotation : Quaternion.identity;
            
            // Add some starter items here if needed, or let PlayerInventory handle defaults
            _playerDataMap[playerID] = data;
            isNewProfile = true;
            Debug.Log($"[ServerDataManager] Created new profile for '{playerID}'.");
        }
        else
        {
            Debug.Log($"[ServerDataManager] Loaded existing profile for '{playerID}'.");
        }

        // 2. Instantiate Prefab
        NetworkObject playerObj = Instantiate(playerPrefab, data.position, data.rotation);

        // 3. Inject Data before spawning (so SyncVars can be set)
        Player playerComponent = playerObj.GetComponent<Player>();
        if (playerComponent != null)
        {
            playerComponent.playerID = playerID;
            
            // Initialize Health
            Player_Health healthComponent = playerObj.GetComponent<Player_Health>();
            if (healthComponent != null)
            {
                healthComponent.ServerInitialize(data.health);
            }

            // Initialize Inventory
            PlayerInventory inventory = playerObj.GetComponent<PlayerInventory>();
            if (inventory != null)
            {
                if (isNewProfile)
                {
                    inventory.SeedServer();
                }
                else
                {
                    inventory.ServerInitialize(data);
                }
            }
        }

        // 4. Spawn over network
        base.ServerManager.Spawn(playerObj, conn);
    }

    private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, FishNet.Transporting.RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Stopped)
        {
            // The client disconnected. Let's find their player object and save its state.
            if (conn.FirstObject != null)
            {
                Player player = conn.FirstObject.GetComponent<Player>();
                if (player != null && _authenticator != null)
                {
                    string playerID = _authenticator.GetIDForConnection(conn);
                    if (!string.IsNullOrEmpty(playerID))
                    {
                        SavePlayerState(playerID, player);
                    }
                }
            }
            
            // Periodically save all data when someone disconnects
            SaveAllData();
        }
    }

    public void SavePlayerState(string playerID, Player player)
    {
        if (string.IsNullOrEmpty(playerID) || player == null) return;

        if (!_playerDataMap.TryGetValue(playerID, out PlayerSaveData data))
        {
            data = new PlayerSaveData(playerID);
            _playerDataMap[playerID] = data;
        }

        // Update Position
        data.position = player.transform.position;
        data.rotation = player.transform.rotation;

        // Update Health
        if (player.health != null)
        {
            data.health = player.health.currentHealth.Value;
        }

        // Update Inventory
        if (player.inventory != null)
        {
            data.backpack.Clear();
            for (int i = 0; i < player.inventory.netBackpack.Count; i++)
            {
                var item = player.inventory.netBackpack[i];
                if (!string.IsNullOrEmpty(item.itemId))
                    data.backpack.Add(new InventoryItemData(item.itemId, item.count, i, item.ammoInMag, item.ammoReserve));
            }

            data.equipment.Clear();
            for (int i = 0; i < player.inventory.netEquipment.Count; i++)
            {
                var item = player.inventory.netEquipment[i];
                if (!string.IsNullOrEmpty(item.itemId))
                {
                    int mag = item.ammoInMag;
                    int res = item.ammoReserve;
                    
                    if (player.weapon != null)
                    {
                        var w = player.weapon.GetWeaponAt(i);
                        if (w != null)
                        {
                            mag = w.bulletsInMagazine;
                            res = w.totalReserveAmmo;
                        }
                    }
                    data.equipment.Add(new InventoryItemData(item.itemId, item.count, i, mag, res));
                }
            }
        }

        Debug.Log($"[ServerDataManager] Saved state for '{playerID}'.");
    }

    public void ClearPlayerState(string playerID)
    {
        if (!string.IsNullOrEmpty(playerID) && _playerDataMap.TryGetValue(playerID, out PlayerSaveData data))
        {
            data.backpack.Clear();
            data.equipment.Clear();
            data.health = 100;
            data.isRespawn = true;
            Debug.Log($"[ServerDataManager] Cleared state for '{playerID}' (marked as respawn).");
        }
    }

    // JSON Persistence

    [System.Serializable]
    private class SerializationWrapper
    {
        public List<PlayerSaveData> players = new List<PlayerSaveData>();
    }

    public void SaveAllData()
    {
        SerializationWrapper wrapper = new SerializationWrapper();
        wrapper.players = new List<PlayerSaveData>(_playerDataMap.Values);
        string json = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(_saveFilePath, json);
        Debug.Log($"[ServerDataManager] Data saved to {_saveFilePath}");
    }

    public void LoadAllData()
    {
        _playerDataMap.Clear();
        if (File.Exists(_saveFilePath))
        {
            string json = File.ReadAllText(_saveFilePath);
            SerializationWrapper wrapper = JsonUtility.FromJson<SerializationWrapper>(json);
            if (wrapper != null && wrapper.players != null)
            {
                foreach (var p in wrapper.players)
                {
                    _playerDataMap[p.playerID] = p;
                }
            }
            Debug.Log($"[ServerDataManager] Data loaded from {_saveFilePath}. {wrapper?.players?.Count ?? 0} profiles found.");
        }
    }

    [ContextMenu("Clear All Saved Data")]
    public void ClearAllData()
    {
        _playerDataMap.Clear();
        if (File.Exists(_saveFilePath))
        {
            File.Delete(_saveFilePath);
            Debug.Log($"[ServerDataManager] Deleted save file at {_saveFilePath}");
        }
        else
        {
            Debug.Log($"[ServerDataManager] Save file not found at {_saveFilePath}");
        }
    }
}
