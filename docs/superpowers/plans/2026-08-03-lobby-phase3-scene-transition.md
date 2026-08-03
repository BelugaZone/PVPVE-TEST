# Lobby / Stash / Matchflow — Phase 3: Scene Transition & Loadout Transfer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Implement the full lobby↔game scene transition cycle: room owner clicks Start → all members load the Game scene with their loadout committed → match runs → match ends → in-raid items move back to stash → all return to the Lobby scene. Connection never drops.

**Architecture:** `ServerDataManager` converts from `NetworkBehaviour` to plain `MonoBehaviour` + `DontDestroyOnLoad` (persists across scenes, no FN0002). It gains `StartMatchForMembers` / `EndMatchAndReturnToLobby` methods that: (1) transfer loadout↔in-raid in `PlayerSaveData`, (2) call `FishNet SceneManager.LoadGlobalScenes` with `ReplaceOption.All`, (3) coroutine-wait for the new scene then spawn the appropriate player bodies. `RoomManager.CmdStartMatch` delegates to `ServerDataManager.StartMatchForMembers` (after saving room state to static). `MatchManager.RestartMatchRoutine` calls `ServerDataManager.EndMatchAndReturnToLobby`.

**Tech Stack:** Unity 2023.1.0f1c1, FishNet (SceneManager, SceneLoadData, ReplaceOption).

## Global Constraints

- No namespaces; top-level classes.
- No Unity MCP — C# code + existing Editor tools.
- FishNet scene load: `NetworkManager.SceneManager.LoadGlobalScenes(new SceneLoadData(sceneName) { ReplaceScenes = ReplaceOption.All })`.
- `OnClientLoadedStartScenes` only fires for INITIAL client connection, NOT for subsequent global scene loads. Match-transition spawning uses a coroutine that waits for the active scene name.
- ServerDataManager becomes plain `MonoBehaviour` — no `NetworkObject` on its GameObject, no `OnStartServer`/`OnStopServer` (use `Start`/`OnDestroy`), use `FishNet.InstanceFinder` instead of `base.*`.
- RoomManager stays `NetworkBehaviour` (scene-placed, needs SyncVars). Saves/restores state via static fields on scene transitions.
- Loadout transfer is a MOVE (not copy): `loadoutEquip/loadoutBackpack` → `equipment/backpack` on match start; `equipment/backpack` → `stash` (append) on match end.
- Mid-match joiners: `OnClientLoadedStartScenes` still fires for new connections → scene-aware spawn gives them the full Player with default loadout (no loadout commit). Existing behavior, no change needed.

**Design spec:** `docs/superpowers/specs/2026-08-03-lobby-stash-matchflow-design.md` (§3).

---

## Task 1: ServerDataManager → MonoBehaviour + DontDestroyOnLoad + match-transition methods

**Files:**
- Modify: `Assets/Scripts/Managers/ServerDataManager.cs`

**Changes:**
1. Base class `NetworkBehaviour` → `MonoBehaviour`. Remove `using FishNet.Object;` (no longer needed). Add `using System.Collections;` (for coroutine).
2. `Awake`: add `DontDestroyOnLoad(gameObject);` (now legal — not NetworkBehaviour).
3. `OnStartServer()` → `Start()`. Remove `override`. Body unchanged (LoadAllData, find authenticator, subscribe to `InstanceFinder.NetworkManager.SceneManager.OnClientLoadedStartScenes` + `InstanceFinder.NetworkManager.ServerManager.OnRemoteConnectionState`).
4. `OnStopServer()` → `OnDestroy()`. Remove `override`. Unsubscribe using `InstanceFinder.NetworkManager`.
5. All `base.NetworkManager` → `FishNet.InstanceFinder.NetworkManager`. All `base.ServerManager` → `FishNet.InstanceFinder.ServerManager`.
6. `base.ServerManager.Spawn(playerObj, conn)` → `FishNet.InstanceFinder.ServerManager.Spawn(playerObj, conn)`.
7. Add new methods (below).

**New methods to add:**

```csharp
    // --- Match transition (Phase 3) ---

    /// <summary>Called by RoomManager.CmdStartMatch. Moves loadout→in-raid, loads Game scene, spawns players.</summary>
    public void StartMatchForMembers(System.Collections.Generic.List<int> memberClientIds)
    {
        // 1. For each member, move loadout → in-raid in PlayerSaveData.
        foreach (int clientId in memberClientIds)
        {
            var conn = FindConnectionByClientId(clientId);
            if (conn == null) continue;
            string id = _authenticator != null ? _authenticator.GetIDForConnection(conn) : null;
            if (id != null && _playerDataMap.TryGetValue(id, out var data))
                MoveLoadoutToInRaid(data);
        }

        // 2. Load the Game scene (replaces all scenes on server + clients).
        var nm = FishNet.InstanceFinder.NetworkManager;
        var sld = new FishNet.Managing.Scened.SceneLoadData("Scene_multi") { ReplaceScenes = FishNet.Managing.Scened.ReplaceOption.All };
        nm.SceneManager.LoadGlobalScenes(sld);

        // 3. Wait for the Game scene to be active, then spawn full Players for members.
        StartCoroutine(SpawnPlayersAfterSceneLoad("Scene_multi", memberClientIds));
    }

    /// <summary>Called by MatchManager on match end. Moves in-raid→stash, loads Lobby scene, spawns lobby bodies.</summary>
    public void EndMatchAndReturnToLobby()
    {
        // 1. For each player in the match, move in-raid → stash.
        var players = FindObjectsOfType<Player>();
        foreach (var player in players)
        {
            if (string.IsNullOrEmpty(player.playerID)) continue;
            SavePlayerState(player.playerID, player); // writes backpack/equipment to data
            if (_playerDataMap.TryGetValue(player.playerID, out var data))
                MoveInRaidToStash(data);
        }
        SaveAllData();

        // 2. Collect member client IDs (for re-spawn in lobby).
        var memberClientIds = new System.Collections.Generic.List<int>();
        foreach (var p in players)
        {
            if (p.NetworkObject != null && p.NetworkObject.Owner != null)
                memberClientIds.Add((int)p.NetworkObject.Owner.ClientId);
        }

        // 3. Load the Lobby scene.
        var nm = FishNet.InstanceFinder.NetworkManager;
        var sld = new FishNet.Managing.Scened.SceneLoadData("Lobby") { ReplaceScenes = FishNet.Managing.Scened.ReplaceOption.All };
        nm.SceneManager.LoadGlobalScenes(sld);

        // 4. Wait for Lobby scene, then spawn LobbyPlayers.
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

    private FishNet.Connection.NetworkConnection FindConnectionByClientId(int clientId)
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

    /// <summary>MOVE: equipment/backpack → stash (append). Clears in-raid.</summary>
    private void MoveInRaidToStash(PlayerSaveData data)
    {
        // Append equipment to stash (find empty slots)
        foreach (var item in data.equipment)
        {
            int slot = FindEmptyStashSlot(data);
            if (slot >= 0)
                data.stash.Add(new InventoryItemData(item.itemID, item.count, slot, item.ammoInMag, item.ammoReserve));
        }
        // Append backpack to stash
        foreach (var item in data.backpack)
        {
            int slot = FindEmptyStashSlot(data);
            if (slot >= 0)
                data.stash.Add(new InventoryItemData(item.itemID, item.count, slot, item.ammoInMag, item.ammoReserve));
        }
        // Clear in-raid
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
```

**Note:** the `using FishNet.Managing.Scened;` is needed for `SceneLoadData` / `ReplaceOption`. Add it to the usings. Also `using FishNet.Connection;` for `NetworkConnection`.

- [ ] **Step 1: Apply the conversion + add new methods**
- [ ] **Step 2: Verify compile**
- [ ] **Step 3: Commit**

---

## Task 2: RoomManager — static state save/restore + real CmdStartMatch

**Files:**
- Modify: `Assets/Scripts/Managers/RoomManager.cs`

**Changes:**
1. Add static fields to save/restore room state across scene transitions:
```csharp
    private static RoomState _savedState;
    private static int _savedOwnerClientId;
    private static int[] _savedMembers;
    private static bool _hasSavedState = false;
```

2. In `OnStartServer`, after setting `State.Value = RoomState.Empty`, restore from static if available:
```csharp
    public override void OnStartServer()
    {
        base.OnStartServer();
        if (_hasSavedState)
        {
            State.Value = _savedState;
            OwnerClientId.Value = _savedOwnerClientId;
            MemberClientIds.Clear();
            foreach (var m in _savedMembers) MemberClientIds.Add(m);
            _hasSavedState = false; // consumed
            Debug.Log("[RoomManager] Restored room state from static.");
        }
        else
        {
            State.Value = RoomState.Empty;
        }
        base.NetworkManager.ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
    }
```

3. Replace `CmdStartMatch` stub with real implementation:
```csharp
    [ServerRpc(RequireOwnership = false)]
    private void CmdStartMatch(NetworkConnection senderConn)
    {
        if (State.Value != RoomState.Gathering)
        {
            Debug.Log("[RoomManager] Cannot start: room not gathering.");
            return;
        }
        int clientId = (int)senderConn.ClientId;
        if (clientId != OwnerClientId.Value)
        {
            Debug.Log($"[RoomManager] Client {clientId} cannot start: not owner ({OwnerClientId.Value}).");
            return;
        }
        if (MemberClientIds.Count == 0)
        {
            Debug.Log("[RoomManager] Cannot start: no members.");
            return;
        }

        // Save room state to static (RoomManager will be destroyed when Lobby unloads).
        _savedState = RoomState.InMatch;
        _savedOwnerClientId = OwnerClientId.Value;
        _savedMembers = new int[MemberClientIds.Count];
        for (int i = 0; i < MemberClientIds.Count; i++) _savedMembers[i] = MemberClientIds[i];
        _hasSavedState = true;

        Debug.Log($"[RoomManager] StartMatch: owner={clientId}, members={MemberClientIds.Count}. Loading Game scene.");

        // Delegate to ServerDataManager: move loadout→in-raid, load Game scene, spawn players.
        if (ServerDataManager.Instance != null)
        {
            var memberList = new System.Collections.Generic.List<int>();
            foreach (var m in MemberClientIds) memberList.Add(m);
            ServerDataManager.Instance.StartMatchForMembers(memberList);
        }
    }
```

- [ ] **Step 1: Apply changes**
- [ ] **Step 2: Verify compile**
- [ ] **Step 3: Commit**

---

## Task 3: MatchManager — RestartMatchRoutine calls EndMatchAndReturnToLobby

**Files:**
- Modify: `Assets/Scripts/Managers/MatchManager.cs`

**Changes:** Replace the `RestartMatchRoutine` stub with a real call to `ServerDataManager.EndMatchAndReturnToLobby`:

```csharp
    private IEnumerator RestartMatchRoutine(float delay)
    {
        // Wait for the scoreboard countdown, then return to lobby.
        yield return new WaitForSeconds(delay);

        Debug.Log("[MatchManager] Match ended. Returning to lobby via ServerDataManager.");
        if (ServerDataManager.Instance != null)
        {
            ServerDataManager.Instance.EndMatchAndReturnToLobby();
        }
        else
        {
            Debug.LogError("[MatchManager] ServerDataManager.Instance is null! Cannot return to lobby.");
        }
    }
```

- [ ] **Step 1: Apply change**
- [ ] **Step 2: Verify compile**
- [ ] **Step 3: Commit**

---

## Task 4: Manual verification (full Phase 3 acceptance)

- [ ] **Step 1: Lobby → create room → start match**

Host: Login → create room → drag pistol to Primary loadout → click 开始游戏.
Expected: Console logs `[RoomManager] StartMatch... Loading Game scene.` Scene switches to Scene_multi. Full Player spawns with the pistol equipped (loadout committed). MatchManager starts the timer.

- [ ] **Step 2: Match end → return to lobby**

Wait for timer to expire (or extract with core).
Expected: Scoreboard shows for 5s. Console logs `[MatchManager] Returning to lobby`. Scene switches back to Lobby. LobbyPlayer spawns. Stash panel shows the items brought back from the raid (pistol + any loot).

- [ ] **Step 3: Multi-client**

Client B joins room → host starts → both load Game scene → both spawn as full Players → match ends → both return to Lobby with their stash updated.

- [ ] **Step 4: Mid-match joiner**

A new client logs in while match is in progress.
Expected: Spawns as full Player in Game scene with default loadout (no committed loadout). Can play. On match end, returns to lobby with whatever they gathered.

- [ ] **Step 5: Stash persistence after full cycle**

After returning to lobby, stop play, restart, login with same ID.
Expected: Stash reflects the post-match state (items brought back persisted).

---

## Self-Review

**Spec coverage:** CmdStartMatch scene transition → Task 2. loadout→in-raid → Task 1 (MoveLoadoutToInRaid). RestartMatchRoutine replaced → Task 3. in-raid→stash → Task 1 (MoveInRaidToStash). Lobby scene load → Task 1 (EndMatchAndReturnToLobby). Mid-match joiner → existing OnClientLoadedStartScenes (no change). ✓

**Risks:**
1. ServerDataManager MonoBehaviour conversion: timing of `Start()` vs server start. Start subscribes to events; events fire when server starts later. Should work. Verify in Task 4.
2. `LoadGlobalScenes` scene name: the Game scene is `Scene_multi` (its file name). Confirm the scene is in Build Settings (it is, index 1).
3. Coroutine `SpawnPlayersAfterSceneLoad`: `WaitUntil` active scene name. The scene load is async; the active scene changes when loaded. Should work. Verify in Task 4.
4. `FindConnectionByClientId` iterates `ServerManager.Clients.Values` — confirm this collection exists and is accessible. If not, adjust to `ServerManager.Connections` or similar.
5. LobbyPlayer (spawned in Lobby) is destroyed when Lobby unloads (Replace). The full Player spawns in Game. Ownership transfers via the new spawn. Verify no ownership conflicts in Task 4.
