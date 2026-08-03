# Lobby / Stash / Matchflow — Phase 0: Scene Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the project into a two-scene (Lobby + Game) layout with a long-lived, `DontDestroyOnLoad` NetworkManager, a self-built start/login screen replacing the FishNet demo HUD, and scene-aware player spawning — so that a server can be started from the Lobby scene, a client can log in, and a player object spawns in the Lobby. No match-scene transition yet (that is Phase 3).

**Architecture:** NetworkManager + ServerDataManager + CustomAuthenticator move from `Scene_multi` into a new `Lobby` scene (build index 0) and are marked `DontDestroyOnLoad` so they survive future scene transitions. `Scene_multi` becomes the `Game` scene (build index 1), stripped of networking/login objects. A new `UI_StartScreen` consolidates connection-mode selection (server / host / client+IP) and ID entry, replacing both the demo OnGUI HUD and `UI_Login`'s connection duty. `ServerDataManager` spawns the existing `Player` prefab at a scene-appropriate spawn point based on the currently active scene.

**Tech Stack:** Unity 2023.1.0f1c1 (URP), FishNet, new Input System 1.6.1, uGUI + TextMeshPro (already in project via `UI_Login`). Tugboat transport (FishNet default UDP).

## Global Constraints

- Unity version: `2023.1.0f1c1`. URP active.
- New Input System package `1.6.1`; do NOT edit `PlayerControls.inputactions`.
- TextMeshPro IS available (used by `UI_Login.cs`); use `TMP_InputField`/`TextMeshProUGUI` for the start screen.
- Follow existing script conventions: no namespace, top-level classes, `[System.Serializable]` plain classes, `NetworkBehaviour` components gated to `IsOwner` via `Player.OnStartClient`.
- No Unity MCP. All scene/prefab/asset creation happens in C# (runtime scripts + an Editor `MenuItem` tool), matching `2026-08-01-player-ui-system.md` and `2026-07-31-player-grenade-throw.md`.
- FishNet demo `NetworkHudCanvas` OnGUI buttons must be removed; `UI_StartScreen` is the sole entry.
- `NetworkManager` must NOT be destroyed on scene load (it becomes `DontDestroyOnLoad`). The existing `MatchManager.RestartMatchRoutine` habit of `Destroy(nm.gameObject)` must be neutralized in Phase 0 (fully replaced in Phase 3).
- The existing `Player` prefab is reused as-is in Phase 0 (spawned in Lobby). A dedicated stripped lobby body is Phase 1.

## Testing approach

The project has **no test framework** (no `com.unity.test-framework`, no `Tests/` folders). Matching the existing convention, each task's test cycle is: **(a) the project compiles with zero console errors**, and **(b) a manual play-mode verification step**. The full manual checklist is the final task. This is a deliberate, documented deviation from unit TDD because Unity scene/UI work has no test infrastructure in this repo.

**Design spec:** `docs/superpowers/specs/2026-08-03-lobby-stash-matchflow-design.md` (§6 Phase 0). Read it before starting.

---

## File Structure

**New runtime scripts:**
- `Assets/Scripts/UI/UI_StartScreen.cs` — uGUI/TMP canvas built at runtime in the Lobby scene. Responsibility: connection-mode selection (server / host / client+IP), ID entry, calls `ServerManager.StartConnection` / `ClientManager.StartConnection`, shows auth errors. Replaces demo `NetworkHudCanvas` and supersedes `UI_Login`'s connection role.

**New Editor script:**
- `Assets/Editor/LobbySceneSetupTool.cs` — `[MenuItem("Tools/Lobby Setup/Build Lobby Scene")]`. Responsibility: create `Assets/Scenes/Lobby.unity`, move the NetworkManager (+ CustomAuthenticator) from `Scene_multi` into it, add a `UI_StartScreen` canvas, register both scenes in Build Settings (Lobby=0, Game=1), and strip the demo HUD / old `LoginCanvas` from `Scene_multi` (renamed conceptually to Game). Idempotent.

**Modified runtime scripts:**
- `Assets/Scripts/Managers/ServerDataManager.cs` — add `DontDestroyOnLoad(gameObject)` in `Awake`; add a `lobbySpawnPoint` field and scene-aware spawn-point selection in `SpawnPlayerForConnection` (Lobby scene → lobby spawn point; else → existing `defaultSpawnPoint`). Keep all existing persistence/spawn logic intact.
- `Assets/Scripts/Managers/MatchManager.cs` — neutralize `RestartMatchRoutine` so it no longer destroys the NetworkManager or stops the connection (Phase 0 guard; Phase 3 replaces it with `RoomManager`-driven return-to-lobby).

**Scenes:**
- `Assets/Scenes/Lobby.unity` — NEW (build index 0). Contains NetworkManager (+CustomAuthenticator), ServerDataManager, UI_StartScreen canvas, EventSystem, a LobbySpawnPoint marker.
- `Assets/Scenes/Scene_multi.unity` — existing (build index 1, the Game scene). Stripped of NetworkManager, ServerDataManager, LoginCanvas, demo HUD. Keeps level geometry, enemies, ExtractionZone, MatchManager, GameManager.

---

## Task 1: UI_StartScreen runtime script

**Files:**
- Create: `Assets/Scripts/UI/UI_StartScreen.cs`

**Interfaces:**
- Consumes: `FishNet.InstanceFinder.NetworkManager` / `ClientManager` / `ServerManager`; `CustomAuthenticator.PendingLoginID` (static string, existing); `CustomAuthenticator.OnClientAuthFailed` (existing `Action<string>`); FishNet Tugboat transport (`FishNet.Transporting.Tugboat.Tugboat`) for client IP.
- Produces: a self-contained `MonoBehaviour` that builds its own canvas. No external callers in Phase 0 (the Lobby scene canvas carries it). Later phases may call `UI_StartScreen.Instance.Hide()` / `Show()`.

- [ ] **Step 1: Write the script**

Create `Assets/Scripts/UI/UI_StartScreen.cs`:

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishNet;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using System.Collections;

public class UI_StartScreen : MonoBehaviour
{
    public static UI_StartScreen Instance;

    [SerializeField] private string defaultClientIP = "127.0.0.1";

    private TMP_InputField _idInput;
    private TMP_InputField _ipInput;
    private Button _startServerButton;
    private Button _hostButton;
    private Button _clientButton;
    private TextMeshProUGUI _errorText;

    private void Awake()
    {
        if (Instance == null) { Instance = this; }
        else { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);
        BuildUI();
    }

    private void Start()
    {
        _startServerButton.onClick.AddListener(StartServer);
        _hostButton.onClick.AddListener(LoginAsHost);
        _clientButton.onClick.AddListener(LoginAsClient);

        if (_errorText != null) _errorText.text = "";
        CustomAuthenticator.OnClientAuthFailed += HandleAuthFailed;

        var nm = InstanceFinder.NetworkManager;
        if (nm != null)
            nm.ClientManager.OnClientConnectionState += OnClientConnectionState;
    }

    private void OnDestroy()
    {
        CustomAuthenticator.OnClientAuthFailed -= HandleAuthFailed;
        var nm = InstanceFinder.NetworkManager;
        if (nm != null)
            nm.ClientManager.OnClientConnectionState -= OnClientConnectionState;
    }

    // --- Connection actions ---

    private void StartServer()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null) { SetError("No NetworkManager."); return; }
        SetError("Starting server...");
        nm.ServerManager.StartConnection();
        _startServerButton.interactable = false;
        // Server-only: keep the screen visible so the host can also log in later
        // via the Host button (server already running).
    }

    private void LoginAsHost()
    {
        if (!TryPrepareLogin(out var nm)) return;
        SetError("Starting host (server + client)...");
        // Host = server + client in one process.
        if (!nm.ServerManager.IsStarted)
            nm.ServerManager.StartConnection();
        nm.ClientManager.StartConnection();
    }

    private void LoginAsClient()
    {
        if (!TryPrepareLogin(out var nm)) return;
        // Set the transport address for the client connection.
        var tug = nm.TransportManager.GetTransport<Tugboat>();
        if (tug != null && _ipInput != null && !string.IsNullOrWhiteSpace(_ipInput.text))
            tug.clientAddress = _ipInput.text.Trim();
        SetError("Connecting as client...");
        nm.ClientManager.StartConnection();
    }

    private bool TryPrepareLogin(out FishNet.Managing.NetworkManager nm)
    {
        nm = InstanceFinder.NetworkManager;
        if (nm == null) { SetError("No NetworkManager."); return false; }
        if (_idInput == null || string.IsNullOrWhiteSpace(_idInput.text))
        {
            SetError("Please enter a valid ID.");
            return false;
        }
        CustomAuthenticator.PendingLoginID = _idInput.text.Trim();
        _hostButton.interactable = false;
        _clientButton.interactable = false;
        return true;
    }

    // --- Connection state / auth feedback ---

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
            StartCoroutine(HideAfterDelay());
        else if (args.ConnectionState == LocalConnectionState.Stopped)
            Show(); // disconnected / auth failed -> show again
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(0.5f);
        if (InstanceFinder.ClientManager.Connection.IsActive)
            Hide();
    }

    private void HandleAuthFailed(string error)
    {
        SetError(error);
        _hostButton.interactable = true;
        _clientButton.interactable = true;
    }

    public void Show() { gameObject.SetActive(true); }
    public void Hide() { gameObject.SetActive(false); }

    private void SetError(string msg)
    {
        if (_errorText != null) _errorText.text = msg;
    }

    // --- UI construction (runtime, matches existing code-built canvases) ---

    private void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        gameObject.AddComponent<GraphicRaycaster>();

        // Panel background
        var panel = CreateImage("Panel", transform);
        panel.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero; panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero; panelRt.offsetMax = Vector2.zero;

        // Title
        var title = CreateText("Title", "PVPVE — Start", transform);
        title.fontSize = 48; title.alignment = TextAnchor.MiddleCenter;
        SetAnchors(title.rectTransform, new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.9f));
        title.rectTransform.sizeDelta = new Vector2(600, 80);

        // ID input
        _idInput = CreateInputField("IDInput", "Enter player ID...", transform,
            new Vector2(0.5f, 0.74f), new Vector2(360, 50));

        // IP input (client only)
        _ipInput = CreateInputField("IPInput", defaultClientIP, transform,
            new Vector2(0.5f, 0.66f), new Vector2(360, 50));
        _ipInput.text = defaultClientIP;

        // Buttons
        _startServerButton = CreateButton("StartServerBtn", "Start Server Only", transform,
            new Vector2(0.5f, 0.52f), new Vector2(360, 56));
        _hostButton = CreateButton("HostBtn", "Login as Host (Server + Client)", transform,
            new Vector2(0.5f, 0.42f), new Vector2(360, 56));
        _clientButton = CreateButton("ClientBtn", "Login as Client", transform,
            new Vector2(0.5f, 0.32f), new Vector2(360, 56));

        // Error text
        _errorText = CreateText("ErrorText", "", transform);
        _errorText.fontSize = 22; _errorText.color = Color.yellow;
        _errorText.alignment = TextAnchor.MiddleCenter;
        SetAnchors(_errorText.rectTransform, new Vector2(0.5f, 0.20f), new Vector2(0.5f, 0.20f));
        _errorText.rectTransform.sizeDelta = new Vector2(700, 40);
    }

    // --- uGUI helpers (procedural; no external assets) ---

    private Image CreateImage(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<Image>();
    }

    private TextMeshProUGUI CreateText(string name, string content, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.font = TMPro.TMP_Settings.defaultFontAsset;
        t.color = Color.white;
        return t;
    }

    private TMP_InputField CreateInputField(string name, string placeholder, Transform parent, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        SetAnchors(rt, anchor, anchor);
        rt.sizeDelta = size;

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.25f, 1f);

        var input = go.AddComponent<TMP_InputField>();
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(go.transform, false);
        var textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(10, 6); textRt.offsetMax = new Vector2(-10, -6);
        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.font = TMPro.TMP_Settings.defaultFontAsset;
        text.color = Color.white; text.fontSize = 28;
        input.textComponent = text;
        input.textViewport = textRt;

        var phObj = new GameObject("Placeholder");
        phObj.transform.SetParent(go.transform, false);
        var phRt = phObj.AddComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
        phRt.offsetMin = new Vector2(10, 6); phRt.offsetMax = new Vector2(-10, -6);
        var ph = phObj.AddComponent<TextMeshProUGUI>();
        ph.font = TMPro.TMP_Settings.defaultFontAsset;
        ph.color = new Color(0.6f, 0.6f, 0.6f, 1f); ph.fontSize = 28; ph.text = placeholder;
        input.placeholder = ph;

        return input;
    }

    private Button CreateButton(string name, string label, Transform parent, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        SetAnchors(rt, anchor, anchor);
        rt.sizeDelta = size;
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.45f, 0.8f, 1f);
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.25f, 0.55f, 0.9f, 1f);
        colors.pressedColor = new Color(0.1f, 0.3f, 0.6f, 1f);
        btn.colors = colors;

        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(go.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero; labelRt.offsetMax = Vector2.zero;
        var t = labelObj.AddComponent<TextMeshProUGUI>();
        t.font = TMPro.TMP_Settings.defaultFontAsset;
        t.text = label; t.alignment = TextAnchor.MiddleCenter; t.fontSize = 26; t.color = Color.white;
        return btn;
    }

    private void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: Unity Editor → wait for recompile (or `Assets/Reimport All` if needed).
Expected: zero compile errors. If `FishNet.Transporting.Tugboat` namespace is not found, confirm the Tugboat transport source exists at `Library/PackageCache/com.firstgeargames.fishnet@*/Runtime/Transporting/Transports/Tugboat/Tugboat.cs` and that its assembly is referenced (it is by default in FishNet). If a different transport is configured, cast to that type instead.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/UI_StartScreen.cs
git commit -m "feat(lobby): add UI_StartScreen (server/host/client+IP login)"
```

---

## Task 2: ServerDataManager — DontDestroyOnLoad + scene-aware spawn

**Files:**
- Modify: `Assets/Scripts/Managers/ServerDataManager.cs` (Awake at lines 23-29; SpawnPlayerForConnection at lines 83-143)

**Interfaces:**
- Consumes: `UnityEngine.SceneManagement.SceneManager.GetActiveScene().name` to decide spawn point.
- Produces: `ServerDataManager` now persists across scene loads; `SpawnPlayerForConnection` picks `lobbySpawnPoint` when in the Lobby scene, else `defaultSpawnPoint`. Adds a public `lobbySpawnPoint` field and a public `string lobbySceneName = "Lobby"` field for the setup tool / inspector to bind.

- [ ] **Step 1: Add DontDestroyOnLoad + lobby spawn fields**

In `Assets/Scripts/Managers/ServerDataManager.cs`, replace the `Awake` method (lines 23-29) with:

```csharp
    [Tooltip("Spawn point used when the player is in the Lobby scene.")]
    public Transform lobbySpawnPoint;

    [Tooltip("Name of the Lobby scene, used to pick the spawn point.")]
    public string lobbySceneName = "Lobby";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject);
        _saveFilePath = Path.Combine(Application.persistentDataPath, "server_players_data.json");
    }
```

- [ ] **Step 2: Make spawn position scene-aware**

In `SpawnPlayerForConnection` (around lines 91-104, the new-profile block), replace the position/rotation assignment:

```csharp
            data.health = 100;
            Transform spawn = ResolveSpawnPoint();
            data.position = spawn != null ? spawn.position : Vector3.zero;
            data.rotation = spawn != null ? spawn.rotation : Quaternion.identity;
```

And add a helper method immediately after `SpawnPlayerForConnection`:

```csharp
    private Transform ResolveSpawnPoint()
    {
        string active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (active == lobbySceneName && lobbySpawnPoint != null)
            return lobbySpawnPoint;
        return defaultSpawnPoint;
    }
```

Note: existing profiles use `data.position` from the save (line 111 `Instantiate(playerPrefab, data.position, data.rotation)`). For Phase 0 this is acceptable — a returning player spawns at their saved position. Phase 1 will refine lobby re-entry to always use the lobby spawn point regardless of saved position.

- [ ] **Step 3: Verify it compiles**

Run: Unity Editor recompile.
Expected: zero errors. Confirm `DontDestroyOnLoad` resolves (`UnityEngine` is already imported via line 6).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Managers/ServerDataManager.cs
git commit -m "feat(lobby): ServerDataManager DontDestroyOnLoad + scene-aware spawn point"
```

---

## Task 3: Neutralize MatchManager.RestartMatchRoutine

**Files:**
- Modify: `Assets/Scripts/Managers/MatchManager.cs` (RestartMatchRoutine at lines ~147-171)

**Interfaces:**
- Consumes: nothing new.
- Produces: a `RestartMatchRoutine` that no longer destroys the NetworkManager and no longer stops the connection. It just logs a warning. This prevents the old hard-reload path from breaking the new long-lived-NM model if a match somehow ends during Phase 0 testing. Phase 3 replaces this method entirely with `RoomManager`-driven return-to-lobby.

- [ ] **Step 1: Read the current method**

Open `Assets/Scripts/Managers/MatchManager.cs`, find `RestartMatchRoutine` (the coroutine that calls `StopConnection`, `Destroy(nm.gameObject)`, and `UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.buildIndex)`).

- [ ] **Step 2: Replace it with a no-op guard**

Replace the entire `RestartMatchRoutine` body with:

```csharp
    private IEnumerator RestartMatchRoutine()
    {
        // Phase 0: the lobby/matchflow refactor moves to a long-lived NetworkManager
        // and RoomManager-driven scene transitions (Phase 3). The old hard-reload path
        // (stop connection + destroy NM + Unity scene reload) would tear down the new
        // foundation, so it is disabled here. Phase 3 replaces this with a real
        // return-to-lobby via FishNet SceneManager.
        Debug.LogWarning("[MatchManager] RestartMatchRoutine is disabled in Phase 0 (lobby/matchflow refactor). Match-end return-to-lobby arrives in Phase 3.");
        yield break;
    }
```

If the method is invoked from `EndMatch` via `StartCoroutine`, leaving it a `yield break` coroutine is safe (it completes immediately).

- [ ] **Step 3: Verify it compiles**

Run: Unity Editor recompile.
Expected: zero errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Managers/MatchManager.cs
git commit -m "chore(lobby): disable MatchManager hard-reload (Phase 0 guard; Phase 3 replaces)"
```

---

## Task 4: LobbySceneSetupTool — build Lobby scene, reconfigure build settings

**Files:**
- Create: `Assets/Editor/LobbySceneSetupTool.cs`

**Interfaces:**
- Consumes: `Assets/Scenes/Scene_multi.unity` (source of the NetworkManager + CustomAuthenticator + ServerDataManager + playerPrefab references); the FishNet NetworkManager prefab guid `0b650fca685f2eb41a86538aa883e4c1` (only as a fallback if the scene instance cannot be moved); `UI_StartScreen` (Task 1); `ServerDataManager` (Task 2).
- Produces: `Assets/Scenes/Lobby.unity` registered at build index 0; `Assets/Scenes/Scene_multi.unity` at build index 1; the NetworkManager (+ CustomAuthenticator + ServerDataManager) relocated into Lobby and marked persistent; a `UI_StartScreen` canvas + EventSystem + `LobbySpawnPoint` in Lobby; the old `LoginCanvas` and demo `NetworkHudCanvas` removed from `Scene_multi`.

- [ ] **Step 1: Write the Editor tool**

Create `Assets/Editor/LobbySceneSetupTool.cs`:

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public static class LobbySceneSetupTool
{
    private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
    private const string GameScenePath = "Assets/Scenes/Scene_multi.unity";
    private const string LobbySceneName = "Lobby";

    [MenuItem("Tools/Lobby Setup/Build Lobby Scene")]
    public static void BuildLobbyScene()
    {
        // --- 1. Open Scene_multi (the current Game scene) to harvest the NetworkManager ---
        if (!System.IO.File.Exists(GameScenePath))
        {
            Debug.LogError($"[LobbySetup] Game scene not found: {GameScenePath}");
            return;
        }
        var gameScene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

        // Harvest references BEFORE moving anything.
        GameObject nmGo = GameObject.Find("NetworkManager");
        CustomAuthenticator authenticator = null;
        ServerDataManager serverData = null;
        if (nmGo != null)
        {
            authenticator = nmGo.GetComponentInChildren<CustomAuthenticator>(true);
            serverData = Object.FindObjectOfType<ServerDataManager>(true);
        }

        if (nmGo == null)
        {
            Debug.LogError("[LobbySetup] No GameObject named 'NetworkManager' in Scene_multi. Aborting.");
            return;
        }

        // Capture the playerPrefab + defaultSpawnPoint from ServerDataManager so we can
        // re-bind them after relocation.
        NetworkObject playerPrefab = null;
        Transform defaultSpawn = null;
        if (serverData != null)
        {
            playerPrefab = serverData.playerPrefab;
            defaultSpawn = serverData.defaultSpawnPoint;
        }

        // --- 2. Strip demo HUD (NetworkHudCanvas) and old LoginCanvas from Scene_multi ---
        StripByName(gameScene, "NetworkHudCanvas");
        StripByName(gameScene, "LoginCanvas");

        // --- 3. Create the Lobby scene additively and move the NM into it ---
        Scene lobbyScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.MarkSceneDirty(lobbyScene);

        // Move the NetworkManager (and its children, incl. NetworkHudCanvas already stripped)
        // into the Lobby scene. MoveGameObjectToScene preserves prefab overrides & components.
        SceneManager.MoveGameObjectToScene(nmGo, lobbyScene);

        // If ServerDataManager was a separate GameObject (not a child of NM), move it too.
        if (serverData != null && serverData.gameObject != nmGo)
            SceneManager.MoveGameObjectToScene(serverData.gameObject, lobbyScene);

        // --- 4. Add a LobbySpawnPoint marker ---
        var spawnGo = new GameObject("LobbySpawnPoint");
        spawnGo.transform.position = new Vector3(0, 0, 0);
        SceneManager.MoveGameObjectToScene(spawnGo, lobbyScene);

        // Bind lobbySpawnPoint on ServerDataManager.
        if (serverData != null)
        {
            serverData.lobbySpawnPoint = spawnGo.transform;
            serverData.lobbySceneName = LobbySceneName;
            if (playerPrefab != null) serverData.playerPrefab = playerPrefab;
            if (defaultSpawn != null) serverData.defaultSpawnPoint = defaultSpawn;
            EditorUtility.SetDirty(serverData);
        }

        // --- 5. Add UI_StartScreen canvas + EventSystem to Lobby ---
        var uiGo = new GameObject("UI_StartScreen");
        SceneManager.MoveGameObjectToScene(uiGo, lobbyScene);
        uiGo.AddComponent<UI_StartScreen>();

        var esGo = new GameObject("EventSystem");
        SceneManager.MoveGameObjectToScene(esGo, lobbyScene);
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<StandaloneInputModule>();

        // --- 6. Save the Lobby scene ---
        EditorSceneManager.SaveScene(lobbyScene, LobbyScenePath);

        // --- 7. Close Lobby, save the stripped Game scene ---
        EditorSceneManager.CloseScene(lobbyScene, false);
        EditorSceneManager.SaveScene(gameScene);

        // --- 8. Build Settings: Lobby at index 0, Game at index 1 ---
        EnsureInBuildSettings(LobbyScenePath, true);
        EnsureInBuildSettings(GameScenePath, true);
        // Reorder so Lobby is first (index 0).
        ReorderBuildScenes();

        Debug.Log("[LobbySetup] Done. Lobby scene created at index 0, Game (Scene_multi) at index 1.");
    }

    private static void StripByName(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == name)
            {
                Object.DestroyImmediate(root);
                return;
            }
            var t = root.transform.Find(name);
            if (t != null)
            {
                Object.DestroyImmediate(t.gameObject);
                return;
            }
        }
    }

    private static void EnsureInBuildSettings(string path, bool enabled)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int idx = scenes.FindIndex(s => s.path == path);
        if (idx < 0)
        {
            scenes.Add(new EditorBuildSettingsScene(path, enabled));
        }
        else
        {
            scenes[idx].enabled = enabled;
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void ReorderBuildScenes()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int lobbyIdx = scenes.FindIndex(s => s.path == LobbyScenePath);
        if (lobbyIdx <= 0) return; // already first or absent
        var lobby = scenes[lobbyIdx];
        scenes.RemoveAt(lobbyIdx);
        scenes.Insert(0, lobby);
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
```

- [ ] **Step 2: Verify it compiles**

Run: Unity Editor recompile.
Expected: zero errors. The tool appears under menu `Tools/Lobby Setup/Build Lobby Scene`.

- [ ] **Step 3: Run the tool**

In the Unity Editor, run `Tools/Lobby Setup/Build Lobby Scene`.
Expected: console log `[LobbySetup] Done. Lobby scene created at index 0, Game (Scene_multi) at index 1.` Open `File → Build Settings` and confirm: `Assets/Scenes/Lobby.unity` at index 0, `Assets/Scenes/Scene_multi.unity` at index 1.

- [ ] **Step 4: Manually verify the Lobby scene contents**

Open `Assets/Scenes/Lobby.unity`. Confirm:
- A `NetworkManager` GameObject is present, with its `CustomAuthenticator` child/component (preventMultiLogin = 1) and the Tugboat transport intact.
- `ServerDataManager` is present (either on the NM GameObject or its own), with `playerPrefab` assigned, `lobbySpawnPoint` assigned to the `LobbySpawnPoint`, and `lobbySceneName = "Lobby"`.
- A `UI_StartScreen` GameObject with the `UI_StartScreen` component.
- An `EventSystem`.
- A `LobbySpawnPoint` at origin.

If `ServerDataManager` was originally a separate root object in `Scene_multi` and was NOT found by `FindObjectOfType` (e.g., it was disabled), add it manually to the NM GameObject in the Lobby scene and assign `playerPrefab` + `lobbySpawnPoint`. The tool logs nothing about a missing ServerDataManager, so check explicitly.

- [ ] **Step 5: Manually verify the Game scene is stripped**

Open `Assets/Scenes/Scene_multi.unity`. Confirm:
- No `NetworkManager`, no `NetworkHudCanvas`, no `LoginCanvas`.
- `ServerDataManager` is gone (it moved to Lobby).
- Level geometry, enemies, `ExtractionZone`, `MatchManager`, `GameManager` are still present.

- [ ] **Step 6: Commit**

```bash
git add Assets/Editor/LobbySceneSetupTool.cs Assets/Scenes/Lobby.unity Assets/Scenes/Lobby.unity.meta Assets/Scenes/Scene_multi.unity ProjectSettings/EditorBuildSettings.asset
git commit -m "feat(lobby): build Lobby scene, relocate NetworkManager, set build indices"
```

---

## Task 5: Manual verification (full Phase 0 acceptance)

**Files:** none (verification only).

- [ ] **Step 1: Server start from Lobby**

Open `Assets/Scenes/Lobby.unity`. Press Play. Click `Start Server Only`.
Expected: console shows FishNet server started; no errors. The `UI_StartScreen` remains visible (server-only mode). The NetworkManager object is not destroyed (DontDestroyOnLoad).

- [ ] **Step 2: Host login + spawn**

Stop. Press Play again in `Lobby.unity`. Enter an ID (e.g. `p1`), click `Login as Host`.
Expected: server + client start; authentication succeeds (console: `[Authenticator] ... authenticated successfully as 'p1'`); `UI_StartScreen` hides after ~0.5s; a `Player` object spawns at the `LobbySpawnPoint`. No fatal console errors. (Some gameplay scripts on the Player may log warnings in the Lobby because there is no weapon model / camera rig — these are acceptable in Phase 0 and will be resolved by the dedicated lobby body in Phase 1. If they are *errors* that block spawn, note them.)

- [ ] **Step 3: Client login to a running host (two-editor test)**

With the host running in Editor A (or a host build), open `Lobby.unity` in a second Editor B (or run a second instance). Enter a different ID (`p2`), set the IP to `127.0.0.1` (or the host IP), click `Login as Client`.
Expected: client connects, authenticates, `UI_StartScreen` hides, a `Player` spawns in the Lobby scene for `p2`. The host's Editor A should also observe `p2`'s player object.

- [ ] **Step 4: Persistence across reload (DontDestroyOnLoad sanity)**

Stop all. In the host Editor, press Play, log in as host, confirm the NM survives. (Full lobby↔game scene transition is Phase 3; here we only confirm the NM is not destroyed when stopping play and that no `Destroy(nm.gameObject)` path fires during Phase 0.)

- [ ] **Step 5: Commit verification notes (optional)**

If any Phase 1-blocking issues were found (e.g., Player gameplay scripts throwing hard errors in the Lobby), record them in the commit message of the fix or in the spec's Phase 1 notes. Otherwise no commit needed.

---

## Self-Review (completed)

**Spec coverage (Phase 0 of §6):**
- "建 Lobby 场景" → Task 4. ✓
- "NetworkManager+ServerDataManager 迁入+DontDestroyOnLoad" → Task 4 (move) + Task 2 (DontDestroyOnLoad on ServerDataManager) + Task 1 (DontDestroyOnLoad on UI_StartScreen). The NetworkManager itself is DontDestroyOnLoad by virtue of being moved into Lobby and surviving scene loads; if a `DontDestroyOnLoad` call is required on the NM GameObject specifically, the implementer should add a tiny bootstrap component during Task 4 Step 4 manual verification. Flagged in Task 4 notes.
- "Scene_multi 拆纯 Game" → Task 4 (strip NM/ServerDataManager/LoginCanvas/HUD). ✓
- "移除 demo HUD" → Task 4 (StripByName "NetworkHudCanvas"). ✓
- "新增 UI_StartScreen" → Task 1. ✓
- "生成逻辑按场景分" → Task 2 (ResolveSpawnPoint). ✓
- Phase 0 acceptance "Lobby 起服、登录、生成玩家体" → Task 5. ✓

**Placeholder scan:** No TBD/TODO. All code blocks are complete. The only soft spot is the NetworkManager DontDestroyOnLoad confirmation (flagged as a manual verify step, not a placeholder).

**Type consistency:** `lobbySpawnPoint` / `lobbySceneName` defined in Task 2 are referenced by the tool in Task 4 with matching names. `UI_StartScreen.Instance` produced in Task 1 is not yet consumed (Phase 1+). `ResolveSpawnPoint` defined in Task 2 is the single spawn-decision point.

**Known risks (carried into spec §Risks):**
1. `MoveGameObjectToScene` on the NetworkManager prefab instance should preserve overrides, but if the NM was a nested prefab with added/removed components, verify in Task 4 Step 4 that `CustomAuthenticator` and the Tugboat transport survived the move.
2. `ServerDataManager` may have been a separate root object; the tool handles both cases (moves it if separate). Verify in Task 4 Step 4.
3. The full `Player` prefab spawning in Lobby may produce console warnings from gameplay scripts (no weapon/camera context). Acceptable for Phase 0; Phase 1 introduces the stripped lobby body.
