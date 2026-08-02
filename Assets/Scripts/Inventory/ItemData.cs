using UnityEngine;

public enum ItemType { Weapon, Ammo, Consumable }

[CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("Identity")]
    public string itemId;
    public string itemName;
    public ItemType itemType;
    public Sprite icon;            // optional; null => procedural placeholder
    public int maxStack = 1;

    [Header("Weapon payload (itemType == Weapon)")]
    public Weapon_Data weaponData;

    [Header("Ammo payload (itemType == Ammo)")]
    public WeaponType ammoForWeaponType;
    public int ammoAmount;

    [Header("Consumable payload (itemType == Consumable)")]
    public int healAmount;
}

// Runtime instance. Mirrors how the existing Weapon class wraps a Weapon_Data SO.
// For equipment slots mirrored from weaponSlots, use OfWeapon(weaponData) (data == null).
[System.Serializable]
public class InventoryItem
{
    public ItemData data;          // may be null when mirrored from a weaponSlot
    public int stack;

    private Weapon_Data _weaponData; // used when data == null
    private Weapon _cachedWeapon;

    public static InventoryItem OfItem(ItemData d, int stack = 1)
    {
        return new InventoryItem { data = d, stack = stack };
    }

    public static InventoryItem OfWeapon(Weapon_Data wd)
    {
        return new InventoryItem { _weaponData = wd, stack = 1, data = null };
    }

    public Weapon_Data WeaponData => data != null ? data.weaponData : _weaponData;

    public ItemType ItemType => data != null ? data.itemType : ItemType.Weapon;

    public string DisplayName => data != null ? data.itemName
        : (WeaponData != null ? WeaponData.weaponName : "Unknown");

    public Weapon GetWeapon()
    {
        if (WeaponData == null) return null;
        return _cachedWeapon ??= new Weapon(WeaponData);
    }

    public bool IsWeapon => ItemType == ItemType.Weapon && WeaponData != null;
}
