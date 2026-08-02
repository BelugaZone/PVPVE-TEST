using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PlayerSaveData
{
    public string playerID;
    
    // Position and Rotation
    public Vector3 position;
    public Quaternion rotation;

    // Health
    public int health;
    public bool isRespawn;

    // Inventory
    public List<InventoryItemData> backpack = new List<InventoryItemData>();
    public List<InventoryItemData> equipment = new List<InventoryItemData>();

    public PlayerSaveData() { }

    public PlayerSaveData(string id)
    {
        playerID = id;
    }
}

[System.Serializable]
public class InventoryItemData
{
    public string itemID;
    public int count;
    public int slotIndex; // Used to maintain position in backpack if needed
    public int ammoInMag;
    public int ammoReserve;

    public InventoryItemData(string id, int amount, int slot = -1, int mag = 0, int res = 0)
    {
        itemID = id;
        count = amount;
        slotIndex = slot;
        ammoInMag = mag;
        ammoReserve = res;
    }
}
