using FishNet;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ServerDataManager : MonoBehaviour
{
    [Tooltip("The player prefab to spawn for authenticated clients.")]
    public NetworkObject playerPrefab;

    [Tooltip("Stripped player prefab spawned in the Lobby scene (has PlayerStash, no gameplay).")]
    public NetworkObject lobbyPlayerPrefab;

    [Tooltip("Default stash contents for a new player.")]
    public DefaultLoadout defaultLoadout;

    [Tooltip("Default spawn point for new players.")]
    public Transform defaultSpawnPoint;

    private Dictionary<string, PlayerSaveData> _playerDataMap = new Dictionary<string, PlayerSaveData>();
    private string _saveFilePath;

    private CustomAuthenticator _authenticator;

    public static ServerDataManager Instance;

    [Tooltip("Spawn point used when the player is in the Lobby scene.")]
    public Transform lobbySpawnPoint;

    [Tooltip("Name of the Lobby scene, used to pick the spawn point.")]
    public string lobbySceneName = "Lobby";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // Persist across scene loads. Now legal — plain MonoBehaviour, not NetworkBehaviour.
        // (FishNet FN0002 forbids DontDestroyOnLoad inside a NetworkBehaviour.)
        DontDestroyOnLoad(gameObject);
        _saveFilePath = Path.Combine(Application.persistentDataPath, "server_players_data.json");
    }

    private void OnDestroy()
    {
        // Guard: if this instance was destroyed as a duplicate in Awake, _saveFilePath is null
        // and there's nothing to save. Also skip if Instance is no longer this.
        if (Instance != this) return;
        if (string.IsNullOrEmpty(_saveFilePath)) return;

        if (FishNet.InstanceFinder.NetworkManager != null)
        {
            FishNet.InstanceFinder.NetworkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
            FishNet.InstanceFinder.NetworkManager.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
        }
        FlushAllStashes();
        SaveAllData();
    }

    private void Start()
    {
        LoadAllData();

        _authenticator = FindObjectOfType<CustomAuthenticator>();
        if (_authenticator == null)
        {
            Debug.LogError("[ServerDataManager] Could not find CustomAuthenticator in scene!");
        }

        // Listen for when a client finishes loading the scene so we can spawn them
        FishNet.InstanceFinder.NetworkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;

        // Listen for disconnects to save their state
        FishNet.InstanceFinder.NetworkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
    }

    private void FlushAllStashes()
    {
        var stashes = FindObjectsOfType<PlayerStash>();
        foreach (var stash in stashes)
        {
            string id = null;
            // The LobbyPlayer may not carry a Player component; resolve id via the connection.
            var p = stash.GetComponent<Player>();
            if (p != null && !string.IsNullOrEmpty(p.playerID)) id = p.playerID;
            if (id == null && _authenticator != null)
            {
                var nob = stash.NetworkObject;
                if (nob != null && nob.Owner != null)
                    id = _authenticator.GetIDForConnection(nob.Owner);
            }
            if (!string.IsNullOrEmpty(id) && _playerDataMap.TryGetValue(id, out var d))
                stash.SaveInto(d);
        }
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
        FishNet.InstanceFinder.NetworkManager.SceneManager.AddConnectionToScene(conn, activeScene);

        SpawnPlayerForConnection(conn, playerID);
    }

    public void SpawnPlayerForConnection(NetworkConnection conn, string playerID)
    {
        bool isNewProfile = false;
        // 1. Get or Create Data
        if (!_playerDataMap.TryGetValue(playerID, out PlayerSaveData data))
        {
            data = new PlayerSaveData(playerID);
            data.health = 100; // Default health
            Transform spawn = ResolveSpawnPoint();
            data.position = spawn != null ? spawn.position : Vector3.zero;
            data.rotation = spawn != null ? spawn.rotation : Quaternion.identity;

            // Add some starter items here if needed, or let PlayerInventory handle defaults
            _playerDataMap[playerID] = data;
            isNewProfile = true;
            Debug.Log($"[ServerDataManager] Created new profile for '{playerID}'.");
        }
        else
        {
            Debug.Log($"[ServerDataManager] Loaded existing profile for '{playerID}'.");
        }

        // 2. Choose prefab by scene: Lobby -> LobbyPlayer (stash), Game -> full Player.
        bool inLobby = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == lobbySceneName;
        NetworkObject prefabToUse = inLobby && lobbyPlayerPrefab != null ? lobbyPlayerPrefab : playerPrefab;
        if (prefabToUse == null)
        {
            Debug.LogError("[ServerDataManager] No player prefab for scene " + (inLobby ? "Lobby" : "Game"));
            return;
        }

        Transform spawnPt = ResolveSpawnPoint();
        Vector3 pos = isNewProfile ? (spawnPt != null ? spawnPt.position : Vector3.zero)
                                   : data.position;
        Quaternion rot = isNewProfile ? (spawnPt != null ? spawnPt.rotation : Quaternion.identity)
                                      : data.rotation;
        NetworkObject playerObj = Instantiate(prefabToUse, pos, rot);

        // 3. Inject data before spawning.
        if (inLobby)
        {
            PlayerStash stash = playerObj.GetComponent<PlayerStash>();
            if (stash != null)
            {
                stash.ServerInitialize(data, defaultLoadout, isNewProfile);
            }
            // Set playerID on a Player component if present (LobbyPlayer may not have one).
            var pComp = playerObj.GetComponent<Player>();
            if (pComp != null) pComp.playerID = playerID;
        }
        else
        {
            Player playerComponent = playerObj.GetComponent<Player>();
            if (playerComponent != null)
            {
                playerComponent.playerID = playerID;
                Player_Health healthComponent = playerObj.GetComponent<Player_Health>();
                if (healthComponent != null) healthComponent.ServerInitialize(data.health);
                PlayerInventory inventory = playerObj.GetComponent<PlayerInventory>();
                if (inventory != null)
                {
                    if (isNewProfile) inventory.SeedServer();
                    else inventory.ServerInitialize(data);
                }
            }
        }

        // 4. Spawn over network
        FishNet.InstanceFinder.ServerManager.Spawn(playerObj, conn);
    }

    public Transform ResolveSpawnPoint()
    {
        string active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (active == lobbySceneName && lobbySpawnPoint != null)
            return lobbySpawnPoint;
        return defaultSpawnPoint;
    }

    private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, FishNet.Transporting.RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Stopped)
        {
            // The client disconnected. Let's find their player object and save its state.
            if (conn.FirstObject != null)
            {
                // Works for both full Player (Game) and LobbyPlayer (Lobby) which carries a Player shell.
                Player player = conn.FirstObject.GetComponent<Player>();
                PlayerStash stash = conn.FirstObject.GetComponent<PlayerStash>();
                if (player != null && _authenticator != null)
                {
                    string playerID = _authenticator.GetIDForConnection(conn);
                    if (!string.IsNullOrEmpty(playerID))
                        SavePlayerState(playerID, player);
                }
                else if (stash != null && _authenticator != null)
                {
                    // LobbyPlayer without a Player component: save stash directly.
                    string playerID = _authenticator.GetIDForConnection(conn);
                    if (!string.IsNullOrEmpty(playerID) && _playerDataMap.TryGetValue(playerID, out var d))
                        stash.SaveInto(d);
                }
            }
            
            // Periodically save all data when someone disconnects
            SaveAllData();
        }
    }

    /// <summary>Resolves the authenticated player ID for a spawned NetworkObject's owner.</summary>
    public string GetIDForNetworkObject(FishNet.Object.NetworkObject nob)
    {
        if (nob == null || _authenticator == null) return null;
        if (nob.Owner == null) return null;
        return _authenticator.GetIDForConnection(nob.Owner);
    }

    /// <summary>Tries to get a player's save data (used by PlayerStash.OnStopServer).</summary>
    public bool TryGetData(string playerID, out PlayerSaveData data)
    {
        return _playerDataMap.TryGetValue(playerID, out data);
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

        // Save stash (lobby player body).
        PlayerStash stashComp = player.GetComponent<PlayerStash>();
        if (stashComp != null)
        {
            stashComp.SaveInto(data);
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
            // NOTE: data.stash is intentionally preserved — death loses in-raid items only.
            Debug.Log($"[ServerDataManager] Cleared in-raid state for '{playerID}' (stash preserved).");
        }
    }

    // --- Match transition (Phase 3) ---

    /// <summary>Called by RoomManager.CmdStartMatch. Moves loadout→in-raid, loads Game scene, spawns players.</summary>
    public void StartMatchForMembers(System.Collections.Generic.List<int> memberClientIds)
    {
        // 1. For each member: flush their PlayerStash SyncLists → PlayerSaveData, then move loadout → in-raid.
        foreach (int clientId in memberClientIds)
        {
            var conn = FindConnectionByClientId(clientId);
            if (conn == null) continue;
            string id = _authenticator != null ? _authenticator.GetIDForConnection(conn) : null;
            if (id != null && _playerDataMap.TryGetValue(id, out var data))
            {
                // Flush the live PlayerStash SyncLists into PlayerSaveData before reading loadout.
                // The loadout lives in netLoadoutEquip/netLoadoutBackpack SyncLists on the LobbyPlayer,
                // NOT in PlayerSaveData until SaveInto is called.
                if (conn.FirstObject != null)
                {
                    var stash = conn.FirstObject.GetComponent<PlayerStash>();
                    if (stash != null)
                    {
                        stash.SaveInto(data);
                        
                    }
                }
                
                MoveLoadoutToInRaid(data);
                
            }
        }

        // 2. Load the Game scene (replaces all scenes on server + clients).
        var nm = FishNet.InstanceFinder.NetworkManager;
        var sld = new SceneLoadData("Scene_multi") { ReplaceScenes = ReplaceOption.All };
        nm.SceneManager.LoadGlobalScenes(sld);

        // 3. Wait for the Game scene to be active, then spawn full Players for members.
        StartCoroutine(SpawnPlayersAfterSceneLoad("Scene_multi", memberClientIds));
    }

    /// <summary>Called by MatchManager on match end. Moves in-raid→loadout (preserve config), loads Lobby scene, spawns lobby bodies.</summary>
    public void EndMatchAndReturnToLobby()
    {
        // 1. For each player in the match, move in-raid → loadout (preserve their equipment/backpack
        //    as the loadout for the next match, NOT dumped into stash).
        var players = FindObjectsOfType<Player>();
        foreach (var player in players)
        {
            if (string.IsNullOrEmpty(player.playerID)) continue;
            SavePlayerState(player.playerID, player); // writes backpack/equipment to data
            if (_playerDataMap.TryGetValue(player.playerID, out var data))
                MoveInRaidToLoadout(data);
        }
        SaveAllData();

        // 2. Collect member client IDs (for re-spawn in lobby).
        var memberClientIds = new System.Collections.Generic.List<int>();
        foreach (var p in players)
        {
            if (p.NetworkObject != null && p.NetworkObject.Owner != null)
                memberClientIds.Add((int)p.NetworkObject.Owner.ClientId);
        }

        // 3. Tell RoomManager to restore as Gathering (not InMatch) when Lobby reloads.
        RoomManager.SetSavedStateForLobbyReturn();

        // 4. Load the Lobby scene.
        var nm = FishNet.InstanceFinder.NetworkManager;
        var sld = new SceneLoadData("Lobby") { ReplaceScenes = ReplaceOption.All };
        nm.SceneManager.LoadGlobalScenes(sld);

        // 5. Wait for Lobby scene, then spawn LobbyPlayers.
        StartCoroutine(SpawnPlayersAfterSceneLoad("Lobby", memberClientIds));
    }

    private System.Collections.IEnumerator SpawnPlayersAfterSceneLoad(string sceneName, System.Collections.Generic.List<int> clientIds)
    {
        // Wait until the target scene is the active scene.
        yield return new UnityEngine.WaitUntil(() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == sceneName);
        yield return null; // one extra frame for scene objects to initialize

        foreach (int clientId in clientIds)
        {
            var conn = FindConnectionByClientId(clientId);
            if (conn == null) continue;
            string id = _authenticator != null ? _authenticator.GetIDForConnection(conn) : null;
            if (!string.IsNullOrEmpty(id))
                SpawnPlayerForConnection(conn, id);
        }
    }

    private NetworkConnection FindConnectionByClientId(int clientId)
    {
        var nm = FishNet.InstanceFinder.NetworkManager;
        if (nm == null) return null;
        foreach (var conn in nm.ServerManager.Clients.Values)
        {
            if ((int)conn.ClientId == clientId) return conn;
        }
        return null;
    }

    /// <summary>MOVE: loadoutEquip/loadoutBackpack → equipment/backpack. Clears loadout.</summary>
    private void MoveLoadoutToInRaid(PlayerSaveData data)
    {
        // equipment = loadoutEquip
        data.equipment.Clear();
        foreach (var item in data.loadoutEquip)
            data.equipment.Add(new InventoryItemData(item.itemID, item.count, item.slotIndex, item.ammoInMag, item.ammoReserve));
        // backpack = loadoutBackpack
        data.backpack.Clear();
        foreach (var item in data.loadoutBackpack)
            data.backpack.Add(new InventoryItemData(item.itemID, item.count, item.slotIndex, item.ammoInMag, item.ammoReserve));
        // Clear loadout (committed to the raid)
        data.loadoutEquip.Clear();
        data.loadoutBackpack.Clear();
    }

    /// <summary>MOVE: equipment/backpack → loadoutEquip/loadoutBackpack. Clears in-raid.
    /// Preserves the player's extraction-time state as their loadout for the next match.</summary>
    private void MoveInRaidToLoadout(PlayerSaveData data)
    {
        // loadoutEquip = equipment (preserve slot indices)
        data.loadoutEquip.Clear();
        foreach (var item in data.equipment)
            data.loadoutEquip.Add(new InventoryItemData(item.itemID, item.count, item.slotIndex, item.ammoInMag, item.ammoReserve));
        // loadoutBackpack = backpack (preserve slot indices)
        data.loadoutBackpack.Clear();
        foreach (var item in data.backpack)
            data.loadoutBackpack.Add(new InventoryItemData(item.itemID, item.count, item.slotIndex, item.ammoInMag, item.ammoReserve));
        // Clear in-raid (moved to loadout)
        data.equipment.Clear();
        data.backpack.Clear();
    }

    private int FindEmptyStashSlot(PlayerSaveData data)
    {
        // Stash slots are 0..31. Find a slotIndex not used by any stash item.
        var used = new System.Collections.Generic.HashSet<int>();
        foreach (var item in data.stash) used.Add(item.slotIndex);
        for (int i = 0; i < 32; i++) if (!used.Contains(i)) return i;
        return -1; // stash full
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
