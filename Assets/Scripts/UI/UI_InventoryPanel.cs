using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Builds the Tarkov-style silhouette equipment panel + 6x4 backpack grid in code,
// and refreshes slot icons from PlayerInventory. Compact medium window.
//
// IMPORTANT layout note: this GameObject is the ScreenSpaceOverlay Canvas root. The
// CanvasScaler (ScaleWithScreenSize, 1920x1080) forces the Canvas root's RectTransform
// to the reference resolution every frame — so we must NOT put the visible panel on the
// root (it would be stretched fullscreen). Instead we build a fixed-size "PanelContent"
// child (560x300, centered) that holds the background + slots. The scaler scales the
// root; the child keeps its 560x300 size in the 1920x1080 design space.
//
// Layout:
//   [ Primary  Secondary ]  |  [ 6x4 backpack grid ]
//   [ Melee    Tactical  ]
public class UI_InventoryPanel : MonoBehaviour
{
    private PlayerInventory inventory;
    private readonly List<UI_InventorySlot> backpackSlots = new List<UI_InventorySlot>();
    private readonly UI_EquipmentSlot[] equipSlots = new UI_EquipmentSlot[4];

    private static readonly string[] EquipLabels = { "Primary", "Secondary", "Melee", "Tactical" };

    private const float WindowW = 560f;
    private const float WindowH = 300f;
    private const float EquipPanelW = 200f;
    private const float GridPanelW = 300f;
    private const float SidePad = 20f;

    public void Build(PlayerInventory inv)
    {
        inventory = inv;

        // Canvas root (this GameObject). Scaler forces it to 1920x1080 design space.
        Canvas canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
        }

        // Fixed-size panel content child (NOT stretched) — this is the visible window.
        var content = CreateRect("PanelContent", (RectTransform)transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(WindowW, WindowH));

        var bg = content.gameObject.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.08f, 0.92f);

        // Equipment panel (left), flush left.
        var equipRoot = CreateRect("EquipPanel", content, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(SidePad + EquipPanelW / 2f, 0), new Vector2(EquipPanelW, WindowH - 40));
        BuildEquipPanel(equipRoot);
        AddHeader(content, "装备栏", new Vector2(SidePad + EquipPanelW / 2f, WindowH / 2f - 6f), EquipPanelW);

        // Vertical divider between equipment and backpack.
        float dividerX = SidePad + EquipPanelW + (WindowW - SidePad * 2 - EquipPanelW - GridPanelW) / 2f - WindowW / 2f;
        AddDivider(content, new Vector2(0.5f, 0.5f), new Vector2(2, WindowH - 60), new Vector2(dividerX, 0));

        // Backpack grid (right), flush right.
        var gridRoot = CreateRect("BackpackGrid", content, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-(SidePad + GridPanelW / 2f), 0), new Vector2(GridPanelW, WindowH - 40));
        var dropZoneImg = gridRoot.gameObject.AddComponent<Image>();
        dropZoneImg.color = new Color(0, 0, 0, 0);
        var dropZone = gridRoot.gameObject.AddComponent<UI_GridDropZone>();
        dropZone.Setup(inventory);
        
        BuildGrid(gridRoot);
        AddHeader(content, "背包", new Vector2(-(SidePad + GridPanelW / 2f), WindowH / 2f - 6f), GridPanelW);

        Refresh();
        inventory.OnChanged += Refresh;
    }

    private void BuildEquipPanel(RectTransform parent)
    {
        float cell = 84f;
        float gap = 10f;
        float xL = -(cell + gap) / 2f;
        float xR = (cell + gap) / 2f;
        float yU = (cell + gap) / 2f;
        float yD = -(cell + gap) / 2f;
        Vector2[] positions =
        {
            new Vector2(xL, yU),  // Primary
            new Vector2(xR, yU),  // Secondary
            new Vector2(xL, yD),  // Melee
            new Vector2(xR, yD),  // Grenade
        };
        for (int i = 0; i < 4; i++)
        {
            CreateSlot(parent, "Equip_" + i, positions[i], new Vector2(cell, cell), true, i);
            equipSlots[i] = parent.Find("Equip_" + i)?.GetComponent<UI_EquipmentSlot>();
        }
    }

    private void BuildGrid(RectTransform parent)
    {
        int cols = 6, rows = 4;
        float cell = 44f, gap = 4f;
        float gridW = cols * cell + (cols - 1) * gap;
        float gridH = rows * cell + (rows - 1) * gap;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                float x = -gridW / 2 + c * (cell + gap) + cell / 2;
                float y = gridH / 2 - r * (cell + gap) - cell / 2;
                var slot = CreateSlot(parent, "Slot_" + idx, new Vector2(x, y), new Vector2(cell, cell), false, idx);
                backpackSlots.Add(slot);
            }
        }
    }

    private UI_InventorySlot CreateSlot(RectTransform parent, string name, Vector2 pos, Vector2 size, bool isEquip, int index)
    {
        var rt = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        var bg = new GameObject("Bg").AddComponent<Image>();
        bg.transform.SetParent(rt, false);
        bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.sizeDelta = Vector2.zero; bgRt.anchoredPosition = Vector2.zero;

        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(rt, false);
        var icon = iconGo.AddComponent<Image>();
        icon.raycastTarget = false;
        var iconRt = icon.GetComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one; iconRt.sizeDelta = new Vector2(-6, -6); iconRt.anchoredPosition = Vector2.zero;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(rt, false);
        var label = labelGo.AddComponent<Text>();
        label.font = GetDefaultFont();
        label.fontSize = 10;
        label.alignment = TextAnchor.LowerCenter;
        label.color = new Color(0.8f, 0.8f, 0.8f, 0.9f);
        label.raycastTarget = false;
        label.text = isEquip ? EquipLabels[index] : "";
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0); labelRt.anchorMax = new Vector2(1, 0);
        labelRt.pivot = new Vector2(0.5f, 0); labelRt.sizeDelta = new Vector2(0, 14); labelRt.anchoredPosition = new Vector2(0, 2);

        var countGo = new GameObject("Count");
        countGo.transform.SetParent(rt, false);
        var count = countGo.AddComponent<Text>();
        count.font = GetDefaultFont();
        count.fontSize = 12;
        count.alignment = TextAnchor.UpperRight;
        count.color = Color.white;
        count.raycastTarget = false;
        var countRt = count.GetComponent<RectTransform>();
        countRt.anchorMin = new Vector2(1, 1); countRt.anchorMax = new Vector2(1, 1);
        countRt.pivot = new Vector2(1, 1); countRt.sizeDelta = new Vector2(30, 16); countRt.anchoredPosition = new Vector2(-2, -2);

        UI_InventorySlot backpackSlot = null;
        if (isEquip)
        {
            var eq = rt.gameObject.AddComponent<UI_EquipmentSlot>();
            eq.Setup(inventory, index);
        }
        else
        {
            backpackSlot = rt.gameObject.AddComponent<UI_InventorySlot>();
            backpackSlot.Setup(inventory, index);
        }

        var hover = rt.gameObject.AddComponent<UI_SlotHover>();
        hover.Setup();

        return backpackSlot;
    }

    private void AddDivider(Transform parent, Vector2 anchor, Vector2 size, Vector2 pos)
    {
        var rt = CreateRect("Divider", (RectTransform)parent, anchor, anchor, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UI_PlaceholderIcons.White();
        img.color = new Color(1, 1, 1, 0.6f);
        img.raycastTarget = false;
    }

    // Section title text, anchored at the top-center of its region (pivot bottom-center
    // so the text sits just above the slots, inside the panel).
    private void AddHeader(Transform parent, string title, Vector2 pos, float width)
    {
        var rt = CreateRect("Header_" + title, (RectTransform)parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(width, 20));
        var t = rt.gameObject.AddComponent<Text>();
        t.font = GetDefaultFont();
        t.fontSize = 16;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.UpperCenter;
        t.color = new Color(0.9f, 0.9f, 0.9f, 0.95f);
        t.raycastTarget = false;
        t.text = title;
    }

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

    private static Font GetDefaultFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    public void Refresh()
    {
        if (inventory == null) return;
        var bp = inventory.GetBackpack();
        for (int i = 0; i < backpackSlots.Count; i++)
            backpackSlots[i].Refresh(i < bp.Count ? bp[i] : null);
        for (int i = 0; i < 4; i++)
            if (equipSlots[i] != null) equipSlots[i].Refresh(inventory.GetEquipment(i));
    }

    private void OnDestroy()
    {
        if (inventory != null) inventory.OnChanged -= Refresh;
    }
}
