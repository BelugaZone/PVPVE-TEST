using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

// Server-Authoritative inventory model (v2).
public class PlayerInventory : NetworkBehaviour
{
    public const int EquipSlotCount = 4;
    public const int BackpackCapacity = 24;

    [SerializeField] private ItemData[] starterBackpackItems;
    [SerializeField] private GameObject hudPrefab;
    [SerializeField] private GameObject panelPrefab;
    [SerializeField] private ItemRegistry itemRegistry; // Used to lookup ItemData by ID

    private Player player;
    private Player_WeaponController weaponController;

    // Struct to synchronize over network
    [System.Serializable]
    public struct NetItem
    {
        public string itemId;
        public int count;
        public bool isWeaponOnly; 
        public int ammoInMag;
        public int ammoReserve;
    }

    public readonly SyncList<NetItem> netBackpack = new SyncList<NetItem>();
    
    public readonly SyncList<NetItem> netEquipment = new SyncList<NetItem>();

    public List<Enemy_LootContainer.LootSlot> DropAllItems()
    {
        List<Enemy_LootContainer.LootSlot> dropped = new List<Enemy_LootContainer.LootSlot>();

        foreach (var item in netEquipment)
        {
            if (!string.IsNullOrEmpty(item.itemId))
                dropped.Add(new Enemy_LootContainer.LootSlot { itemId = item.itemId, count = item.count, taken = false });
        }
        foreach (var item in netBackpack)
        {
            if (!string.IsNullOrEmpty(item.itemId))
                dropped.Add(new Enemy_LootContainer.LootSlot { itemId = item.itemId, count = item.count, taken = false });
        }

        netEquipment.Clear();
        netBackpack.Clear();
        
        return dropped;
    }

    // Local representations for the UI to read easily
    private readonly List<InventoryItem> backpack = new List<InventoryItem>();
    private readonly InventoryItem[] equipment = new InventoryItem[EquipSlotCount];

    public event Action OnChanged;

    public IReadOnlyList<InventoryItem> GetBackpack() => backpack;

    public InventoryItem GetEquipment(int slot)
    {
        if (slot < 0 || slot >= EquipSlotCount) return null;
        return equipment[slot];
    }

    private void Awake()
    {
        player = GetComponent<Player>();
        weaponController = GetComponent<Player_WeaponController>();
        
        for (int i = 0; i < BackpackCapacity; i++) backpack.Add(null);
        
        netBackpack.OnChange += OnNetBackpackChanged;
        netEquipment.OnChange += OnNetEquipmentChanged;
    }

    public void ServerInitialize(PlayerSaveData data)
    {
        netBackpack.Clear();
        for (int i = 0; i < BackpackCapacity; i++)
            netBackpack.Add(new NetItem { itemId = "", count = 0 });
            
        foreach (var item in data.backpack)
        {
            if (item.slotIndex >= 0 && item.slotIndex < BackpackCapacity)
                netBackpack[item.slotIndex] = new NetItem { itemId = item.itemID, count = item.count, ammoInMag = item.ammoInMag, ammoReserve = item.ammoReserve };
        }

        netEquipment.Clear();
        for (int i = 0; i < EquipSlotCount; i++)
            netEquipment.Add(new NetItem { itemId = "", count = 0 });
            
        foreach (var item in data.equipment)
        {
            if (item.slotIndex >= 0 && item.slotIndex < EquipSlotCount)
                netEquipment[item.slotIndex] = new NetItem { itemId = item.itemID, count = item.count, isWeaponOnly = true, ammoInMag = item.ammoInMag, ammoReserve = item.ammoReserve };
        }

        if (data.isRespawn && weaponController != null)
        {
            Weapon_Data melee = weaponController.GetDefaultWeaponData(2);
            if (melee != null)
                netEquipment[2] = new NetItem { itemId = melee.weaponName, count = 1, isWeaponOnly = true, ammoInMag = -1, ammoReserve = -1 };
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        // If not initialized by save data (e.g. testing offline), seed it
        if (netBackpack.Count == 0)
        {
            SeedServer();
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        // Build local representation immediately
        RebuildLocalBackpack();
        RebuildLocalEquipment();

        if (weaponController != null)
        {
            for (int i = 0; i < EquipSlotCount; i++)
            {
                if (equipment[i] != null && i < netEquipment.Count)
                    weaponController.EquipFromInventory(i, equipment[i].WeaponData, netEquipment[i].ammoInMag, netEquipment[i].ammoReserve, false);
                else
                    weaponController.ClearWeaponSlot(i);
            }
            
            // Equip first available weapon, prioritizing primary slot
            int equipSlot = 0;
            if (equipment[0] == null)
            {
                for (int i = 1; i < EquipSlotCount; i++)
                {
                    if (equipment[i] != null)
                    {
                        equipSlot = i;
                        break;
                    }
                }
            }
            weaponController.EquipWeapon(equipSlot);
        }

        if (base.IsOwner)
        {
            SpawnUI();
        }
    }

    public void SeedServer()
    {
        netBackpack.Clear();
        netEquipment.Clear();
        for (int i = 0; i < BackpackCapacity; i++) netBackpack.Add(new NetItem { itemId = "", count = 0 });
        for (int slot = 0; slot < EquipSlotCount; slot++)
        {
            Weapon_Data wd = weaponController != null ? weaponController.GetDefaultWeaponData(slot) : null;
            netEquipment.Add(new NetItem { itemId = wd != null ? wd.weaponName : "", count = 1, isWeaponOnly = true, ammoInMag = -1, ammoReserve = -1 });
        }

        if (starterBackpackItems != null)
        {
            int idx = 0;
            foreach (var data in starterBackpackItems)
            {
                if (data == null) continue;
                while (idx < BackpackCapacity && !string.IsNullOrEmpty(netBackpack[idx].itemId)) idx++;
                if (idx >= BackpackCapacity) break;
                netBackpack[idx] = new NetItem { itemId = data.itemId, count = data.maxStack, ammoInMag = -1, ammoReserve = -1 };
            }
        }
    }

    private void OnNetBackpackChanged(SyncListOperation op, int index, NetItem oldItem, NetItem newItem, bool asServer)
    {
        RebuildLocalBackpack();
        OnChanged?.Invoke();
    }

    private void OnNetEquipmentChanged(SyncListOperation op, int index, NetItem oldItem, NetItem newItem, bool asServer)
    {
        RebuildLocalEquipment();
        OnChanged?.Invoke();
        
        // Sync the actual weapon controller locally
        if (index >= 0 && index < EquipSlotCount && weaponController != null)
        {
            if (string.IsNullOrEmpty(newItem.itemId))
                weaponController.ClearWeaponSlot(index);
            else if (equipment[index] != null)
            {
                bool shouldEquip = (weaponController.CurrentWeaponIndex == index) || (weaponController.CurrentWeaponIndex == -1 && index == 0);
                weaponController.EquipFromInventory(index, equipment[index].WeaponData, newItem.ammoInMag, newItem.ammoReserve, shouldEquip);
            }
        }
    }

    private void RebuildLocalBackpack()
    {
        if (itemRegistry == null) return;
        for (int i = 0; i < BackpackCapacity; i++)
        {
            if (i >= netBackpack.Count || string.IsNullOrEmpty(netBackpack[i].itemId))
                backpack[i] = null;
            else
            {
                ItemData d = itemRegistry.GetItem(netBackpack[i].itemId);
                if (d != null)
                {
                    backpack[i] = InventoryItem.OfItem(d, netBackpack[i].count);
                }
                else
                {
                    Weapon_Data wd = itemRegistry.GetWeapon(netBackpack[i].itemId);
                    if (wd != null)
                    {
                        backpack[i] = InventoryItem.OfWeapon(wd);
                        backpack[i].stack = netBackpack[i].count;
                    }
                    else
                    {
                        backpack[i] = null;
                    }
                }
            }
        }
    }

    private void RebuildLocalEquipment()
    {
        if (itemRegistry == null) return;
        for (int i = 0; i < EquipSlotCount; i++)
        {
            if (i >= netEquipment.Count || string.IsNullOrEmpty(netEquipment[i].itemId))
                equipment[i] = null;
            else
            {
                Weapon_Data w = itemRegistry.GetWeapon(netEquipment[i].itemId);
                if (w == null)
                {
                    ItemData d = itemRegistry.GetItem(netEquipment[i].itemId);
                    if (d != null && d.itemType == ItemType.Weapon)
                    {
                        w = d.weaponData;
                    }
                }
                equipment[i] = w != null ? InventoryItem.OfWeapon(w) : null;
            }
        }
    }

    public static bool IsWeaponAllowedInSlot(WeaponType t, int slot)
    {
        if (slot == 2) return t == WeaponType.Melee;
        if (slot == 3) return t == WeaponType.Grenade;
        
        // Slots 0 and 1 are for firearms
        if (slot == 0 || slot == 1)
        {
            return t == WeaponType.Pistol || t == WeaponType.Revolver || t == WeaponType.AutoRifle || t == WeaponType.Rifle || t == WeaponType.Shotgun;
        }
        
        return false;
    }

    public static bool SlotAccepts(int slot, InventoryItem item)
    {
        if (item == null || !item.IsWeapon || item.WeaponData == null) return false;
        return IsWeaponAllowedInSlot(item.WeaponData.weaponType, slot);
    }

    // Client requests drag operations
    public bool MoveBackpackToEquip(int backpackIndex, int equipSlot)
    {
        if (!base.IsOwner) return false;
        CmdMoveBackpackToEquip(backpackIndex, equipSlot);
        return true; 
    }

    public void UseItem(int backpackIndex)
    {
        if (!base.IsOwner) return;
        CmdUseItem(backpackIndex);
    }

    [ServerRpc]
    private void CmdUseItem(int backpackIndex)
    {
        if (backpackIndex < 0 || backpackIndex >= netBackpack.Count) return;
        
        NetItem slot = netBackpack[backpackIndex];
        if (string.IsNullOrEmpty(slot.itemId) || itemRegistry == null) return;

        ItemData data = itemRegistry.GetItem(slot.itemId);
        if (data != null && data.itemType == ItemType.Consumable)
        {
            Player_Health health = GetComponent<Player_Health>();
            if (health != null)
            {
                if (health.IsAtMaxHealth())
                {
                    return; // Full health, do not consume
                }
                
                health.Heal(data.healAmount);
                
                slot.count--;
                if (slot.count <= 0)
                {
                    slot = new NetItem { itemId = "", count = 0 };
                }
                netBackpack[backpackIndex] = slot;
            }
        }
    }

    [ServerRpc]
    private void CmdMoveBackpackToEquip(int backpackIndex, int equipSlot)
    {
        if (backpackIndex < 0 || backpackIndex >= netBackpack.Count) return;
        if (equipSlot < 0 || equipSlot >= EquipSlotCount) return;

        NetItem incoming = netBackpack[backpackIndex];
        if (string.IsNullOrEmpty(incoming.itemId)) return;

        if (itemRegistry != null)
        {
            ItemData id = itemRegistry.GetItem(incoming.itemId);
            Weapon_Data wd = id != null ? id.weaponData : itemRegistry.GetWeapon(incoming.itemId);
            if (wd != null && !IsWeaponAllowedInSlot(wd.weaponType, equipSlot)) return; 
        }

        NetItem displaced = netEquipment[equipSlot];
        if (weaponController != null && !string.IsNullOrEmpty(displaced.itemId))
        {
            var w = weaponController.GetWeaponAt(equipSlot);
            if (w != null)
            {
                displaced.ammoInMag = w.bulletsInMagazine;
                displaced.ammoReserve = w.totalReserveAmmo;
            }
        }

        netEquipment[equipSlot] = new NetItem { itemId = incoming.itemId, count = incoming.count, isWeaponOnly = true, ammoInMag = incoming.ammoInMag, ammoReserve = incoming.ammoReserve };
        netBackpack[backpackIndex] = displaced;
    }

    [ServerRpc]
    public void CmdPickupWeapon(string weaponId, int ammoInMag, int ammoReserve)
    {
        if (string.IsNullOrEmpty(weaponId)) return;
        
        // Try equip to slot 0 or 1 (firearms)
        for (int i = 0; i < 2; i++)
        {
            if (string.IsNullOrEmpty(netEquipment[i].itemId))
            {
                netEquipment[i] = new NetItem { itemId = weaponId, count = 1, isWeaponOnly = true, ammoInMag = ammoInMag, ammoReserve = ammoReserve };
                return;
            }
        }
        
        // If both full, try backpack
        int target = FirstEmptyNetBackpackSlot();
        if (target >= 0)
        {
            netBackpack[target] = new NetItem { itemId = weaponId, count = 1, isWeaponOnly = false, ammoInMag = ammoInMag, ammoReserve = ammoReserve };
        }
    }

    public bool UnequipToBackpack(int equipSlot, int backpackIndex = -1)
    {
        if (!base.IsOwner) return false;
        CmdUnequipToBackpack(equipSlot, backpackIndex);
        return true;
    }

    [ServerRpc]
    private void CmdUnequipToBackpack(int equipSlot, int backpackIndex)
    {
        if (equipSlot < 0 || equipSlot >= EquipSlotCount) return;
        NetItem item = netEquipment[equipSlot];
        if (string.IsNullOrEmpty(item.itemId)) return;

        if (weaponController != null)
        {
            var w = weaponController.GetWeaponAt(equipSlot);
            if (w != null)
            {
                item.ammoInMag = w.bulletsInMagazine;
                item.ammoReserve = w.totalReserveAmmo;
            }
        }

        int target = backpackIndex >= 0 && backpackIndex < netBackpack.Count && string.IsNullOrEmpty(netBackpack[backpackIndex].itemId)
            ? backpackIndex
            : FirstEmptyNetBackpackSlot();
        if (target < 0) return; // full

        netBackpack[target] = item;
        netEquipment[equipSlot] = new NetItem { itemId = "", count = 0 };
    }

    public bool MoveBackpackToBackpack(int fromIndex, int toIndex)
    {
        if (!base.IsOwner) return false;
        CmdMoveBackpackToBackpack(fromIndex, toIndex);
        return true;
    }

    [ServerRpc]
    private void CmdMoveBackpackToBackpack(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        if (fromIndex < 0 || fromIndex >= netBackpack.Count) return;
        if (toIndex < 0 || toIndex >= netBackpack.Count) return;

        NetItem a = netBackpack[fromIndex];
        NetItem b = netBackpack[toIndex];
        netBackpack[fromIndex] = b;
        netBackpack[toIndex] = a;
    }

    public int FirstEmptyBackpackSlot()
    {
        for (int i = 0; i < backpack.Count; i++)
            if (backpack[i] == null) return i;
        return -1;
    }

    private int FirstEmptyNetBackpackSlot()
    {
        for (int i = 0; i < netBackpack.Count; i++)
            if (string.IsNullOrEmpty(netBackpack[i].itemId)) return i;
        return -1;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Corpse loot pickup
    //
    // The client calls this on their OWN PlayerInventory (they own it), so
    // FishNet correctly routes the ServerRpc to the server without any
    // RequireOwnership=false workarounds or manual connection look-ups.
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Client requests to take a loot slot from a dead enemy's loot container.
    /// FishNet serializes NetworkObject references in RPCs automatically.
    /// </summary>
    [ServerRpc]
    public void CmdTakeLootFromEnemy(NetworkObject enemyNetObj, int slotIndex)
    {
        if (enemyNetObj == null)
        {
            Debug.LogWarning("[PlayerInventory.CmdTakeLootFromEnemy] enemyNetObj is null.");
            return;
        }

        var container = enemyNetObj.GetComponentInChildren<Enemy_LootContainer>();
        if (container == null)
        {
            Debug.LogWarning($"[PlayerInventory.CmdTakeLootFromEnemy] No Enemy_LootContainer on {enemyNetObj.gameObject.name}.");
            return;
        }

        Debug.Log($"[PlayerInventory.CmdTakeLootFromEnemy] Player taking slot {slotIndex} from {enemyNetObj.gameObject.name}");
        container.ServerTakeLootSlot(slotIndex, this);
    }

    private void SpawnUI()
    {
        if (hudPrefab != null)
        {
            var hud = Instantiate(hudPrefab, transform);
            hud.SetActive(true);
            var hudRoot = hud.GetComponent<UI_HudRoot>();
            if (hudRoot != null) hudRoot.Init(player);
        }

        if (panelPrefab != null)
        {
            var ctrlGO = new GameObject("InventoryController");
            ctrlGO.transform.SetParent(transform, false);
            var ctrl = ctrlGO.AddComponent<UI_InventoryController>();
            ctrl.Init(player, panelPrefab);
        }

        // 撤离区 HUD（仅本地玩家）
        gameObject.AddComponent<UI_ExtractionHUD>();
        // 对局结算界面（仅本地玩家）
        gameObject.AddComponent<UI_MatchEndScreen>();
    }
}

