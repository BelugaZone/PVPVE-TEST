using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Handles all player interaction:
/// - E key / existing action for regular Interactable objects
/// - F key for searching a nearby dead enemy (Enemy_LootContainer)
///
/// Prompt "按 [F] 搜索" is built in code as a World-Space Canvas that is
/// always above the nearest loot container.
/// </summary>
public class Player_Interaction : MonoBehaviour
{
    // ──────────────────────────────────────────
    // Regular interactables (E key)
    // ──────────────────────────────────────────
    private readonly List<Interactable> interactables = new List<Interactable>();
    private Interactable closestInteractable;

    // ──────────────────────────────────────────
    // Loot containers (F key)
    // ──────────────────────────────────────────
    private Enemy_LootContainer closestContainer;
    private const float LOOT_SEARCH_RADIUS = 0.8f; // Must be very close to search

    // ──────────────────────────────────────────
    // UI prompt
    // ──────────────────────────────────────────
    private GameObject       _promptGO;
    private Text             _promptText;
    private UI_CorpseSearch  _searchUI;

    // ──────────────────────────────────────────
    // References
    // ──────────────────────────────────────────
    private Player _player;
    private Camera _cam;

    // ──────────────────────────────────────────
    // Unity lifecycle
    // ──────────────────────────────────────────

    private void Start()
    {
        _player = GetComponent<Player>();

        // Do NOT cache Camera.main here — during scene transitions Cinemachine may not have
        // bound the virtual camera to Main Camera yet. Fetch it lazily in UpdatePromptPosition.

        // E key — existing interaction
        _player.controls.Character.Interaction.performed += _ => InteractWithClosest();

        // Build floating prompt
        BuildSearchPrompt();

        // Build corpse search UI (Screen Space Overlay canvas on this GameObject)
        var searchGO = new GameObject("UI_CorpseSearch");
        searchGO.transform.SetParent(transform, false);
        _searchUI = searchGO.AddComponent<UI_CorpseSearch>();
        _searchUI.Init(_player);
    }

    private void Update()
    {
        if (_player != null && !_player.IsOwner) return;

        UpdateClosestContainer();

        // F key — search loot
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            if (_searchUI != null && _searchUI.IsOpen)
            {
                _searchUI.Close();
            }
            else if (closestContainer != null && closestContainer.HasLoot())
            {
                _searchUI.Open(closestContainer);
            }
        }

        UpdatePromptPosition();
    }

    // ──────────────────────────────────────────
    // Regular interactable handling
    // ──────────────────────────────────────────

    private void InteractWithClosest()
    {
        closestInteractable?.Interaction();
        interactables.Remove(closestInteractable);
        UpdateClosestInteractble();
    }

    public void UpdateClosestInteractble()
    {
        closestInteractable?.HighlightActive(false);
        closestInteractable = null;

        float closest = float.MaxValue;
        foreach (Interactable interactable in interactables)
        {
            float d = Vector3.Distance(transform.position, interactable.transform.position);
            if (d < closest)
            {
                closest              = d;
                closestInteractable  = interactable;
            }
        }
        closestInteractable?.HighlightActive(true);
    }

    public List<Interactable> GetInteracbles() => interactables;

    // ──────────────────────────────────────────
    // Loot container proximity check
    // ──────────────────────────────────────────

    private void UpdateClosestContainer()
    {
        var oldContainer = closestContainer;
        closestContainer = null;
        float closest = float.MaxValue;

        foreach (var c in Enemy_LootContainer.ActiveLootContainers)
        {
            if (c == null || !c.HasLoot()) continue; // Ignore empty containers
            float d = Vector3.Distance(transform.position, c.LootPosition);
            if (d <= LOOT_SEARCH_RADIUS && d < closest)
            {
                closest          = d;
                closestContainer = c;
            }
        }

        // Close UI if we walk away from the container that's currently open
        if (_searchUI != null && _searchUI.IsOpen && closestContainer == null && oldContainer != null)
        {
            _searchUI.Close();
        }

        // Show/hide the search prompt and highlight
        bool showPrompt = closestContainer != null && closestContainer.HasLoot()
                          && (_searchUI == null || !_searchUI.IsOpen);
        if (_promptGO != null) _promptGO.SetActive(showPrompt);

        // Highlight logic
        if (oldContainer != closestContainer)
        {
            if (oldContainer != null) oldContainer.HighlightActive(false);
            if (closestContainer != null) closestContainer.HighlightActive(true);
        }
    }

    // ──────────────────────────────────────────
    // "按 [F] 搜索" prompt — built in code
    // ──────────────────────────────────────────

    private void BuildSearchPrompt()
    {
        // World-space canvas so the prompt floats above the corpse in 3D
        _promptGO = new GameObject("SearchPromptCanvas");
        var canvas = _promptGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        _promptGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Fixed small size in world units
        var rt = _promptGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(180f, 36f);
        rt.localScale = Vector3.one * 0.008f;  // scale down to world size

        // Background pill
        var bgGO = new GameObject("Bg");
        bgGO.transform.SetParent(_promptGO.transform, false);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.05f, 0.05f, 0.05f, 0.78f);
        bgImg.raycastTarget = false;
        var bgRt = bgImg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero; bgRt.anchoredPosition = Vector2.zero;

        // Text
        var txtGO = new GameObject("Label");
        txtGO.transform.SetParent(_promptGO.transform, false);
        _promptText             = txtGO.AddComponent<Text>();
        _promptText.font        = DefaultFont();
        _promptText.fontSize    = 22;
        _promptText.alignment   = TextAnchor.MiddleCenter;
        _promptText.color       = new Color(1f, 0.92f, 0.5f);
        _promptText.raycastTarget = false;
        _promptText.text        = "按 [F] 搜索";
        var txtRt = _promptText.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
        txtRt.sizeDelta = Vector2.zero; txtRt.anchoredPosition = Vector2.zero;

        _promptGO.SetActive(false);
    }

    private void UpdatePromptPosition()
    {
        if (_promptGO == null || !_promptGO.activeSelf) return;
        if (closestContainer == null) { _promptGO.SetActive(false); return; }

        // Float 2 m above the corpse's ragdoll
        Vector3 worldPos = closestContainer.LootPosition + Vector3.up * 2f;
        _promptGO.transform.position = worldPos;

        // Always face the camera (billboard) — fetch Camera.main each frame so we get the
        // Cinemachine-controlled camera even if it wasn't ready at Start().
        Camera cam = Camera.main;
        if (cam != null)
            _promptGO.transform.forward = cam.transform.forward;
    }

    // ──────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────

    private static Font DefaultFont() =>
        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
}
