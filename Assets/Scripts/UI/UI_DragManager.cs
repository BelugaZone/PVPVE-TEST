using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Floating icon that follows the cursor during a drag. Lazily created on first use.
public class UI_DragManager : MonoBehaviour
{
    private static UI_DragManager _instance;
    public static UI_DragManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("DragManager");
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000;
                _instance = go.AddComponent<UI_DragManager>();
                var img = new GameObject("Icon").AddComponent<Image>();
                img.transform.SetParent(go.transform, false);
                img.raycastTarget = false;
                var rt = img.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(48, 48);
                _instance.icon = img;
            }
            return _instance;
        }
    }

    public Image icon;
    public InventoryItem dragging;
    public object source;   // the slot that started the drag (for snap-back)

    public void Begin(InventoryItem item, object source, Sprite sprite)
    {
        dragging = item;
        this.source = source;
        icon.sprite = sprite;
        icon.color = new Color(1, 1, 1, 0.85f);
        icon.enabled = true;
    }

    public void End()
    {
        dragging = null;
        source = null;
        icon.enabled = false;
    }

    private void Update()
    {
        if (icon != null && icon.enabled)
        {
            Vector2 pos = Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition;
            icon.transform.position = pos;
        }
    }
}
