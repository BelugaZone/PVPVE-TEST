using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Corpse search UI — a compact loot grid.
///
/// Opened by Player_Interaction when the player presses F near a dead enemy.
/// Reads Enemy_LootContainer.lootSlots (a SyncList) so it always reflects the
/// authoritative server state, including after disconnect / reconnect.
///
/// Double-click a slot → CmdTakeLootSlot(slotIndex) on the container (ServerRpc).
/// ESC closes the panel.
///
/// Layout (built in code, same style as UI_InventoryPanel):
///   Title bar "搜索 [敌人名]"
///   2 rows × 4 columns grid  (up to 8 loot slots)
///   Bottom hint: "双击拿走  •  ESC 关闭"
/// </summary>
public class UI_CorpseSearch : MonoBehaviour
{
    // ──────────────────────────────────────────
    // Constants & layout
    // ──────────────────────────────────────────
    private const float PanelW       = 400f;
    private const float PanelH       = 240f;
    private const int   Cols         = 4;
    private const int   Rows         = 2;
    private const float CellSize     = 64f;
    private const float CellGap      = 6f;
    private const float FadeDuration = 0.15f;
    private const float DblClickTime = 0.35f;

    // ──────────────────────────────────────────
    // Runtime state
    // ──────────────────────────────────────────
    private Enemy_LootContainer _container;
    private Player              _player;

    private CanvasGroup         _group;
    private bool                _isOpen;
    private Coroutine           _fadeRoutine;

    private SlotCell[]          _cells = new SlotCell[Cols * Rows];

    // Double-click tracking
    private int   _lastClickedSlot = -1;
    private float _lastClickTime   = -1f;

    // ──────────────────────────────────────────
    // Nested helper: one grid slot
    // ──────────────────────────────────────────
    private class SlotCell
    {
        public int     slotIndex;
        public Image   bg;
        public Image   icon;
        public Text    nameText;
        public Text    countText;
        public GameObject root;
    }

    // ──────────────────────────────────────────
    // Hover Helper
    // ──────────────────────────────────────────
    private class UI_CorpseSlotHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
    {
        private UI_CorpseSearch ui;
        private int slotIndex;
        private Outline highlight;
        private Image bg;

        public void Setup(UI_CorpseSearch searchUI, int idx)
        {
            ui = searchUI;
            slotIndex = idx;
            bg = GetComponent<Image>(); // The root is the bg
            if (bg != null)
            {
                highlight = bg.gameObject.AddComponent<Outline>();
                highlight.effectColor = new Color(1f, 0.85f, 0.3f, 1f);
                highlight.effectDistance = new Vector2(2.5f, -2.5f);
                highlight.enabled = false;
            }
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e)
        {
            if (highlight != null) highlight.enabled = true;
            if (ui != null && ui._container != null)
            {
                // Only show tooltip if slot is not empty and not taken
                if (slotIndex >= 0 && slotIndex < ui._container.lootSlots.Count)
                {
                    var slot = ui._container.lootSlots[slotIndex];
                    if (!string.IsNullOrEmpty(slot.itemId) && !slot.taken)
                    {
                        string name = ui._container.GetDisplayName(slotIndex);
                        if (!string.IsNullOrEmpty(name))
                            UI_Tooltip.Instance.Show(name);
                    }
                }
            }
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e)
        {
            if (highlight != null) highlight.enabled = false;
            if (UI_Tooltip.Instance != null) UI_Tooltip.Instance.Hide();
        }

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e)
        {
            if (bg != null) bg.color = new Color(0.38f, 0.38f, 0.42f, 1f);
        }

        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e)
        {
            if (bg != null) bg.color = new Color(0.18f, 0.18f, 0.18f, 1f);
        }
    }

    // ──────────────────────────────────────────
    // Public API called by Player_Interaction
    // ──────────────────────────────────────────

    public void Init(Player player)
    {
        _player = player;
        BuildUI();
        SetVisible(false, instant: true);
    }

    public void Open(Enemy_LootContainer container)
    {
        if (_isOpen && _container == container) return;

        // Detach from old container
        if (_container != null)
            _container.OnLootChanged -= RefreshGrid;

        _container               = container;
        _container.OnLootChanged += RefreshGrid;

        RefreshGrid();
        SetVisible(true);

        // Unlock cursor for clicking
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        if (_player.controls != null) _player.controls.Character.Disable();
    }

    public void Close()
    {
        if (!_isOpen) return;

        if (_container != null)
        {
            _container.OnLootChanged -= RefreshGrid;
            _container = null;
        }

        SetVisible(false);

        // Restore normal play state (Top-down shooter requires cursor to be visible and unlocked)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        if (_player.controls != null) _player.controls.Character.Enable();
    }

    public bool IsOpen => _isOpen;

    // ──────────────────────────────────────────
    // Unity Update — ESC closes
    // ──────────────────────────────────────────

    private void Update()
    {
        if (_isOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Close();
    }

    // ──────────────────────────────────────────
    // Build the panel UI (all in code)
    // ──────────────────────────────────────────

    private void BuildUI()
    {
        // ── Canvas ──────────────────────────────
        var canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 210;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
        }

        // ── CanvasGroup (for fade) ──────────────
        _group = gameObject.GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

        // ── Panel root ──────────────────────────
        var panel = MakeRect("SearchPanel", (RectTransform)transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 80f),           // slightly above center
            new Vector2(PanelW, PanelH));

        var panelBg = panel.gameObject.AddComponent<Image>();
        panelBg.color = new Color(0.07f, 0.07f, 0.07f, 0.94f);

        // ── Title ───────────────────────────────
        var titleRt = MakeRect("Title", panel,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -14f), new Vector2(PanelW - 20f, 26f));
        var titleTxt = titleRt.gameObject.AddComponent<Text>();
        titleTxt.font      = DefaultFont();
        titleTxt.fontSize  = 17;
        titleTxt.fontStyle = FontStyle.Bold;
        titleTxt.alignment = TextAnchor.MiddleCenter;
        titleTxt.color     = new Color(0.95f, 0.85f, 0.5f);
        titleTxt.text      = "搜索尸体";
        titleTxt.raycastTarget = false;

        // ── Thin divider under title ─────────────
        var div = MakeRect("TitleDiv", panel,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -28f), new Vector2(PanelW - 30f, 1f));
        var divImg = div.gameObject.AddComponent<Image>();
        divImg.color = new Color(1f, 1f, 1f, 0.18f);
        divImg.raycastTarget = false;

        // ── Grid ────────────────────────────────
        float gridW  = Cols * CellSize + (Cols - 1) * CellGap;
        float gridH  = Rows * CellSize + (Rows - 1) * CellGap;
        var gridRoot = MakeRect("Grid", panel,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 10f), new Vector2(gridW, gridH));

        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                int idx = r * Cols + c;
                float x = -gridW / 2f + c * (CellSize + CellGap) + CellSize / 2f;
                float y =  gridH / 2f - r * (CellSize + CellGap) - CellSize / 2f;
                _cells[idx] = BuildCell(gridRoot, idx, new Vector2(x, y));
            }
        }

        // ── Bottom hint ─────────────────────────
        var hintRt = MakeRect("Hint", panel,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 16f), new Vector2(PanelW - 20f, 20f));
        var hintTxt = hintRt.gameObject.AddComponent<Text>();
        hintTxt.font      = DefaultFont();
        hintTxt.fontSize  = 12;
        hintTxt.alignment = TextAnchor.MiddleCenter;
        hintTxt.color     = new Color(0.7f, 0.7f, 0.7f, 0.85f);
        hintTxt.text      = "双击拿走物品  •  按 ESC 关闭";
        hintTxt.raycastTarget = false;
    }

    private SlotCell BuildCell(RectTransform parent, int idx, Vector2 pos)
    {
        var rt = MakeRect("Slot_" + idx, parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            pos, new Vector2(CellSize, CellSize));

        // Background
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.18f, 0.18f, 0.18f, 1f);

        // Icon
        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(rt, false);
        var icon   = iconGo.AddComponent<Image>();
        icon.raycastTarget = false;
        icon.enabled       = false;
        var iconRt = icon.GetComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one;
        iconRt.sizeDelta = new Vector2(-6f, -6f); iconRt.anchoredPosition = Vector2.zero;

        // Name label (bottom)
        var nameGo  = new GameObject("Name");
        nameGo.transform.SetParent(rt, false);
        var nameTxt = nameGo.AddComponent<Text>();
        nameTxt.font      = DefaultFont();
        nameTxt.fontSize  = 9;
        nameTxt.alignment = TextAnchor.LowerCenter;
        nameTxt.color     = new Color(0.9f, 0.9f, 0.9f, 0.9f);
        nameTxt.raycastTarget = false;
        var nameRt = nameTxt.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0, 0); nameRt.anchorMax = new Vector2(1, 0);
        nameRt.pivot = new Vector2(0.5f, 0f); nameRt.sizeDelta = new Vector2(0, 18f);
        nameRt.anchoredPosition = new Vector2(0, 2f);

        // Count badge (top-right)
        var countGo  = new GameObject("Count");
        countGo.transform.SetParent(rt, false);
        var countTxt = countGo.AddComponent<Text>();
        countTxt.font      = DefaultFont();
        countTxt.fontSize  = 12;
        countTxt.alignment = TextAnchor.UpperRight;
        countTxt.color     = Color.white;
        countTxt.raycastTarget = false;
        var countRt = countTxt.GetComponent<RectTransform>();
        countRt.anchorMin = new Vector2(1, 1); countRt.anchorMax = new Vector2(1, 1);
        countRt.pivot = new Vector2(1, 1); countRt.sizeDelta = new Vector2(30, 16f);
        countRt.anchoredPosition = new Vector2(-2f, -2f);

        // Click handler
        var handler = rt.gameObject.AddComponent<LootSlotClickHandler>();
        handler.Setup(idx, this);

        // Hover effect
        var hover = rt.gameObject.AddComponent<UI_CorpseSlotHover>();
        hover.Setup(this, idx);

        var cell = new SlotCell
        {
            slotIndex = idx,
            bg        = bg,
            icon      = icon,
            nameText  = nameTxt,
            countText = countTxt,
            root      = rt.gameObject
        };
        return cell;
    }

    // ──────────────────────────────────────────
    // Grid refresh
    // ──────────────────────────────────────────

    private void RefreshGrid()
    {
        if (_container == null) return;

        int slotCount = _container.lootSlots.Count;

        for (int i = 0; i < _cells.Length; i++)
        {
            var cell = _cells[i];
            bool hasloot = i < slotCount && !_container.lootSlots[i].taken
                           && !string.IsNullOrEmpty(_container.lootSlots[i].itemId);

            cell.icon.enabled = hasloot;
            cell.bg.color     = hasloot
                ? new Color(0.18f, 0.18f, 0.18f, 1f)
                : new Color(0.10f, 0.10f, 0.10f, 0.6f);

            if (hasloot)
            {
                cell.icon.sprite   = _container.GetIcon(i);
                cell.icon.color    = Color.white;
                cell.nameText.text = _container.GetDisplayName(i);
                int cnt = _container.lootSlots[i].count;
                cell.countText.text = cnt > 1 ? cnt.ToString() : "";
            }
            else
            {
                cell.icon.sprite    = null;
                cell.icon.color     = Color.clear;
                cell.nameText.text  = "";
                cell.countText.text = "";
            }
        }
    }

    // ──────────────────────────────────────────
    // Slot click (called by LootSlotClickHandler)
    // ──────────────────────────────────────────

    public void OnSlotClick(int slotIndex)
    {
        float now = Time.unscaledTime;
        bool isDoubleClick = (slotIndex == _lastClickedSlot)
                             && (now - _lastClickTime < DblClickTime);

        _lastClickedSlot = slotIndex;
        _lastClickTime   = now;

        if (!isDoubleClick) return;

        // Double-click → request server to transfer item via player's own ServerRpc
        if (_container == null) return;
        if (slotIndex < 0 || slotIndex >= _container.lootSlots.Count) return;
        if (_container.lootSlots[slotIndex].taken) return;
        if (_player == null || _player.inventory == null) return;

        // Call ServerRpc on player's OWN inventory (player owns it → reliable routing).
        // Server validates, transfers item, and updates the SyncList.
        // OnLootChanged fires → RefreshGrid() updates the UI correctly.
        _player.inventory.CmdTakeLootFromEnemy(_container.NetworkObject, slotIndex);
    }


    // ──────────────────────────────────────────
    // Visibility helpers
    // ──────────────────────────────────────────

    private void SetVisible(bool show, bool instant = false)
    {
        _isOpen = show;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);

        if (instant)
        {
            _group.alpha          = show ? 1f : 0f;
            _group.interactable   = show;
            _group.blocksRaycasts = show;
            gameObject.SetActive(show);
            return;
        }

        if (show)
        {
            gameObject.SetActive(true);
            _group.interactable   = false;
            _group.blocksRaycasts = false;
            _fadeRoutine = StartCoroutine(Fade(0f, 1f, true));
        }
        else
        {
            _group.interactable   = false;
            _group.blocksRaycasts = false;
            _fadeRoutine = StartCoroutine(Fade(_group.alpha, 0f, false));
        }
    }

    private IEnumerator Fade(float from, float to, bool enableAtEnd)
    {
        float t = 0f;
        _group.alpha = from;
        while (t < FadeDuration)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(from, to, t / FadeDuration);
            yield return null;
        }
        _group.alpha = to;
        if (to <= 0f)
        {
            gameObject.SetActive(false);
        }
        else if (enableAtEnd)
        {
            _group.interactable   = true;
            _group.blocksRaycasts = true;
        }
    }

    // ──────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────

    private RectTransform MakeRect(string name, RectTransform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    private static Font DefaultFont() =>
        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
}

/// <summary>
/// Thin MonoBehaviour that sits on each loot slot GameObject to capture pointer clicks.
/// Forwards to UI_CorpseSearch so we keep the grid logic in one place.
/// </summary>
public class LootSlotClickHandler : MonoBehaviour, IPointerClickHandler
{
    private int           _slotIndex;
    private UI_CorpseSearch _owner;

    public void Setup(int slotIndex, UI_CorpseSearch owner)
    {
        _slotIndex = slotIndex;
        _owner     = owner;
    }

    public void OnPointerClick(PointerEventData e)
    {
        _owner?.OnSlotClick(_slotIndex);
    }
}
