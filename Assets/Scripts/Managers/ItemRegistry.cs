using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ItemRegistry", menuName = "Inventory/Item Registry")]
public class ItemRegistry : ScriptableObject
{
    public List<ItemData> allItems = new List<ItemData>();
    public List<Weapon_Data> allWeapons = new List<Weapon_Data>();

    private Dictionary<string, ItemData> itemDict;
    private Dictionary<string, Weapon_Data> weaponDict;

    public void Initialize()
    {
        itemDict = new Dictionary<string, ItemData>();
        foreach (var item in allItems)
        {
            if (item != null && !string.IsNullOrEmpty(item.itemId))
                itemDict[item.itemId] = item;
        }

        weaponDict = new Dictionary<string, Weapon_Data>();
        foreach (var weapon in allWeapons)
        {
            if (weapon != null && !string.IsNullOrEmpty(weapon.weaponName)) // Assuming weaponName is unique ID
                weaponDict[weapon.weaponName] = weapon;
        }
    }

    public ItemData GetItem(string id)
    {
        if (itemDict == null) Initialize();
        itemDict.TryGetValue(id, out ItemData data);
        return data;
    }

    public Weapon_Data GetWeapon(string id)
    {
        if (weaponDict == null) Initialize();
        weaponDict.TryGetValue(id, out Weapon_Data data);
        return data;
    }
}
