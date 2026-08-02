using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// An equipment slot (Primary/Secondary/Melee/Grenade). Drop target with type filter.
// Also a drag source so equipped items can be dragged onto empty backpack cells to unequip.
// Implements IItemSlot for hover/tooltip.
public class UI_EquipmentSlot : MonoBehaviour, IDropHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IItemSlot
{
    private PlayerInventory inventory;
    private int slot;
    private Image icon;
    private Text label;

    public void Setup(PlayerInventory inv, int slotIndex)
    {
        inventory = inv;
        slot = slotIndex;
        icon = transform.Find("Icon")?.GetComponent<Image>();
        label = transform.Find("Label")?.GetComponent<Text>();
    }

    public InventoryItem CurrentItem => inventory != null ? inventory.GetEquipment(slot) : null;

    public void Refresh(InventoryItem item)
    {
        if (icon == null) return;
        if (item == null)
        {
            icon.enabled = false;
            return;
        }
        icon.enabled = true;
        icon.sprite = UI_PlaceholderIcons.ForItem(item);
        icon.color = Color.white;
    }

    public void OnDrop(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        if (dm.dragging == null) return;

        if (dm.source is UI_InventorySlot src)
        {
            // Backpack -> equipment
            if (inventory.MoveBackpackToEquip(src.GetIndex(), slot))
            {
                dm.End();
                return;
            }
        }
        else if (dm.source is UI_EquipmentSlot srcEquip)
        {
            // Equipment -> equipment (swap slots). Route through backpack to reuse logic.
            // Capture the target backpack cell explicitly so the unequipped item is the
            // one we re-equip (not whatever LastBackpackIndex happens to find).
            int fromSlot = srcEquip.GetSlot();
            if (fromSlot != slot)
            {
                int target = inventory.FirstEmptyBackpackSlot();
                if (target >= 0
                    && inventory.UnequipToBackpack(fromSlot, target)
                    && inventory.MoveBackpackToEquip(target, slot))
                {
                    dm.End();
                    return;
                }
            }
        }
        // Invalid drop: leave drag active so OnEndDrag snaps back.
    }

    public int GetSlot() => slot;

    public void OnBeginDrag(PointerEventData e)
    {
        InventoryItem item = CurrentItem;
        if (item == null) { e.pointerDrag = null; return; }
        UI_DragManager.Instance.Begin(item, this, UI_PlaceholderIcons.ForItem(item));
        if (icon != null) icon.color = new Color(1, 1, 1, 0.3f);
    }

    public void OnDrag(PointerEventData e) { /* movement handled in UI_DragManager.Update */ }

    public void OnEndDrag(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        dm.End();
        Refresh(CurrentItem);
    }
}
