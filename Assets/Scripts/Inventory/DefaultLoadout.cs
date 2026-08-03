using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DefaultLoadout", menuName = "Inventory/Default Loadout")]
public class DefaultLoadout : ScriptableObject
{
    [System.Serializable]
    public struct StashEntry
    {
        public string itemId;   // matches ItemData.itemId OR Weapon_Data.weaponName
        public int count;
    }

    [Tooltip("Items a new player starts with in their stash. itemId resolves via ItemRegistry (ItemData.itemId first, then Weapon_Data.weaponName).")]
    public List<StashEntry> entries = new List<StashEntry>();
}
