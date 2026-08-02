using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// A backpack grid cell. Draggable source. Implements IItemSlot for hover/tooltip.
public class UI_InventorySlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IItemSlot, IDropHandler, IPointerClickHandler
{
    private PlayerInventory inventory;
    private int index;

    private Image icon;
    private Text countText;

    public void Setup(PlayerInventory inv, int idx)
    {
        inventory = inv;
        index = idx;
        icon = transform.Find("Icon")?.GetComponent<Image>();
        countText = transform.Find("Count")?.GetComponent<Text>();
    }

    public InventoryItem CurrentItem
    {
        get
        {
            var bp = inventory.GetBackpack();
            return index >= 0 && index < bp.Count ? bp[index] : null;
        }
    }

    public void Refresh(InventoryItem item)
    {
        if (icon == null) return;
        if (item == null)
        {
            icon.enabled = false;
            if (countText != null) countText.text = "";
            return;
        }
        icon.enabled = true;
        icon.sprite = UI_PlaceholderIcons.ForItem(item);
        icon.color = Color.white;
        if (countText != null)
            countText.text = item.stack > 1 ? item.stack.ToString() : "";
    }

    public void OnBeginDrag(PointerEventData e)
    {
        var item = CurrentItem;
        if (item == null) { e.pointerDrag = null; return; }
        UI_DragManager.Instance.Begin(item, this, UI_PlaceholderIcons.ForItem(item));
        if (icon != null) icon.color = new Color(1, 1, 1, 0.3f);
    }

    public void OnDrag(PointerEventData e)
    {
        // movement handled in UI_DragManager.Update
    }

    public void OnDrop(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        if (dm.dragging == null) return;

        if (dm.source is UI_EquipmentSlot srcEquip)
        {
            // Equipment -> Backpack
            if (inventory.UnequipToBackpack(srcEquip.GetSlot(), index))
            {
                dm.End();
                return;
            }
        }
        else if (dm.source is UI_InventorySlot srcInv)
        {
            // Backpack -> Backpack
            if (inventory.MoveBackpackToBackpack(srcInv.GetIndex(), index))
            {
                dm.End();
                return;
            }
        }
    }

    public void OnEndDrag(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        dm.End();
        Refresh(CurrentItem);
    }

    public int GetIndex() => index;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            var item = CurrentItem;
            if (item != null && item.ItemType == ItemType.Consumable)
            {
                inventory.UseItem(index);
            }
        }
    }
}
