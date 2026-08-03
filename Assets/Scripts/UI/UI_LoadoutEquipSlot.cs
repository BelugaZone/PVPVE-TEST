using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// A loadout equipment cell (Primary/Secondary/Melee/Tactical). Drop target with weapon-type
// filter (reuses PlayerInventory.IsWeaponAllowedInSlot). Also a drag source -> stash.
public class UI_LoadoutEquipSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IItemSlot
{
    private PlayerStash stash;
    private int slot;
    private Image icon;
    private Text label;

    private static readonly string[] Labels = { "Primary", "Secondary", "Melee", "Tactical" };

    public static string LabelsStatic(int i) { string[] L = { "Primary", "Secondary", "Melee", "Tactical" }; return L[i]; }

    public void Setup(PlayerStash s, int slotIndex)
    {
        stash = s;
        slot = slotIndex;
        icon = transform.Find("Icon")?.GetComponent<Image>();
        label = transform.Find("Label")?.GetComponent<Text>();
        if (label != null) label.text = Labels[slotIndex];
    }

    public InventoryItem CurrentItem => stash != null ? stash.GetLoadoutEquipment(slot) : null;

    public void Refresh(InventoryItem item)
    {
        if (icon == null) return;
        if (item == null) { icon.enabled = false; return; }
        icon.enabled = true;
        icon.sprite = UI_PlaceholderIcons.ForItem(item);
        icon.color = Color.white;
    }

    public void OnDrop(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        if (dm.dragging == null) return;
        if (dm.source is UI_StashSlot src) { if (stash.MoveStashToLoadoutEquip(src.GetIndex(), slot)) dm.End(); }
        else if (dm.source is UI_LoadoutBackpackSlot srcLb) { /* backpack->equip: route via stash? for v1 disallow */ }
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

    public int GetSlot() => slot;
}
