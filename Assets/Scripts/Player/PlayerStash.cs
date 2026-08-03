using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

// Server-authoritative stash + loadout for the lobby player body.
// Mirrors PlayerInventory's SyncList pattern. Lives on the LobbyPlayer prefab.
public class PlayerStash : NetworkBehaviour
{
    public const int StashCapacity = 32;     // 8x4
    public const int LoadoutEquipCount = 4;
    public const int LoadoutBackpackCapacity = 24;

    [SerializeField] private ItemRegistry itemRegistry;

    public readonly SyncList<PlayerInventory.NetItem> netStash = new SyncList<PlayerInventory.NetItem>();
    public readonly SyncList<PlayerInventory.NetItem> netLoadoutEquip = new SyncList<PlayerInventory.NetItem>();
    public readonly SyncList<PlayerInventory.NetItem> netLoadoutBackpack = new SyncList<PlayerInventory.NetItem>();

    private readonly List<InventoryItem> stash = new List<InventoryItem>();
    private readonly InventoryItem[] loadoutEquip = new InventoryItem[LoadoutEquipCount];
    private readonly List<InventoryItem> loadoutBackpack = new List<InventoryItem>();

    public event Action OnChanged;

    private void Awake()
    {
        for (int i = 0; i < StashCapacity; i++) stash.Add(null);
        for (int i = 0; i < LoadoutBackpackCapacity; i++) loadoutBackpack.Add(null);

        netStash.OnChange += (op, index, oldItem, newItem, asServer) => { RebuildLocalStash(); OnChanged?.Invoke(); };
        netLoadoutEquip.OnChange += (op, index, oldItem, newItem, asServer) => { RebuildLocalLoadoutEquip(); OnChanged?.Invoke(); };
        netLoadoutBackpack.OnChange += (op, index, oldItem, newItem, asServer) => { RebuildLocalLoadoutBackpack(); OnChanged?.Invoke(); };
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Rebuild local state from the synced SyncLists (already populated by spawn time).
        RebuildLocalStash();
        RebuildLocalLoadoutEquip();
        RebuildLocalLoadoutBackpack();

        if (base.IsOwner)
        {
            // Build + show the stash panel for the owning client.
            var panel = GetComponentInChildren<UI_StashPanel>(true);
            if (panel != null)
            {
                panel.gameObject.SetActive(true);
                panel.Build(this);
            }
            // Build + show the room panel for the owning client.
            var roomPanel = GetComponentInChildren<UI_RoomPanel>(true);
            if (roomPanel != null)
            {
                roomPanel.gameObject.SetActive(true);
                roomPanel.Build();
            }
            // Ensure the cursor is free for UI interaction in the lobby.
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }
    }

    // Flush stash/loadout back into the save data map when this lobby body is despawned
    // or the server stops. This fires before FishNet destroys the object, so the SyncLists
    // are still readable — unlike ServerDataManager.OnStopServer's FindObjectsOfType, which
    // runs after player objects are already gone.
    public override void OnStopServer()
    {
        base.OnStopServer();
        if (ServerDataManager.Instance == null) return;

        // Resolve the player ID: prefer the Player component, fall back to the authenticator.
        string id = null;
        var p = GetComponent<Player>();
        if (p != null && !string.IsNullOrEmpty(p.playerID)) id = p.playerID;
        if (string.IsNullOrEmpty(id) && ServerDataManager.Instance != null)
        {
            // Ask ServerDataManager to resolve id from this object's owner connection.
            id = ServerDataManager.Instance.GetIDForNetworkObject(NetworkObject);
        }
        if (string.IsNullOrEmpty(id)) return;

        if (ServerDataManager.Instance.TryGetData(id, out var data))
            SaveInto(data);
    }

    // --- Server initialization ---

    public void ServerInitialize(PlayerSaveData data, DefaultLoadout defaultLoadout)
    {
        InitList(netStash, StashCapacity);
        InitList(netLoadoutEquip, LoadoutEquipCount);
        InitList(netLoadoutBackpack, LoadoutBackpackCapacity);

        if (data.stash.Count == 0 && defaultLoadout != null)
        {
            // New player: seed stash from DefaultLoadout.
            int idx = 0;
            foreach (var entry in defaultLoadout.entries)
            {
                if (string.IsNullOrEmpty(entry.itemId) || idx >= StashCapacity) continue;
                netStash[idx] = new PlayerInventory.NetItem { itemId = entry.itemId, count = entry.count, ammoInMag = -1, ammoReserve = -1 };
                idx++;
            }
        }
        else
        {
            foreach (var item in data.stash)
            {
                if (item.slotIndex >= 0 && item.slotIndex < StashCapacity)
                    netStash[item.slotIndex] = new PlayerInventory.NetItem { itemId = item.itemID, count = item.count, ammoInMag = item.ammoInMag, ammoReserve = item.ammoReserve };
            }
            foreach (var item in data.loadoutEquip)
            {
                if (item.slotIndex >= 0 && item.slotIndex < LoadoutEquipCount)
                    netLoadoutEquip[item.slotIndex] = new PlayerInventory.NetItem { itemId = item.itemID, count = item.count, isWeaponOnly = true, ammoInMag = item.ammoInMag, ammoReserve = item.ammoReserve };
            }
            foreach (var item in data.loadoutBackpack)
            {
                if (item.slotIndex >= 0 && item.slotIndex < LoadoutBackpackCapacity)
                    netLoadoutBackpack[item.slotIndex] = new PlayerInventory.NetItem { itemId = item.itemID, count = item.count, ammoInMag = item.ammoInMag, ammoReserve = item.ammoReserve };
            }
        }
    }

    private void InitList(SyncList<PlayerInventory.NetItem> list, int count)
    {
        list.Clear();
        for (int i = 0; i < count; i++) list.Add(new PlayerInventory.NetItem { itemId = "", count = 0 });
    }

    // --- Save (server reads SyncLists back into PlayerSaveData) ---

    public void SaveInto(PlayerSaveData data)
    {
        data.stash.Clear();
        for (int i = 0; i < netStash.Count; i++)
            if (!string.IsNullOrEmpty(netStash[i].itemId))
                data.stash.Add(new InventoryItemData(netStash[i].itemId, netStash[i].count, i, netStash[i].ammoInMag, netStash[i].ammoReserve));

        data.loadoutEquip.Clear();
        for (int i = 0; i < netLoadoutEquip.Count; i++)
            if (!string.IsNullOrEmpty(netLoadoutEquip[i].itemId))
                data.loadoutEquip.Add(new InventoryItemData(netLoadoutEquip[i].itemId, netLoadoutEquip[i].count, i, netLoadoutEquip[i].ammoInMag, netLoadoutEquip[i].ammoReserve));

        data.loadoutBackpack.Clear();
        for (int i = 0; i < netLoadoutBackpack.Count; i++)
            if (!string.IsNullOrEmpty(netLoadoutBackpack[i].itemId))
                data.loadoutBackpack.Add(new InventoryItemData(netLoadoutBackpack[i].itemId, netLoadoutBackpack[i].count, i, netLoadoutBackpack[i].ammoInMag, netLoadoutBackpack[i].ammoReserve));
    }

    // --- Local read access for UI ---

    public IReadOnlyList<InventoryItem> GetStash() => stash;
    public InventoryItem GetLoadoutEquipment(int slot) => (slot >= 0 && slot < LoadoutEquipCount) ? loadoutEquip[slot] : null;
    public IReadOnlyList<InventoryItem> GetLoadoutBackpack() => loadoutBackpack;
    public int FirstEmptyStashSlot() { for (int i = 0; i < stash.Count; i++) if (stash[i] == null) return i; return -1; }

    // --- Client drag requests (mirror PlayerInventory's pattern) ---

    public bool MoveStashToLoadoutEquip(int stashIndex, int equipSlot)
    { if (!base.IsOwner) return false; CmdMoveStashToLoadoutEquip(stashIndex, equipSlot); return true; }

    public bool MoveStashToLoadoutBackpack(int stashIndex, int backpackIndex)
    { if (!base.IsOwner) return false; CmdMoveStashToLoadoutBackpack(stashIndex, backpackIndex); return true; }

    public bool MoveLoadoutEquipToStash(int equipSlot, int stashIndex)
    { if (!base.IsOwner) return false; CmdMoveLoadoutEquipToStash(equipSlot, stashIndex); return true; }

    public bool MoveLoadoutBackpackToStash(int backpackIndex, int stashIndex)
    { if (!base.IsOwner) return false; CmdMoveLoadoutBackpackToStash(backpackIndex, stashIndex); return true; }

    public bool MoveStashToStash(int from, int to)
    { if (!base.IsOwner) return false; CmdMoveStashToStash(from, to); return true; }

    [ServerRpc]
    private void CmdMoveStashToLoadoutEquip(int stashIndex, int equipSlot)
    {
        if (stashIndex < 0 || stashIndex >= netStash.Count) return;
        if (equipSlot < 0 || equipSlot >= LoadoutEquipCount) return;
        var incoming = netStash[stashIndex];
        if (string.IsNullOrEmpty(incoming.itemId)) return;
        if (itemRegistry != null)
        {
            ItemData id = itemRegistry.GetItem(incoming.itemId);
            Weapon_Data wd = id != null ? id.weaponData : itemRegistry.GetWeapon(incoming.itemId);
            if (wd != null && !PlayerInventory.IsWeaponAllowedInSlot(wd.weaponType, equipSlot)) return;
        }
        var displaced = netLoadoutEquip[equipSlot];
        netLoadoutEquip[equipSlot] = incoming;
        netStash[stashIndex] = displaced;
    }

    [ServerRpc]
    private void CmdMoveStashToLoadoutBackpack(int stashIndex, int backpackIndex)
    {
        if (stashIndex < 0 || stashIndex >= netStash.Count) return;
        if (backpackIndex < 0 || backpackIndex >= netLoadoutBackpack.Count) return;
        var incoming = netStash[stashIndex];
        if (string.IsNullOrEmpty(incoming.itemId)) return;
        var displaced = netLoadoutBackpack[backpackIndex];
        netLoadoutBackpack[backpackIndex] = incoming;
        netStash[stashIndex] = displaced;
    }

    [ServerRpc]
    private void CmdMoveLoadoutEquipToStash(int equipSlot, int stashIndex)
    {
        if (equipSlot < 0 || equipSlot >= LoadoutEquipCount) return;
        if (stashIndex < 0 || stashIndex >= netStash.Count) return;
        var incoming = netLoadoutEquip[equipSlot];
        if (string.IsNullOrEmpty(incoming.itemId)) return;
        var displaced = netStash[stashIndex];
        netStash[stashIndex] = incoming;
        netLoadoutEquip[equipSlot] = displaced;
    }

    [ServerRpc]
    private void CmdMoveLoadoutBackpackToStash(int backpackIndex, int stashIndex)
    {
        if (backpackIndex < 0 || backpackIndex >= netLoadoutBackpack.Count) return;
        if (stashIndex < 0 || stashIndex >= netStash.Count) return;
        var incoming = netLoadoutBackpack[backpackIndex];
        if (string.IsNullOrEmpty(incoming.itemId)) return;
        var displaced = netStash[stashIndex];
        netStash[stashIndex] = incoming;
        netLoadoutBackpack[backpackIndex] = displaced;
    }

    [ServerRpc]
    private void CmdMoveStashToStash(int from, int to)
    {
        if (from == to) return;
        if (from < 0 || from >= netStash.Count) return;
        if (to < 0 || to >= netStash.Count) return;
        var a = netStash[from]; var b = netStash[to];
        netStash[from] = b; netStash[to] = a;
    }

    // --- Local rebuild (resolve itemId -> InventoryItem via ItemRegistry) ---

    private void RebuildLocalStash() => RebuildList(netStash, stash, StashCapacity, false);
    private void RebuildLocalLoadoutEquip() => RebuildArray(netLoadoutEquip, loadoutEquip, LoadoutEquipCount, true);
    private void RebuildLocalLoadoutBackpack() => RebuildList(netLoadoutBackpack, loadoutBackpack, LoadoutBackpackCapacity, false);

    private void RebuildList(SyncList<PlayerInventory.NetItem> src, List<InventoryItem> dst, int cap, bool weaponOnly)
    {
        if (itemRegistry == null) return;
        for (int i = 0; i < cap; i++)
            dst[i] = (i < src.Count && !string.IsNullOrEmpty(src[i].itemId)) ? ResolveItem(src[i]) : null;
    }

    private void RebuildArray(SyncList<PlayerInventory.NetItem> src, InventoryItem[] dst, int cap, bool weaponOnly)
    {
        if (itemRegistry == null) return;
        for (int i = 0; i < cap; i++)
            dst[i] = (i < src.Count && !string.IsNullOrEmpty(src[i].itemId)) ? ResolveItem(src[i]) : null;
    }

    private InventoryItem ResolveItem(PlayerInventory.NetItem ni)
    {
        ItemData d = itemRegistry.GetItem(ni.itemId);
        if (d != null) { var it = InventoryItem.OfItem(d, ni.count); return it; }
        Weapon_Data wd = itemRegistry.GetWeapon(ni.itemId);
        if (wd != null) { var it = InventoryItem.OfWeapon(wd); it.stack = ni.count; return it; }
        return null;
    }
}
