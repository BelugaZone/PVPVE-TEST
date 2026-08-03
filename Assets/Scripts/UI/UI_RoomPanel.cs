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
            canvas.sortingOrder = 250;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
        }

        // Panel anchored bottom-center. Height 180, bottom edge 20px above screen bottom
        // so the whole panel (incl. buttons) is comfortably on-screen.
        const float panelW = 560f, panelH = 180f, panelBottomGap = 20f;
        var content = CreateRect("PanelContent", (RectTransform)transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0, panelBottomGap + panelH / 2f), new Vector2(panelW, panelH));
        var bg = content.gameObject.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);

        // All children anchored top-center (1,1), positioned by negative y from the panel top.
        // Panel top = y 0 (local). Layout top→bottom: Title, Status, Members, Buttons.
        var title = CreateText("Title", "房间 (Room)", content,
            new Vector2(0.5f, 1f), new Vector2(0, -18f), new Vector2(panelW, 26), 18, true);
        statusText = CreateText("Status", "", content,
            new Vector2(0.5f, 1f), new Vector2(0, -48f), new Vector2(panelW, 22), 14, false);
        membersText = CreateText("Members", "", content,
            new Vector2(0.5f, 1f), new Vector2(0, -74f), new Vector2(panelW, 22), 12, false);

        // Buttons anchored bottom-center (0,0), y=24 (24px above panel bottom). Panel is 180 tall,
        // buttons at y=24 sit well inside. Four buttons spread across 560 width.
        float btnY = 24f;
        createBtn = CreateButton("CreateBtn", "创建房间", content, new Vector2(-195f, btnY), new Vector2(160f, 36));
        joinBtn   = CreateButton("JoinBtn",   "加入房间", content, new Vector2(-65f,  btnY), new Vector2(160f, 36));
        leaveBtn  = CreateButton("LeaveBtn",  "退出房间", content, new Vector2(65f,   btnY), new Vector2(160f, 36));
        startBtn  = CreateButton("StartBtn",  "开始游戏", content, new Vector2(195f,  btnY), new Vector2(160f, 36));

        createBtn.onClick.AddListener(() => { Debug.Log("[RoomPanel] CreateRoom clicked. room=" + (room==null?"NULL":room.gameObject.name)); room?.CreateRoom(); });
        joinBtn.onClick.AddListener(() => { Debug.Log("[RoomPanel] JoinRoom clicked. room=" + (room==null?"NULL":room.gameObject.name)); room?.JoinRoom(); });
        leaveBtn.onClick.AddListener(() => { Debug.Log("[RoomPanel] LeaveRoom clicked. room=" + (room==null?"NULL":room.gameObject.name)); room?.LeaveRoom(); });
        startBtn.onClick.AddListener(() => { Debug.Log("[RoomPanel] StartMatch clicked. room=" + (room==null?"NULL":room.gameObject.name)); room?.StartMatch(); });

        Debug.Log("[RoomPanel] Build done. room=" + (room==null?"NULL":room.gameObject.name) + " RoomManager.Instance=" + (RoomManager.Instance==null?"NULL":RoomManager.Instance.gameObject.name));

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
