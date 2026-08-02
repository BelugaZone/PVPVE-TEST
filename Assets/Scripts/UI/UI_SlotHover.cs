using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Shared interface so UI_SlotHover can read the current item from either slot type
// without knowing which concrete slot it's on.
public interface IItemSlot
{
    InventoryItem CurrentItem { get; }
}

// Hover/click feedback for inventory slots: edge highlight (Outline) + tooltip.
// Attach to a slot GameObject that also has a component implementing IItemSlot.
// The slot's "Bg" Image gets an Outline that toggles on hover; bg tint shifts on press.
public class UI_SlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private IItemSlot slot;
    private Outline highlight;
    private Image bg;

    private static readonly Color BgNormal = new Color(0.2f, 0.2f, 0.2f, 1f);
    private static readonly Color BgPressed = new Color(0.38f, 0.38f, 0.42f, 1f);
    private static readonly Color HighlightColor = new Color(1f, 0.85f, 0.3f, 1f); // warm edge

    public void Setup()
    {
        slot = GetComponent<IItemSlot>();
        bg = transform.Find("Bg")?.GetComponent<Image>();
        if (bg != null)
        {
            highlight = bg.GetComponent<Outline>();
            if (highlight == null)
            {
                highlight = bg.gameObject.AddComponent<Outline>();
                highlight.effectColor = HighlightColor;
                highlight.effectDistance = new Vector2(2.5f, -2.5f);
            }
            highlight.enabled = false;
        }
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (highlight != null) highlight.enabled = true;
        var item = slot?.CurrentItem;
        if (item != null)
            UI_Tooltip.Instance.Show(item.DisplayName);
        else
            UI_Tooltip.Instance.Hide();
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (highlight != null) highlight.enabled = false;
        UI_Tooltip.Instance.Hide();
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (bg != null) bg.color = BgPressed;
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (bg != null) bg.color = BgNormal;
    }
}
