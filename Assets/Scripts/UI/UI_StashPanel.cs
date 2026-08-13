using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Lobby stash panel: left = 8x4 stash grid, right = 2x2 loadout equip + 6x4 loadout backpack.
// Built procedurally (same style as UI_InventoryPanel). This GameObject is the Canvas root.
public class UI_StashPanel : MonoBehaviour
{
    private PlayerStash stash;
    private readonly List<UI_StashSlot> stashSlots = new List<UI_StashSlot>();
    private readonly UI_LoadoutEquipSlot[] loadoutEquipSlots = new UI_LoadoutEquipSlot[4];
    private readonly List<UI_LoadoutBackpackSlot> loadoutBackpackSlots = new List<UI_LoadoutBackpackSlot>();

    private const float WindowW = 920f;
    private const float WindowH = 560f;
    private const float StashGridW = 440f;
    private const float LoadoutPanelW = 440f;
    private const float SidePad = 20f;

    public void Build(PlayerStash s)
    {
        stash = s;
        Canvas canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
        }

        var content = CreateRect("PanelContent", (RectTransform)transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WindowW, WindowH));
        var bg = content.gameObject.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);

        // Stash grid (left). Header x is center-relative: -(halfWindow - SidePad - halfStash).
        float stashCenterX = -(WindowW / 2f - SidePad - StashGridW / 2f);
        var stashRoot = CreateRect("StashGrid", content, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(SidePad + StashGridW / 2f, 0), new Vector2(StashGridW, WindowH - 80));
        var stashImg = stashRoot.gameObject.AddComponent<Image>();
        stashImg.color = new Color(0, 0, 0, 0);
        var stashDropZone = stashRoot.gameObject.AddComponent<UI_GridDropZone>();
        stashDropZone.Setup(stash, GridDropZoneType.StashStash);
        
        BuildStashGrid(stashRoot);
        AddHeader(content, "仓库 (Stash)", new Vector2(stashCenterX, WindowH / 2f - 20f), StashGridW);

        // Divider
        AddDivider(content, new Vector2(0.5f, 0.5f), new Vector2(2, WindowH - 100), Vector2.zero);

        // Loadout panel (right): equip 2x2 on top, backpack 6x4 below
        float loadoutCenterX = (WindowW / 2f - SidePad - LoadoutPanelW / 2f);
        var loadoutRoot = CreateRect("LoadoutPanel", content, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-(SidePad + LoadoutPanelW / 2f), 0), new Vector2(LoadoutPanelW, WindowH - 80));
        var loadoutImg = loadoutRoot.gameObject.AddComponent<Image>();
        loadoutImg.color = new Color(0, 0, 0, 0);
        var loadoutDropZone = loadoutRoot.gameObject.AddComponent<UI_GridDropZone>();
        loadoutDropZone.Setup(stash, GridDropZoneType.StashBackpack);
        
        BuildLoadoutEquip(loadoutRoot);
        BuildLoadoutBackpack(loadoutRoot);
        AddHeader(content, "配装 (Loadout)", new Vector2(loadoutCenterX, WindowH / 2f - 20f), LoadoutPanelW);

        Refresh();
        stash.OnChanged += Refresh;
    }

    private void BuildStashGrid(RectTransform parent)
    {
        int cols = 8, rows = 4; float cell = 50f, gap = 4f;
        float gw = cols * cell + (cols - 1) * gap, gh = rows * cell + (rows - 1) * gap;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                float x = -gw / 2 + c * (cell + gap) + cell / 2;
                float y = gh / 2 - r * (cell + gap) - cell / 2;
                var rt = CreateSlot(parent, "StashSlot_" + idx, new Vector2(x, y), new Vector2(cell, cell));
                var slot = rt.gameObject.AddComponent<UI_StashSlot>();
                slot.Setup(stash, idx);
                rt.gameObject.AddComponent<UI_SlotHover>().Setup();
                stashSlots.Add(slot);
            }
    }

    private void BuildLoadoutEquip(RectTransform parent)
    {
        // 2x2 equip block centered at y=+110 (top of loadout panel). cell=80, gap=10.
        float cell = 80f, gap = 10f;
        float cx = (cell + gap) / 2f;     // ±45
        float topRow = 155f;              // 110 + 45
        float botRow = 65f;               // 110 - 45
        Vector2[] pos = { new Vector2(-cx, topRow), new Vector2(cx, topRow),
                          new Vector2(-cx, botRow), new Vector2(cx, botRow) };
        for (int i = 0; i < 4; i++)
        {
            var rt = CreateSlot(parent, "LoadoutEquip_" + i, pos[i], new Vector2(cell, cell), label: UI_LoadoutEquipSlot.LabelsStatic(i));
            var slot = rt.gameObject.AddComponent<UI_LoadoutEquipSlot>();
            slot.Setup(stash, i);
            rt.gameObject.AddComponent<UI_SlotHover>().Setup();
            loadoutEquipSlots[i] = slot;
        }
    }

    private void BuildLoadoutBackpack(RectTransform parent)
    {
        // 6x4 backpack grid centered at y=-100 (below equip). cell=44, gap=4.
        int cols = 6, rows = 4; float cell = 44f, gap = 4f;
        float gw = cols * cell + (cols - 1) * gap;
        float cy = -100f;
        float gh = ghBackpack(rows, cell, gap);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                float x = -gw / 2 + c * (cell + gap) + cell / 2;
                float y = cy + gh / 2 - r * (cell + gap) - cell / 2;
                var rt = CreateSlot(parent, "LoadoutBp_" + idx, new Vector2(x, y), new Vector2(cell, cell));
                var slot = rt.gameObject.AddComponent<UI_LoadoutBackpackSlot>();
                slot.Setup(stash, idx);
                rt.gameObject.AddComponent<UI_SlotHover>().Setup();
                loadoutBackpackSlots.Add(slot);
            }
    }

    private float ghBackpack(int rows, float cell, float gap) => rows * cell + (rows - 1) * gap;

    private RectTransform CreateSlot(RectTransform parent, string name, Vector2 pos, Vector2 size, string label = null)
    {
        var rt = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        var bg = new GameObject("Bg").AddComponent<Image>();
        bg.transform.SetParent(rt, false); bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.sizeDelta = Vector2.zero; bgRt.anchoredPosition = Vector2.zero;

        var iconGo = new GameObject("Icon"); iconGo.transform.SetParent(rt, false);
        var icon = iconGo.AddComponent<Image>(); icon.raycastTarget = false;
        var iconRt = icon.GetComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one; iconRt.sizeDelta = new Vector2(-6, -6); iconRt.anchoredPosition = Vector2.zero;

        var labelGo = new GameObject("Label"); labelGo.transform.SetParent(rt, false);
        var lt = labelGo.AddComponent<Text>();
        lt.font = GetDefaultFont(); lt.fontSize = 10; lt.alignment = TextAnchor.LowerCenter;
        lt.color = new Color(0.8f, 0.8f, 0.8f, 0.9f); lt.raycastTarget = false; lt.text = label ?? "";
        var labelRt = lt.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0); labelRt.anchorMax = new Vector2(1, 0);
        labelRt.pivot = new Vector2(0.5f, 0); labelRt.sizeDelta = new Vector2(0, 14); labelRt.anchoredPosition = new Vector2(0, 2);

        var countGo = new GameObject("Count"); countGo.transform.SetParent(rt, false);
        var ct = countGo.AddComponent<Text>();
        ct.font = GetDefaultFont(); ct.fontSize = 12; ct.alignment = TextAnchor.UpperRight;
        ct.color = Color.white; ct.raycastTarget = false;
        var countRt = ct.GetComponent<RectTransform>();
        countRt.anchorMin = new Vector2(1, 1); countRt.anchorMax = new Vector2(1, 1);
        countRt.pivot = new Vector2(1, 1); countRt.sizeDelta = new Vector2(30, 16); countRt.anchoredPosition = new Vector2(-2, -2);
        return rt;
    }

    private void AddDivider(Transform parent, Vector2 anchor, Vector2 size, Vector2 pos)
    {
        var rt = CreateRect("Divider", (RectTransform)parent, anchor, anchor, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UI_PlaceholderIcons.White(); img.color = new Color(1, 1, 1, 0.6f); img.raycastTarget = false;
    }

    private void AddHeader(Transform parent, string title, Vector2 pos, float width)
    {
        var rt = CreateRect("Header_" + title, (RectTransform)parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(width, 20));
        var t = rt.gameObject.AddComponent<Text>();
        t.font = GetDefaultFont(); t.fontSize = 16; t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.UpperCenter; t.color = new Color(0.9f, 0.9f, 0.9f, 0.95f); t.raycastTarget = false; t.text = title;
    }

    private RectTransform CreateRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size; return rt;
    }

    private static Font GetDefaultFont() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

    public void Refresh()
    {
        if (stash == null) return;
        var s = stash.GetStash();
        for (int i = 0; i < stashSlots.Count; i++) stashSlots[i].Refresh(i < s.Count ? s[i] : null);
        for (int i = 0; i < 4; i++) if (loadoutEquipSlots[i] != null) loadoutEquipSlots[i].Refresh(stash.GetLoadoutEquipment(i));
        var lb = stash.GetLoadoutBackpack();
        for (int i = 0; i < loadoutBackpackSlots.Count; i++) loadoutBackpackSlots[i].Refresh(i < lb.Count ? lb[i] : null);
    }

    private void OnDestroy() { if (stash != null) stash.OnChanged -= Refresh; }
}
