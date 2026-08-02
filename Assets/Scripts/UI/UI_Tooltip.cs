using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// Simple hover tooltip: a small box that follows the mouse and shows one line of text.
// Lazily created on first Show(). Call Show(string)/Hide().
//
// Layout note: the root GameObject is a ScreenSpaceOverlay Canvas (sortingOrder 2000).
// The Canvas root's RectTransform fills the screen, so the visible box must be a CHILD
// with a fixed sizeDelta (not stretched) — otherwise the bg Image stretches fullscreen.
// We move the child box to follow the cursor; the canvas root stays put.
public class UI_Tooltip : MonoBehaviour
{
    private static UI_Tooltip _instance;
    public static UI_Tooltip Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("UI_Tooltip");
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 2000; // above everything (panel is 200)
                _instance = go.AddComponent<UI_Tooltip>();
                _instance.Build();
            }
            return _instance;
        }
    }

    private RectTransform box;   // the visible small panel (fixed size, follows cursor)
    private Text text;
    private bool built;

    private void Build()
    {
        // Box: fixed size, centered anchor, pivot bottom-left so it grows up/right of cursor.
        var boxGo = new GameObject("Box");
        boxGo.transform.SetParent(transform, false);
        box = boxGo.AddComponent<RectTransform>();
        box.anchorMin = new Vector2(0.5f, 0.5f);
        box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0f, 0f);
        box.anchoredPosition = Vector2.zero;
        box.sizeDelta = new Vector2(160f, 26f);

        var bg = new GameObject("Bg").AddComponent<Image>();
        bg.transform.SetParent(box, false);
        bg.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

        var tgo = new GameObject("Text");
        tgo.transform.SetParent(box, false);
        text = tgo.AddComponent<Text>();
        text.font = GetDefaultFont();
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var trt = text.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8, 3); trt.offsetMax = new Vector2(-8, -3);

        built = true;
        Hide();
    }

    public void Show(string t)
    {
        if (!built) return;
        text.text = t ?? "";
        float w = Mathf.Max(60, Mathf.Min(420, (t?.Length ?? 0) * 8 + 24));
        box.sizeDelta = new Vector2(w, 26);
        gameObject.SetActive(true);
        // Position immediately so it doesn't flash at center for one frame.
        MoveToCursor();
    }

    public void Hide()
    {
        if (this != null && gameObject != null) gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!gameObject.activeSelf) return;
        MoveToCursor();
    }

    private void MoveToCursor()
    {
        Vector2 pos = Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition;
        box.position = new Vector3(pos.x + 14, pos.y + 10, 0);
    }

    private static Font GetDefaultFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
