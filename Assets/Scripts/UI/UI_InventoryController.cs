using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Toggles the inventory panel on B / Esc with a fade in/out transition (CanvasGroup
// alpha). While open: unlock cursor + disable gameplay input (Tarkov-style freeze).
// Ensures an EventSystem exists for drag/hover to work.
public class UI_InventoryController : MonoBehaviour
{
    private const float FadeDuration = 0.18f;

    private Player player;
    private GameObject panel;
    private CanvasGroup group;
    private bool isOpen;
    private Coroutine fadeRoutine;

    public void Init(Player player, GameObject panelPrefab)
    {
        this.player = player;
        EnsureEventSystem();

        // Instantiate panel as a sibling under this transform, hidden by default.
        panel = Instantiate(panelPrefab, transform);
        group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        panel.GetComponent<UI_InventoryPanel>().Build(player.inventory);
        panel.SetActive(false);
    }

    private void Update()
    {
        if (player == null || panel == null) return;

        bool toggle = Keyboard.current.bKey.wasPressedThisFrame
                      || (isOpen && Keyboard.current.escapeKey.wasPressedThisFrame);
        if (toggle)
        {
            if (isOpen) Close();
            else Open();
        }
    }

    private void Open()
    {
        isOpen = true;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        panel.SetActive(true);
        // Non-interactive during the fade so clicks don't land mid-transition.
        group.interactable = false;
        group.blocksRaycasts = false;
        fadeRoutine = StartCoroutine(FadeRoutine(0f, 1f, true));

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (player.controls != null) player.controls.Character.Disable();
        panel.GetComponent<UI_InventoryPanel>().Refresh();
    }

    private void Close()
    {
        isOpen = false;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        // Stop accepting input immediately; hide after the fade.
        group.interactable = false;
        group.blocksRaycasts = false;
        fadeRoutine = StartCoroutine(FadeRoutine(group.alpha, 0f, false));

        // This is a top-down/3rd-person shooter that aims via mouse position
        // (Camera.ScreenPointToRay), so the cursor stays visible + unlocked during
        // normal play. Restore that state — do NOT lock the cursor here.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (player.controls != null) player.controls.Character.Enable();
        UI_Tooltip.Instance.Hide();
    }

    private IEnumerator FadeRoutine(float from, float to, bool enableInteractableAtEnd)
    {
        float t = 0f;
        group.alpha = from;
        while (t < FadeDuration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, t / FadeDuration);
            yield return null;
        }
        group.alpha = to;
        if (to <= 0f)
        {
            panel.SetActive(false);
        }
        else if (enableInteractableAtEnd)
        {
            group.interactable = true;
            group.blocksRaycasts = true;
        }
    }

    private void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }
}
