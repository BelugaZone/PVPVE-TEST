using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum GridDropZoneType { InventoryBackpack, StashStash, StashBackpack }

public class UI_GridDropZone : MonoBehaviour, IDropHandler
{
    private PlayerInventory inventory;
    private PlayerStash stash;
    private GridDropZoneType zoneType;

    public void Setup(PlayerInventory inv)
    {
        inventory = inv;
        zoneType = GridDropZoneType.InventoryBackpack;
    }

    public void Setup(PlayerStash s, GridDropZoneType type)
    {
        stash = s;
        zoneType = type;
    }

    public void OnDrop(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        if (dm.dragging == null || dm.source == null) return;

        if (zoneType == GridDropZoneType.InventoryBackpack && inventory != null)
        {
            if (dm.source is UI_EquipmentSlot srcEquip)
            {
                if (inventory.UnequipToBackpack(srcEquip.GetSlot(), -1))
                    dm.End();
            }
        }
        else if (zoneType == GridDropZoneType.StashStash && stash != null)
        {
            if (dm.source is UI_LoadoutEquipSlot srcEquip)
            {
                if (stash.MoveLoadoutEquipToStash(srcEquip.GetSlot(), -1))
                    dm.End();
            }
            else if (dm.source is UI_LoadoutBackpackSlot srcBp)
            {
                if (stash.MoveLoadoutBackpackToStash(srcBp.GetIndex(), -1))
                    dm.End();
            }
        }
        else if (zoneType == GridDropZoneType.StashBackpack && stash != null)
        {
            if (dm.source is UI_StashSlot srcStash)
            {
                if (stash.MoveStashToLoadoutBackpack(srcStash.GetIndex(), -1))
                    dm.End();
            }
        }
    }
}
