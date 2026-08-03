# Lobby / Stash / Matchflow — Phase 2: Room State Machine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a single-room state machine (`RoomManager`) that lets a host or client create a room, lets others join (max 4), tracks members + owner, transfers ownership on owner disconnect, and provides a room panel UI with create/join/leave/start buttons. Match-start scene transition is stubbed (Phase 3 implements it).

**Architecture:** `RoomManager` is a scene-placed `NetworkBehaviour` in the Lobby scene (own GameObject + NetworkObject, like ServerDataManager) exposing `SyncVar<RoomState> state`, `SyncVar<int> ownerClientId`, `SyncList<int> memberClientIds`. ServerRpcs handle create/join/leave/start. Owner transfer fires on remote-client disconnect. `UI_RoomPanel` is a child of the LobbyPlayer prefab (shown for the owner, like the stash panel) and reads `RoomManager.Instance` syncvars to render buttons + member list. A small editor tool adds `RoomManager` to the Lobby scene and a `RoomPanel` child to the LobbyPlayer prefab.

**Tech Stack:** Unity 2023.1.0f1c1 (URP), FishNet, uGUI (`UnityEngine.UI.Text`). No Unity MCP.

## Global Constraints

- Unity version: `2023.1.0f1c1`. URP active.
- No namespaces; top-level classes. `NetworkBehaviour` components gated to `IsOwner` where client-input is involved.
- No Unity MCP. Scene/prefab changes via an Editor `MenuItem` tool.
- RoomManager is a `NetworkBehaviour` — do NOT call `DontDestroyOnLoad` (FishNet FN0002). It is scene-placed in Lobby (Phase 3 handles cross-scene persistence).
- Reuse uGUI construction style from `UI_StashPanel` (CreateRect, Image, Text with `LegacyRuntime.ttf`).
- `CmdStartMatch` is a validating STUB in Phase 2 (logs "would start match — Phase 3 implements scene transition"). It must NOT load the Game scene or set state to InMatch (avoid a dead-end with no return path yet).
- Max members = 4. Only the owner can start. Only authenticated connections can join.

**Design spec:** `docs/superpowers/specs/2026-08-03-lobby-stash-matchflow-design.md` (§3 room state machine, §6 Phase 2).

**Testing approach:** No test framework. Each task = compiles with zero console errors + manual play-mode verification with ≥2 clients.

---

## File Structure

**New runtime scripts:**
- `Assets/Scripts/Managers/RoomManager.cs` — `NetworkBehaviour`, scene-placed in Lobby. Holds room state + members; ServerRpcs for create/join/leave/start; owner transfer on disconnect. Responsibility: authoritative room state.
- `Assets/Scripts/UI/UI_RoomPanel.cs` — uGUI canvas built at runtime on the LobbyPlayer. Renders create/join/leave/start buttons + member list; reads `RoomManager.Instance`. Responsibility: room UI for the local owner.

**New Editor script:**
- `Assets/Editor/RoomSetupTool.cs` — `[MenuItem("Tools/Lobby Setup/Build Room Assets")]`. Adds a `RoomManager` GameObject (NetworkObject + RoomManager) to the Lobby scene; adds a `RoomPanel` child (UI_RoomPanel) to the LobbyPlayer prefab. Idempotent.

**Modified:** none (RoomManager and UI_RoomPanel are self-contained new files; the setup tool wires them).

---

## Task 1: RoomManager NetworkBehaviour

**Files:**
- Create: `Assets/Scripts/Managers/RoomManager.cs`

**Interfaces:**
- Consumes: `FishNet.Connection.NetworkConnection`, `FishNet.Object.NetworkObject`, `FishNet.Object.Synchronizing.SyncVar/SyncList`, `FishNet.Managing.NetworkManager` (for the disconnect event). `ServerDataManager` is NOT required.
- Produces: `RoomManager.Instance` (static); `RoomState` enum (`Empty, Gathering, InMatch`); `SyncVar<RoomState> State`; `SyncVar<int> OwnerClientId`; `SyncList<int> MemberClientIds`; `SyncVar<int> MaxMembers=4`; `CmdCreateRoom`, `CmdJoinRoom`, `CmdLeaveRoom`, `CmdStartMatch` (stub); server-side `OnRemoteConnectionState` for owner transfer + member removal on disconnect.

- [ ] **Step 1: Write RoomManager.cs**

Create `Assets/Scripts/Managers/RoomManager.cs`:

```csharp
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

// Single-room state machine. Scene-placed in the Lobby. Phase 2: create/join/leave/transfer.
// Phase 3 will implement the actual match-start scene transition (CmdStartMatch is a stub here).
public class RoomManager : NetworkBehaviour
{
    public enum RoomState { Empty, Gathering, InMatch }

    public static RoomManager Instance;

    public readonly SyncVar<RoomState> State = new SyncVar<RoomState>();
    public readonly SyncVar<int> OwnerClientId = new SyncVar<int>(-1);
    public readonly SyncList<int> MemberClientIds = new SyncList<int>();
    public readonly SyncVar<int> MaxMembers = new SyncVar<int>(4);

    // SyncVar change callbacks so clients refresh the UI when state/members change.
    public event System.Action OnRoomChanged;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        State.Value = RoomState.Empty;
        base.NetworkManager.ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (base.NetworkManager != null)
            base.NetworkManager.ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        State.OnChange += _ => OnRoomChanged?.Invoke();
        OwnerClientId.OnChange += _ => OnRoomChanged?.Invoke();
        MemberClientIds.OnChange += _ => OnRoomChanged?.Invoke();
        OnRoomChanged?.Invoke();
    }

    // --- Client requests ---

    public void CreateRoom()
    {
        if (!base.IsOwner) return;
        CmdCreateRoom();
    }

    public void JoinRoom()
    {
        if (!base.IsOwner) return;
        CmdJoinRoom();
    }

    public void LeaveRoom()
    {
        if (!base.IsOwner) return;
        CmdLeaveRoom();
    }

    public void StartMatch()
    {
        if (!base.IsOwner) return;
        CmdStartMatch();
    }

    // --- Server RPCs ---

    [ServerRpc]
    private void CmdCreateRoom()
    {
        if (State.Value != RoomState.Empty)
        {
            Debug.Log("[RoomManager] Cannot create room: room already exists.");
            return;
        }
        int clientId = (int)base.Owner.ClientId;
        State.Value = RoomState.Gathering;
        OwnerClientId.Value = clientId;
        MemberClientIds.Clear();
        MemberClientIds.Add(clientId);
        Debug.Log($"[RoomManager] Room created by client {clientId}.");
    }

    [ServerRpc]
    private void CmdJoinRoom()
    {
        if (State.Value != RoomState.Gathering)
        {
            Debug.Log("[RoomManager] Cannot join: room not open.");
            return;
        }
        if (MemberClientIds.Count >= MaxMembers.Value)
        {
            Debug.Log("[RoomManager] Cannot join: room full.");
            return;
        }
        int clientId = (int)base.Owner.ClientId;
        if (MemberClientIds.Contains(clientId))
        {
            Debug.Log($"[RoomManager] Client {clientId} already in room.");
            return;
        }
        MemberClientIds.Add(clientId);
        Debug.Log($"[RoomManager] Client {clientId} joined. Members: {MemberClientIds.Count}.");
    }

    [ServerRpc]
    private void CmdLeaveRoom()
    {
        int clientId = (int)base.Owner.ClientId;
        RemoveMember(clientId);
    }

    [ServerRpc]
    private void CmdStartMatch()
    {
        if (State.Value != RoomState.Gathering)
        {
            Debug.Log("[RoomManager] Cannot start: room not gathering.");
            return;
        }
        int clientId = (int)base.Owner.ClientId;
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
        // Phase 2 stub: validate only. Phase 3 will set InMatch + load the Game scene.
        Debug.Log($"[RoomManager] StartMatch validated (owner={clientId}, members={MemberClientIds.Count}). Scene transition arrives in Phase 3.");
    }

    // --- Disconnect handling: remove member, transfer/empty room ---

    private void HandleRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState != RemoteConnectionState.Stopped) return;
        int clientId = (int)conn.ClientId;
        if (MemberClientIds.Contains(clientId))
            RemoveMember(clientId);
    }

    private void RemoveMember(int clientId)
    {
        if (!MemberClientIds.Contains(clientId)) return;
        MemberClientIds.Remove(clientId);

        // Owner left: transfer to the earliest remaining member, or empty the room.
        if (clientId == OwnerClientId.Value)
        {
            if (MemberClientIds.Count > 0)
            {
                OwnerClientId.Value = MemberClientIds[0];
                Debug.Log($"[RoomManager] Owner left; transferred ownership to client {MemberClientIds[0]}.");
            }
            else
            {
                OwnerClientId.Value = -1;
                State.Value = RoomState.Empty;
                Debug.Log("[RoomManager] Owner left; room empty.");
            }
        }
    }

    // --- Client-side helpers for the UI ---

    public bool IsLocalOwner()
    {
        if (!base.IsClientStarted) return false;
        return (int)base.LocalConnection.ClientId == OwnerClientId.Value;
    }

    public bool IsLocalMember()
    {
        if (!base.IsClientStarted) return false;
        return MemberClientIds.Contains((int)base.LocalConnection.ClientId);
    }
}
```

- [ ] **Step 2: Verify compile**

Run: Unity recompile.
Expected: zero errors. `SyncVar<T>` and `SyncList<int>` are in `FishNet.Object.Synchronizing`. `base.Owner` and `base.LocalConnection` are FishNet NetworkBehaviour properties. `RemoteConnectionStateArgs` is in `FishNet.Transporting`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Managers/RoomManager.cs
git commit -m "feat(room): add RoomManager state machine (create/join/leave/start stub)"
```

---

## Task 2: UI_RoomPanel

**Files:**
- Create: `Assets/Scripts/UI/UI_RoomPanel.cs`

**Interfaces:**
- Consumes: `RoomManager.Instance` (state, members, IsLocalOwner, IsLocalMember, Create/Join/Leave/StartMatch).
- Produces: `UI_RoomPanel.Build()` constructs the panel and subscribes to `RoomManager.Instance.OnRoomChanged` to refresh. `Refresh()` toggles button visibility by state + membership + ownership.

- [ ] **Step 1: Write UI_RoomPanel.cs**

Create `Assets/Scripts/UI/UI_RoomPanel.cs`. It builds a small canvas panel with a title, a member count, a member list (text), and up to 4 buttons (Create / Join / Leave / Start). Button visibility depends on room state and the local client's role:

```csharp
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// Room panel shown on the LobbyPlayer for the local owner. Reads RoomManager.Instance.
// Button visibility:
//   State.Empty           -> [Create Room]
//   State.Gathering, not member -> [Join Room]
//   State.Gathering, member, not owner -> [Leave Room]  (start disabled)
//   State.Gathering, owner -> [Start Match] [Leave Room]
//   State.InMatch         -> (nothing interactive; match in progress)
public class UI_RoomPanel : MonoBehaviour
{
    private RoomManager room;
    private Text statusText;
    private Text membersText;
    private Button createBtn, joinBtn, leaveBtn, startBtn;

    public void Build()
    {
        room = RoomManager.Instance;
        Canvas canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
        }

        // Panel anchored bottom-center, above where stash panel sits.
        var content = CreateRect("PanelContent", (RectTransform)transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0, 40f), new Vector2(520f, 120f));
        var bg = content.gameObject.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);

        // Title
        var title = CreateText("Title", "房间 (Room)", content,
            new Vector2(0.5f, 1f), new Vector2(0, -16f), new Vector2(520f, 24), 18, true);

        // Status (state + member count)
        statusText = CreateText("Status", "", content,
            new Vector2(0.5f, 1f), new Vector2(0, -42f), new Vector2(520f, 22), 14, false);

        // Members list
        membersText = CreateText("Members", "", content,
            new Vector2(0.5f, 1f), new Vector2(0, -66f), new Vector2(520f, 20), 12, false);

        // Buttons row (bottom of panel)
        float btnY = -92f;
        createBtn = CreateButton("CreateBtn", "创建房间", content, new Vector2(-180f, btnY), new Vector2(150f, 32));
        joinBtn   = CreateButton("JoinBtn",   "加入房间", content, new Vector2(0f,    btnY), new Vector2(150f, 32));
        leaveBtn  = CreateButton("LeaveBtn",  "退出房间", content, new Vector2(0f,    btnY), new Vector2(150f, 32));
        startBtn  = CreateButton("StartBtn",  "开始游戏", content, new Vector2(180f,  btnY), new Vector2(150f, 32));

        createBtn.onClick.AddListener(() => room?.CreateRoom());
        joinBtn.onClick.AddListener(() => room?.JoinRoom());
        leaveBtn.onClick.AddListener(() => room?.LeaveRoom());
        startBtn.onClick.AddListener(() => room?.StartMatch());

        if (room != null)
            room.OnRoomChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (room == null) return;
        bool isOwner = room.IsLocalOwner();
        bool isMember = room.IsLocalMember();
        string stateStr = room.State.Value.ToString();

        statusText.text = $"状态: {stateStr}  成员: {room.MemberClientIds.Count}/{room.MaxMembers.Value}";
        var sb = new StringBuilder("成员: ");
        for (int i = 0; i < room.MemberClientIds.Count; i++)
        {
            int c = room.MemberClientIds[i];
            sb.Append(c == room.OwnerClientId.Value ? $"[{c}房主] " : $"{c} ");
        }
        membersText.text = sb.ToString().TrimEnd();

        // Button visibility
        createBtn.gameObject.SetActive(room.State.Value == RoomManager.RoomState.Empty);
        joinBtn.gameObject.SetActive(room.State.Value == RoomManager.RoomState.Gathering && !isMember);
        leaveBtn.gameObject.SetActive(room.State.Value == RoomManager.RoomState.Gathering && isMember);
        startBtn.gameObject.SetActive(room.State.Value == RoomManager.RoomState.Gathering && isOwner);
    }

    private void OnDestroy()
    {
        if (room != null) room.OnRoomChanged -= Refresh;
    }

    // --- uGUI helpers (mirror UI_StashPanel style) ---

    private RectTransform CreateRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    private Text CreateText(string name, string content, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, bool bold)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.text = content; t.fontSize = fontSize; t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.raycastTarget = false;
        return t;
    }

    private Button CreateButton(string name, string label, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f); rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.45f, 0.8f, 1f);
        var btn = go.AddComponent<Button>();
        var c = btn.colors;
        c.highlightedColor = new Color(0.25f, 0.55f, 0.9f, 1f);
        c.pressedColor = new Color(0.1f, 0.3f, 0.6f, 1f);
        btn.colors = c;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, false);
        var lrt = labelGo.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.sizeDelta = Vector2.zero; lrt.anchoredPosition = Vector2.zero;
        var t = labelGo.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.text = label; t.fontSize = 14; t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
        return btn;
    }
}
```

- [ ] **Step 2: Verify compile**

Run: Unity recompile.
Expected: zero errors. `RoomManager.Instance`, `RoomState`, `State.Value`, `MemberClientIds`, `MaxMembers.Value`, `IsLocalOwner`, `IsLocalMember`, `Create/Join/Leave/StartMatch`, `OnRoomChanged` all match Task 1's output.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/UI_RoomPanel.cs
git commit -m "feat(room): add UI_RoomPanel (create/join/leave/start + member list)"
```

---

## Task 3: RoomSetupTool — add RoomManager to Lobby + RoomPanel to LobbyPlayer

**Files:**
- Create: `Assets/Editor/RoomSetupTool.cs`

**Interfaces:**
- Consumes: `RoomManager`, `UI_RoomPanel`, the Lobby scene (`Assets/Scenes/Lobby.unity`), the LobbyPlayer prefab (`Assets/Prefab/LobbyPlayer.prefab`).
- Produces: a `RoomManager` GameObject (NetworkObject + RoomManager) in the Lobby scene; a `RoomPanel` child (UI_RoomPanel) on the LobbyPlayer prefab.

- [ ] **Step 1: Write RoomSetupTool.cs**

Create `Assets/Editor/RoomSetupTool.cs`:

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FishNet.Object;

public static class RoomSetupTool
{
    private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
    private const string LobbyPlayerPrefabPath = "Assets/Prefab/LobbyPlayer.prefab";

    [MenuItem("Tools/Lobby Setup/Build Room Assets")]
    public static void BuildRoomAssets()
    {
        // 1. Add RoomManager to the Lobby scene (if absent).
        var lobbyScene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
        if (Object.FindObjectOfType<RoomManager>(true) == null)
        {
            var go = new GameObject("RoomManager");
            go.AddComponent<NetworkObject>();
            go.AddComponent<RoomManager>();
            EditorSceneManager.MarkSceneDirty(lobbyScene);
            Debug.Log("[RoomSetup] Added RoomManager to Lobby scene.");
        }
        else
        {
            Debug.Log("[RoomSetup] RoomManager already present in Lobby scene.");
        }
        EditorSceneManager.SaveScene(lobbyScene);

        // 2. Add a RoomPanel child (UI_RoomPanel) to the LobbyPlayer prefab (if absent).
        var root = PrefabUtility.LoadPrefabContents(LobbyPlayerPrefabPath);
        try
        {
            bool found = false;
            foreach (Transform child in root.transform)
                if (child.name == "RoomPanel") { found = true; break; }
            if (!found)
            {
                var panelGo = new GameObject("RoomPanel");
                panelGo.transform.SetParent(root.transform, false);
                panelGo.AddComponent<UI_RoomPanel>();
                PrefabUtility.SaveAsPrefabAsset(root, LobbyPlayerPrefabPath);
                Debug.Log("[RoomSetup] Added RoomPanel child to LobbyPlayer prefab.");
            }
            else
            {
                Debug.Log("[RoomSetup] RoomPanel already present on LobbyPlayer prefab.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[RoomSetup] Done.");
    }
}
#endif
```

- [ ] **Step 2: Verify compile**

Run: Unity recompile.
Expected: zero errors. Menu `Tools/Lobby Setup/Build Room Assets` appears.

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/RoomSetupTool.cs
git commit -m "feat(room): RoomSetupTool (RoomManager in Lobby + RoomPanel on LobbyPlayer)"
```

---

## Task 4: Show the room panel on the LobbyPlayer (PlayerStash.OnStartClient wiring)

**Files:**
- Modify: `Assets/Scripts/Player/PlayerStash.cs` (OnStartClient — also build/show the RoomPanel child for the owner)

**Interfaces:**
- Consumes: `UI_RoomPanel.Build()` (no args; reads `RoomManager.Instance`).
- Produces: the owner's LobbyPlayer now shows both the stash panel and the room panel.

- [ ] **Step 1: Extend OnStartClient to also build the room panel**

In `Assets/Scripts/Player/PlayerStash.cs`, inside `OnStartClient`, within the `if (base.IsOwner)` block, after the stash panel is built, add:

```csharp
            // Build + show the room panel for the owning client.
            var roomPanel = GetComponentInChildren<UI_RoomPanel>(true);
            if (roomPanel != null)
            {
                roomPanel.gameObject.SetActive(true);
                roomPanel.Build();
            }
```

The full `if (base.IsOwner)` block becomes:

```csharp
        if (base.IsOwner)
        {
            var panel = GetComponentInChildren<UI_StashPanel>(true);
            if (panel != null)
            {
                panel.gameObject.SetActive(true);
                panel.Build(this);
            }
            var roomPanel = GetComponentInChildren<UI_RoomPanel>(true);
            if (roomPanel != null)
            {
                roomPanel.gameObject.SetActive(true);
                roomPanel.Build();
            }
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }
```

- [ ] **Step 2: Verify compile**

Run: Unity recompile.
Expected: zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Player/PlayerStash.cs
git commit -m "feat(room): show room panel on LobbyPlayer for owner"
```

---

## Task 5: Manual verification (full Phase 2 acceptance)

**Files:** none (verification only).

- [ ] **Step 1: Run RoomSetupTool**

In Unity, run `Tools → Lobby Setup/Build Room Assets`. Confirm:
- The Lobby scene has a new `RoomManager` GameObject (with NetworkObject + RoomManager).
- The LobbyPlayer prefab has a new `RoomPanel` child (with UI_RoomPanel).

- [ ] **Step 2: Single-client create/start stub**

Open `Lobby.unity` → Play → `Login as Host` (ID `hostA`). Expected:
- Both the stash panel and the room panel appear.
- Room panel shows "状态: Empty 成员: 0/4" and a "创建房间" button.
- Click "创建房间" → status becomes "Gathering 成员: 1/4", member list shows "[0房主]", buttons switch to "开始游戏" + "退出房间".
- Click "开始游戏" → console logs `[RoomManager] StartMatch validated (owner=0, members=1). Scene transition arrives in Phase 3.` (state stays Gathering — stub).

- [ ] **Step 3: Two-client create + join**

With host `hostA` running (room in Gathering), open a second Editor/build, `Login as Client` (IP 127.0.0.1, ID `clientB`). Expected:
- `clientB`'s room panel shows "Gathering 成员: 1/4" and a "加入房间" button.
- Click "加入房间" → member count becomes 2/4, member list shows both clients.
- `hostA`'s panel updates to show 2 members (SyncList replicated).
- `clientB` sees "退出房间" (member, not owner) and no start button.

- [ ] **Step 4: Owner transfer**

With both in the room, have `hostA` (owner) disconnect (stop its play mode or close). Expected:
- Server logs `[RoomManager] Owner left; transferred ownership to client <clientB's id>.`
- `clientB`'s panel now shows itself as 房主 and the "开始游戏" button appears.

- [ ] **Step 5: Leave + empty**

Have the last member click "退出房间". Expected:
- Server logs `[RoomManager] Owner left; room empty.`
- State returns to Empty; "创建房间" button reappears.

- [ ] **Step 6: Max-members cap**

Create a room, join with 3 more clients (4 total). A 5th client's "加入房间" should be rejected (console: `room full`), button stays.

- [ ] **Step 7: Commit if all pass**

If all steps pass, Phase 2 is complete.

---

## Self-Review (completed)

**Spec coverage (Phase 2 of §6):**
- "RoomManager+CmdCreate/Join/Leave/Start" → Task 1. ✓
- "UI_RoomPanel" → Task 2. ✓
- "房主转让" → Task 1 (RemoveMember owner-transfer). ✓
- "多端建房、加入(≤4)、转让、退出" → Task 5. ✓

**Placeholder scan:** No TBD/TODO. `CmdStartMatch` is intentionally a validating stub (Phase 3 implements scene transition) — documented, not a placeholder. All code blocks complete.

**Type consistency:** `RoomManager.RoomState`, `State.Value`, `OwnerClientId.Value`, `MemberClientIds`, `MaxMembers.Value`, `IsLocalOwner()`, `IsLocalMember()`, `Create/Join/Leave/StartMatch`, `OnRoomChanged` — all match between Task 1 (RoomManager) and Task 2 (UI_RoomPanel). `UI_RoomPanel.Build()` (no-arg) matches Task 4's call site.

**Known risks:**
1. `RoomManager` is scene-placed; if the Lobby scene is reloaded (Phase 3 return-to-lobby), the room state resets. Acceptable for Phase 2 (no scene transitions yet); Phase 3 addresses persistence.
2. `(int)base.Owner.ClientId` / `(int)base.LocalConnection.ClientId` — FishNet `ClientId` is `int`; the cast is safe. Verify in Task 5.
3. `UI_RoomPanel.Build()` reads `RoomManager.Instance` — on the owner client, `RoomManager` (scene object) must be spawned before `PlayerStash.OnStartClient` runs. FishNet spawns scene objects during scene load, which precedes player spawn, so `Instance` should be set. If `RoomManager.Instance` is null at build time, the panel shows but buttons do nothing — verify in Task 5.
