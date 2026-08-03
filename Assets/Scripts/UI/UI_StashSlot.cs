using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// A stash grid cell. Drag source + drop target. Calls PlayerStash move methods.
public class UI_StashSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IItemSlot
{
    private PlayerStash stash;
    private int index;
    private Image icon;
    private Text countText;

    public void Setup(PlayerStash s, int idx)
    {
        stash = s;
        index = idx;
        icon = transform.Find("Icon")?.GetComponent<Image>();
        countText = transform.Find("Count")?.GetComponent<Text>();
    }

    public InventoryItem CurrentItem
    {
        get
        {
            var bp = stash.GetStash();
            return index >= 0 && index < bp.Count ? bp[index] : null;
        }
    }

    public void Refresh(InventoryItem item)
    {
        if (icon == null) return;
        if (item == null) { icon.enabled = false; if (countText != null) countText.text = ""; return; }
        icon.enabled = true;
        icon.sprite = UI_PlaceholderIcons.ForItem(item);
        icon.color = Color.white;
        if (countText != null) countText.text = item.stack > 1 ? item.stack.ToString() : "";
    }

    public void OnBeginDrag(PointerEventData e)
    {
        var item = CurrentItem;
        if (item == null) { e.pointerDrag = null; return; }
        UI_DragManager.Instance.Begin(item, this, UI_PlaceholderIcons.ForItem(item));
        if (icon != null) icon.color = new Color(1, 1, 1, 0.3f);
    }
    public void OnDrag(PointerEventData e) { }
    public void OnEndDrag(PointerEventData e) { UI_DragManager.Instance.End(); Refresh(CurrentItem); }

    public void OnDrop(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        if (dm.dragging == null) return;
        if (dm.source is UI_StashSlot src) { if (stash.MoveStashToStash(src.GetIndex(), index)) dm.End(); }
        else if (dm.source is UI_LoadoutEquipSlot srcEq) { if (stash.MoveLoadoutEquipToStash(srcEq.GetSlot(), index)) dm.End(); }
        else if (dm.source is UI_LoadoutBackpackSlot srcLb) { if (stash.MoveLoadoutBackpackToStash(srcLb.GetIndex(), index)) dm.End(); }
    }

    public int GetIndex() => index;
}
