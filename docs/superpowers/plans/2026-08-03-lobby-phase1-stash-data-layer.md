# Lobby / Stash / Matchflow — Phase 1: Stash Data Layer & Lobby Body Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persistent cross-match stash + loadout data layer, a stripped lobby player body (replacing the full Player prefab in the Lobby, fixing Phase 0's black-screen/gameplay-script issues), a `PlayerStash` network component with server-authoritative SyncLists, and a stash/loadout drag-drop panel — so a logged-in player sees their stash, can drag items into a loadout, and the stash persists across sessions. Death no longer wipes the stash.

**Architecture:** `PlayerSaveData` gains `stash` + `loadoutEquip` + `loadoutBackpack` lists (stash is JSON-persisted; loadout is memory-only). A new `PlayerStash` NetworkBehaviour (on a new stripped `LobbyPlayer` prefab) mirrors `PlayerInventory`'s SyncList pattern: `SyncList<NetItem>` for stash and for loadout. A `DefaultLoadout` SO seeds new players' stashes. `ServerDataManager` spawns `LobbyPlayer` in the Lobby scene and the full `Player` in the Game scene. A new `UI_StashPanel` + stash-specific slot classes (parallel to the in-raid `UI_InventoryPanel`, reusing `UI_DragManager`/`UI_PlaceholderIcons`/`UI_Tooltip`) provide drag between stash and loadout. `Player_Health.Die` stops clearing the stash (only clears in-raid items).

**Tech Stack:** Unity 2023.1.0f1c1 (URP), FishNet, new Input System 1.6.1, uGUI (`UnityEngine.UI` + `UnityEngine.UI.Text`), TextMeshPro (for the start screen only). No Unity MCP — C# runtime scripts + an Editor MenuItem tool for prefab/asset creation.

## Global Constraints

- Unity version: `2023.1.0f1c1`. URP active.
- No namespaces; top-level classes. `[System.Serializable]` plain classes for data. `NetworkBehaviour` components gated to `IsOwner`.
- No Unity MCP. Prefab/asset creation via an Editor `MenuItem` tool (`PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`/`UnloadPrefabContents` in `try/finally`, `AssetDatabase.CreateAsset` for SOs).
- Reuse existing types verbatim: `PlayerInventory.NetItem` (the struct), `InventoryItem` (runtime wrapper), `ItemData`/`Weapon_Data` SOs, `ItemRegistry`, `UI_DragManager`, `UI_PlaceholderIcons`, `UI_Tooltip`, `UI_SlotHover`/`IItemSlot`.
- Do NOT modify the existing in-raid `UI_InventoryPanel`/`UI_InventorySlot`/`UI_EquipmentSlot`/`PlayerInventory` drag logic — it works and is coupled to `PlayerInventory`. The stash UI is parallel/new.
- Stash persistence: `stash` is serialized to `server_players_data.json` (via `ServerDataManager`). `loadoutEquip`/`loadoutBackpack` are memory-only (not serialized) — reconnect is deferred.
- The full `Player` prefab (`Assets/Prefab/Player.prefab`) is NOT modified in Phase 1 (Phase 0's lobby-scene gameplay-disable guard in `Player.OnStartClient` becomes dead code once the lobby body is used, but leave it as a safety net).

**Design spec:** `docs/superpowers/specs/2026-08-03-lobby-stash-matchflow-design.md` (§2 data model, §6 Phase 1).

**Testing approach:** No test framework. Each task's test cycle = (a) compiles with zero console errors, (b) manual play-mode verification in the Lobby scene. Final task is the full manual checklist.

---

## File Structure

**New runtime scripts:**
- `Assets/Scripts/Inventory/DefaultLoadout.cs` — SO listing starter stash items (itemId + count). Responsibility: define what a new player starts with.
- `Assets/Scripts/Player/PlayerStash.cs` — `NetworkBehaviour` on the LobbyPlayer. Holds `SyncList<NetItem> netStash`, `SyncList<NetItem> netLoadoutEquip` (4), `SyncList<NetItem> netLoadoutBackpack` (24). ServerRpc drag ops (stash↔loadout, stash↔stash, loadout↔loadout). Seeding from `DefaultLoadout`. Responsibility: server-authoritative stash + loadout state, lobby-only.
- `Assets/Scripts/UI/UI_StashPanel.cs` — builds the lobby panel: left = stash grid (8x4), right = loadout equip (2x2) + loadout backpack (6x4). Responsibility: panel layout + refresh.
- `Assets/Scripts/UI/UI_StashSlot.cs` — a stash grid cell, draggable source/target. Calls `PlayerStash` move methods. Responsibility: stash drag source + drop target.
- `Assets/Scripts/UI/UI_LoadoutEquipSlot.cs` — a loadout equipment cell (Primary/Secondary/Melee/Tactical), drop target with weapon-type filter. Responsibility: loadout equip drop target + drag source.

**New Editor script:**
- `Assets/Editor/StashSetupTool.cs` — `[MenuItem("Tools/Lobby Setup/Build Stash Assets")]`. Creates the `DefaultLoadout` SO asset, the `LobbyPlayer` prefab (stripped, with `PlayerStash` + `UI_StashPanel` hook), and assigns `ServerDataManager.lobbyPlayerPrefab` + `defaultLoadout`. Idempotent.

**Modified runtime scripts:**
- `Assets/Scripts/Managers/PlayerSaveData.cs` — add `List<InventoryItemData> stash`, `loadoutEquip`, `loadoutBackpack` fields.
- `Assets/Scripts/Managers/ServerDataManager.cs` — add `lobbyPlayerPrefab` + `defaultLoadout` fields; scene-aware spawn (Lobby→LobbyPlayer with stash, Game→full Player); seed stash for new profiles; save stash; `ClearPlayerState` no longer clears `stash`.
- `Assets/Scripts/Player/Player_Health.cs` — `Die` no longer calls `ClearPlayerState`-with-stash-wipe (stash is preserved; only in-raid items are dropped, which already happens via `DropAllItems`).

**New prefab:**
- `Assets/Prefab/LobbyPlayer.prefab` — a stripped NetworkObject with `PlayerStash` (and a minimal `Player`-like shell for ID/ownership, OR just `PlayerStash` standalone). No gameplay scripts (no movement/aim/weapon/health/FOV).

---

## Task 1: PlayerSaveData extension + DefaultLoadout SO

**Files:**
- Modify: `Assets/Scripts/Managers/PlayerSaveData.cs`
- Create: `Assets/Scripts/Inventory/DefaultLoadout.cs`

**Interfaces:**
- Produces: `PlayerSaveData.stash` / `loadoutEquip` / `loadoutBackpack` (`List<InventoryItemData>`); `DefaultLoadout` SO with `List<StashEntry> entries` where `StashEntry { string itemId; int count; }`.

- [ ] **Step 1: Extend PlayerSaveData**

In `Assets/Scripts/Managers/PlayerSaveData.cs`, add three fields to the `PlayerSaveData` class (after `equipment`):

```csharp
    // Phase 1: cross-match stash (persisted) + loadout (memory-only, not serialized by JsonUtility
    // because we clear them before save — but the fields exist for runtime use).
    public List<InventoryItemData> stash = new List<InventoryItemData>();
    public List<InventoryItemData> loadoutEquip = new List<InventoryItemData>();
    public List<InventoryItemData> loadoutBackpack = new List<InventoryItemData>();
```

- [ ] **Step 2: Create DefaultLoadout SO**

Create `Assets/Scripts/Inventory/DefaultLoadout.cs`:

```csharp
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
```

- [ ] **Step 3: Verify compile + commit**

Run: Unity recompile.
Expected: zero errors. The new fields and SO type compile. (`InventoryItemData` already exists in PlayerSaveData.cs.)

```bash
git add Assets/Scripts/Managers/PlayerSaveData.cs Assets/Scripts/Inventory/DefaultLoadout.cs
git commit -m "feat(stash): extend PlayerSaveData + add DefaultLoadout SO"
```

---

## Task 2: PlayerStash NetworkBehaviour

**Files:**
- Create: `Assets/Scripts/Player/PlayerStash.cs`

**Interfaces:**
- Consumes: `PlayerInventory.NetItem` (struct, reuse), `InventoryItem`, `ItemRegistry`, `ItemData`, `Weapon_Data`, `PlayerSaveData`, `DefaultLoadout`.
- Produces: `PlayerStash` with `netStash`/`netLoadoutEquip`/`netLoadoutBackpack` SyncLists; `ServerInitialize(PlayerSaveData, DefaultLoadout)`; `SaveInto(PlayerSaveData)`; `CmdMoveStashToLoadoutEquip`/`CmdMoveStashToLoadoutBackpack`/`CmdMoveStashToStash`/`CmdMoveLoadoutEquipToStash`/`CmdMoveLoadoutBackpackToStash`; `GetStash()`/`GetLoadoutEquipment(i)`/`GetLoadoutBackpack()`; `OnChanged` event.

- [ ] **Step 1: Write PlayerStash.cs**

Create `Assets/Scripts/Player/PlayerStash.cs`. It mirrors `PlayerInventory`'s structure (SyncList + local rebuild + Cmd moves) but for stash/loadout. Reuse `PlayerInventory.NetItem`:

```csharp
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

        netStash.OnChange += _ => { RebuildLocalStash(); OnChanged?.Invoke(); };
        netLoadoutEquip.OnChange += _ => { RebuildLocalLoadoutEquip(); OnChanged?.Invoke(); };
        netLoadoutBackpack.OnChange += _ => { RebuildLocalLoadoutBackpack(); OnChanged?.Invoke(); };
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
```

- [ ] **Step 2: Verify compile + commit**

Run: Unity recompile.
Expected: zero errors. `PlayerInventory.NetItem` is referenced (it's a nested public struct — accessible as `PlayerInventory.NetItem`). `PlayerInventory.IsWeaponAllowedInSlot` is public static.

```bash
git add Assets/Scripts/Player/PlayerStash.cs
git commit -m "feat(stash): add PlayerStash network component (stash + loadout SyncLists)"
```

---

## Task 3: Stash UI — UI_StashSlot, UI_LoadoutEquipSlot, UI_StashPanel

**Files:**
- Create: `Assets/Scripts/UI/UI_StashSlot.cs`
- Create: `Assets/Scripts/UI/UI_LoadoutEquipSlot.cs`
- Create: `Assets/Scripts/UI/UI_StashPanel.cs`

**Interfaces:**
- Consumes: `PlayerStash`, `UI_DragManager`, `UI_PlaceholderIcons`, `UI_SlotHover`/`IItemSlot`, `InventoryItem`.
- Produces: `UI_StashPanel.Build(PlayerStash)` builds the panel; the slot classes call `PlayerStash` move methods on drag/drop.

- [ ] **Step 1: Write UI_StashSlot.cs**

A stash grid cell. Draggable source + drop target. On drop, routes based on what the drag source is (stash slot, loadout equip slot, or loadout backpack slot):

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// A stash grid cell. Drag source + drop target. Calls PlayerStash move methods.
public class UI_StashSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IItemSlot
{
    private PlayerStash stash;
    private int index;
    private Image icon;
    private Text countText;

    public void Setup(PlayerStash s, int idx)
    {
        stash = s;
        index = idx;
        icon = transform.Find("Icon")?.GetComponent<Image>();
        countText = transform.Find("Count")?.GetComponent<Text>();
    }

    public InventoryItem CurrentItem
    {
        get
        {
            var bp = stash.GetStash();
            return index >= 0 && index < bp.Count ? bp[index] : null;
        }
    }

    public void Refresh(InventoryItem item)
    {
        if (icon == null) return;
        if (item == null) { icon.enabled = false; if (countText != null) countText.text = ""; return; }
        icon.enabled = true;
        icon.sprite = UI_PlaceholderIcons.ForItem(item);
        icon.color = Color.white;
        if (countText != null) countText.text = item.stack > 1 ? item.stack.ToString() : "";
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

    public void OnDrop(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        if (dm.dragging == null) return;
        if (dm.source is UI_StashSlot src) { if (stash.MoveStashToStash(src.GetIndex(), index)) dm.End(); }
        else if (dm.source is UI_LoadoutEquipSlot srcEq) { if (stash.MoveLoadoutEquipToStash(srcEq.GetSlot(), index)) dm.End(); }
        else if (dm.source is UI_LoadoutBackpackSlot srcLb) { if (stash.MoveLoadoutBackpackToStash(srcLb.GetIndex(), index)) dm.End(); }
    }

    public int GetIndex() => index;
}
```

- [ ] **Step 2: Write UI_LoadoutEquipSlot.cs + UI_LoadoutBackpackSlot.cs**

Create `Assets/Scripts/UI/UI_LoadoutEquipSlot.cs`:

```csharp
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
```

Create `Assets/Scripts/UI/UI_LoadoutBackpackSlot.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// A loadout backpack cell. Drag source + drop target (stash <-> loadout backpack).
public class UI_LoadoutBackpackSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IItemSlot
{
    private PlayerStash stash;
    private int index;
    private Image icon;
    private Text countText;

    public void Setup(PlayerStash s, int idx)
    {
        stash = s;
        index = idx;
        icon = transform.Find("Icon")?.GetComponent<Image>();
        countText = transform.Find("Count")?.GetComponent<Text>();
    }

    public InventoryItem CurrentItem
    {
        get { var bp = stash.GetLoadoutBackpack(); return index >= 0 && index < bp.Count ? bp[index] : null; }
    }

    public void Refresh(InventoryItem item)
    {
        if (icon == null) return;
        if (item == null) { icon.enabled = false; if (countText != null) countText.text = ""; return; }
        icon.enabled = true;
        icon.sprite = UI_PlaceholderIcons.ForItem(item);
        icon.color = Color.white;
        if (countText != null) countText.text = item.stack > 1 ? item.stack.ToString() : "";
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

    public void OnDrop(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        if (dm.dragging == null) return;
        if (dm.source is UI_StashSlot src) { if (stash.MoveStashToLoadoutBackpack(src.GetIndex(), index)) dm.End(); }
    }

    public int GetIndex() => index;
}
```

- [ ] **Step 3: Write UI_StashPanel.cs**

Create `Assets/Scripts/UI/UI_StashPanel.cs`. Builds a canvas root + panel: left stash 8x4 grid, right loadout equip 2x2 + loadout backpack 6x4. Follows `UI_InventoryPanel`'s construction style (CreateRect helper, procedural slots). Reuse `UI_SlotHover` on each slot.

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Lobby stash panel: left = 8x4 stash grid, right = 2x2 loadout equip + 6x4 loadout backpack.
// Built procedurally (same style as UI_InventoryPanel). This GameObject is the Canvas root.
public class UI_StashPanel : MonoBehaviour
{
    private PlayerStash stash;
    private readonly List<UI_StashSlot> stashSlots = new List<UI_StashSlot>();
    private readonly UI_LoadoutEquipSlot[] loadoutEquipSlots = new UI_LoadoutEquipSlot[4];
    private readonly List<UI_LoadoutBackpackSlot> loadoutBackpackSlots = new List<UI_LoadoutBackpackSlot>();

    private const float WindowW = 760f;
    private const float WindowH = 360f;
    private const float StashGridW = 380f;
    private const float LoadoutPanelW = 340f;
    private const float SidePad = 16f;

    public void Build(PlayerStash s)
    {
        stash = s;
        Canvas canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
        }

        var content = CreateRect("PanelContent", (RectTransform)transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WindowW, WindowH));
        var bg = content.gameObject.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);

        // Stash grid (left)
        var stashRoot = CreateRect("StashGrid", content, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(SidePad + StashGridW / 2f, 0), new Vector2(StashGridW, WindowH - 50));
        BuildStashGrid(stashRoot);
        AddHeader(content, "仓库 (Stash)", new Vector2(SidePad + StashGridW / 2f, WindowH / 2f - 8f), StashGridW);

        // Divider
        AddDivider(content, new Vector2(0.5f, 0.5f), new Vector2(2, WindowH - 70), Vector2.zero);

        // Loadout panel (right): equip 2x2 on top, backpack 6x4 below
        var loadoutRoot = CreateRect("LoadoutPanel", content, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-(SidePad + LoadoutPanelW / 2f), 0), new Vector2(LoadoutPanelW, WindowH - 50));
        BuildLoadoutEquip(loadoutRoot);
        BuildLoadoutBackpack(loadoutRoot);
        AddHeader(content, "配装 (Loadout)", new Vector2(-(SidePad + LoadoutPanelW / 2f), WindowH / 2f - 8f), LoadoutPanelW);

        Refresh();
        stash.OnChanged += Refresh;
    }

    private void BuildStashGrid(RectTransform parent)
    {
        int cols = 8, rows = 4; float cell = 40f, gap = 4f;
        float gw = cols * cell + (cols - 1) * gap, gh = rows * cell + (rows - 1) * gap;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                float x = -gw / 2 + c * (cell + gap) + cell / 2;
                float y = gh / 2 - r * (cell + gap) - cell / 2;
                var rt = CreateSlot(parent, "StashSlot_" + idx, new Vector2(x, y), new Vector2(cell, cell));
                var slot = rt.gameObject.AddComponent<UI_StashSlot>();
                slot.Setup(stash, idx);
                rt.gameObject.AddComponent<UI_SlotHover>().Setup();
                stashSlots.Add(slot);
            }
    }

    private void BuildLoadoutEquip(RectTransform parent)
    {
        float cell = 70f, gap = 8f;
        float yTop = 70f;
        Vector2[] pos = { new Vector2(-(cell+gap)/2, yTop), new Vector2((cell+gap)/2, yTop),
                          new Vector2(-(cell+gap)/2, yTop - cell - gap), new Vector2((cell+gap)/2, yTop - cell - gap) };
        for (int i = 0; i < 4; i++)
        {
            var rt = CreateSlot(parent, "LoadoutEquip_" + i, pos[i], new Vector2(cell, cell), label: UI_LoadoutEquipSlot.LabelsStatic(i));
            var slot = rt.gameObject.AddComponent<UI_LoadoutEquipSlot>();
            slot.Setup(stash, i);
            rt.gameObject.AddComponent<UI_SlotHover>().Setup();
            loadoutEquipSlots[i] = slot;
        }
    }

    private void BuildLoadoutBackpack(RectTransform parent)
    {
        int cols = 6, rows = 4; float cell = 40f, gap = 4f;
        float gw = cols * cell + (cols - 1) * gap;
        float yBase = -30f;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                float x = -gw / 2 + c * (cell + gap) + cell / 2;
                float y = yBase + ghBackpack(rows, cell, gap) / 2 - r * (cell + gap) - cell / 2;
                var rt = CreateSlot(parent, "LoadoutBp_" + idx, new Vector2(x, y), new Vector2(cell, cell));
                var slot = rt.gameObject.AddComponent<UI_LoadoutBackpackSlot>();
                slot.Setup(stash, idx);
                rt.gameObject.AddComponent<UI_SlotHover>().Setup();
                loadoutBackpackSlots.Add(slot);
            }
    }

    private float ghBackpack(int rows, float cell, float gap) => rows * cell + (rows - 1) * gap;

    private RectTransform CreateSlot(RectTransform parent, string name, Vector2 pos, Vector2 size, string label = null)
    {
        var rt = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        var bg = new GameObject("Bg").AddComponent<Image>();
        bg.transform.SetParent(rt, false); bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.sizeDelta = Vector2.zero; bgRt.anchoredPosition = Vector2.zero;

        var iconGo = new GameObject("Icon"); iconGo.transform.SetParent(rt, false);
        var icon = iconGo.AddComponent<Image>(); icon.raycastTarget = false;
        var iconRt = icon.GetComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one; iconRt.sizeDelta = new Vector2(-6, -6); iconRt.anchoredPosition = Vector2.zero;

        var labelGo = new GameObject("Label"); labelGo.transform.SetParent(rt, false);
        var lt = labelGo.AddComponent<Text>();
        lt.font = GetDefaultFont(); lt.fontSize = 10; lt.alignment = TextAnchor.LowerCenter;
        lt.color = new Color(0.8f, 0.8f, 0.8f, 0.9f); lt.raycastTarget = false; lt.text = label ?? "";
        var labelRt = lt.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0); labelRt.anchorMax = new Vector2(1, 0);
        labelRt.pivot = new Vector2(0.5f, 0); labelRt.sizeDelta = new Vector2(0, 14); labelRt.anchoredPosition = new Vector2(0, 2);

        var countGo = new GameObject("Count"); countGo.transform.SetParent(rt, false);
        var ct = countGo.AddComponent<Text>();
        ct.font = GetDefaultFont(); ct.fontSize = 12; ct.alignment = TextAnchor.UpperRight;
        ct.color = Color.white; ct.raycastTarget = false;
        var countRt = ct.GetComponent<RectTransform>();
        countRt.anchorMin = new Vector2(1, 1); countRt.anchorMax = new Vector2(1, 1);
        countRt.pivot = new Vector2(1, 1); countRt.sizeDelta = new Vector2(30, 16); countRt.anchoredPosition = new Vector2(-2, -2);
        return rt;
    }

    private void AddDivider(Transform parent, Vector2 anchor, Vector2 size, Vector2 pos)
    {
        var rt = CreateRect("Divider", (RectTransform)parent, anchor, anchor, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UI_PlaceholderIcons.White(); img.color = new Color(1, 1, 1, 0.6f); img.raycastTarget = false;
    }

    private void AddHeader(Transform parent, string title, Vector2 pos, float width)
    {
        var rt = CreateRect("Header_" + title, (RectTransform)parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(width, 20));
        var t = rt.gameObject.AddComponent<Text>();
        t.font = GetDefaultFont(); t.fontSize = 16; t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.UpperCenter; t.color = new Color(0.9f, 0.9f, 0.9f, 0.95f); t.raycastTarget = false; t.text = title;
    }

    private RectTransform CreateRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size; return rt;
    }

    private static Font GetDefaultFont() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

    public void Refresh()
    {
        if (stash == null) return;
        var s = stash.GetStash();
        for (int i = 0; i < stashSlots.Count; i++) stashSlots[i].Refresh(i < s.Count ? s[i] : null);
        for (int i = 0; i < 4; i++) if (loadoutEquipSlots[i] != null) loadoutEquipSlots[i].Refresh(stash.GetLoadoutEquipment(i));
        var lb = stash.GetLoadoutBackpack();
        for (int i = 0; i < loadoutBackpackSlots.Count; i++) loadoutBackpackSlots[i].Refresh(i < lb.Count ? lb[i] : null);
    }

    private void OnDestroy() { if (stash != null) stash.OnChanged -= Refresh; }
}
```

Add a static label helper to `UI_LoadoutEquipSlot` (add this inside the class, used by `UI_StashPanel.BuildLoadoutEquip`):
```csharp
    public static string LabelsStatic(int i) { string[] L = { "Primary", "Secondary", "Melee", "Tactical" }; return L[i]; }
```

- [ ] **Step 4: Verify compile + commit**

Run: Unity recompile.
Expected: zero errors. All slot classes implement the drag interfaces; `UI_DragManager.source` is `object`, so `is UI_StashSlot`/`is UI_LoadoutEquipSlot`/`is UI_LoadoutBackpackSlot` pattern checks compile.

```bash
git add Assets/Scripts/UI/UI_StashSlot.cs Assets/Scripts/UI/UI_LoadoutEquipSlot.cs Assets/Scripts/UI/UI_LoadoutBackpackSlot.cs Assets/Scripts/UI/UI_StashPanel.cs
git commit -m "feat(stash): add stash/loadout drag-drop UI panel + slots"
```

---

## Task 4: StashSetupTool — DefaultLoadout asset, LobbyPlayer prefab, ServerDataManager wiring

**Files:**
- Create: `Assets/Editor/StashSetupTool.cs`

**Interfaces:**
- Consumes: `DefaultLoadout`, `PlayerStash`, `UI_StashPanel`, `ServerDataManager`, `ItemRegistry` asset (`Assets/Data/ItemRegistry.asset`), existing weapon/item IDs (`Heaven 567` pistol, `rifle_ammo`, `medkit`).
- Produces: `Assets/Data/DefaultLoadout.asset`; `Assets/Prefab/LobbyPlayer.prefab` (NetworkObject + `PlayerStash` + a `UI_StashPanel` canvas child, no gameplay scripts); `ServerDataManager.lobbyPlayerPrefab` + `defaultLoadout` assigned on the Lobby scene's ServerDataManager.

- [ ] **Step 1: Write StashSetupTool.cs**

Create `Assets/Editor/StashSetupTool.cs`:

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using FishNet.Object;

public static class StashSetupTool
{
    private const string DefaultLoadoutPath = "Assets/Data/DefaultLoadout.asset";
    private const string LobbyPlayerPrefabPath = "Assets/Prefab/LobbyPlayer.prefab";
    private const string ItemRegistryPath = "Assets/Data/ItemRegistry.asset";

    [MenuItem("Tools/Lobby Setup/Build Stash Assets")]
    public static void BuildStashAssets()
    {
        // 1. DefaultLoadout SO
        var loadout = AssetDatabase.LoadAssetAtPath<DefaultLoadout>(DefaultLoadoutPath);
        if (loadout == null)
        {
            loadout = ScriptableObject.CreateInstance<DefaultLoadout>();
            AssetDatabase.CreateAsset(loadout, DefaultLoadoutPath);
        }
        loadout.entries = new System.Collections.Generic.List<DefaultLoadout.StashEntry>
        {
            new DefaultLoadout.StashEntry { itemId = "Heaven 567", count = 1 },   // pistol (Weapon_Data.weaponName)
            new DefaultLoadout.StashEntry { itemId = "rifle_ammo", count = 2 },   // ammo (ItemData.itemId)
            new DefaultLoadout.StashEntry { itemId = "medkit", count = 2 },       // consumable
        };
        EditorUtility.SetDirty(loadout);

        // 2. LobbyPlayer prefab
        var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(ItemRegistryPath);

        var root = new GameObject("LobbyPlayer");
        var nob = root.AddComponent<NetworkObject>();
        var stash = root.AddComponent<PlayerStash>();
        // Assign itemRegistry via SerializedObject (private serialized field).
        var soStash = new SerializedObject(stash);
        var regProp = soStash.FindProperty("itemRegistry");
        if (regProp != null && registry != null) regProp.objectReferenceValue = registry;
        soStash.ApplyModifiedPropertiesWithoutUndo();

        // UI_StashPanel canvas child (inactive until owner shows it).
        var uiGo = new GameObject("StashPanel");
        uiGo.transform.SetParent(root.transform, false);
        uiGo.AddComponent<UI_StashPanel>();

        DirectoryEnsure(System.IO.Path.GetDirectoryName(LobbyPlayerPrefabPath));
        PrefabUtility.SaveAsPrefabAsset(root, LobbyPlayerPrefabPath);
        Object.DestroyImmediate(root);

        // 3. Wire ServerDataManager in the Lobby scene (must be open).
        var sdm = Object.FindObjectOfType<ServerDataManager>();
        if (sdm != null)
        {
            var lobbyPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(LobbyPlayerPrefabPath);
            var so = new SerializedObject(sdm);
            var lpProp = so.FindProperty("lobbyPlayerPrefab");
            if (lpProp != null && lobbyPrefab != null) lpProp.objectReferenceValue = lobbyPrefab;
            var dlProp = so.FindProperty("defaultLoadout");
            if (dlProp != null && loadout != null) dlProp.objectReferenceValue = loadout;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(sdm);
            EditorSceneManager_MarkDirty();
            Debug.Log("[StashSetup] Wired ServerDataManager.lobbyPlayerPrefab + defaultLoadout.");
        }
        else
        {
            Debug.LogWarning("[StashSetup] No ServerDataManager in open scene. Open Assets/Scenes/Lobby.unity and re-run to wire.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[StashSetup] Done. DefaultLoadout + LobbyPlayer prefab created.");
    }

    private static void DirectoryEnsure(string dir)
    {
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
    }

    private static void EditorSceneManager_MarkDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
}
#endif
```

- [ ] **Step 2: Verify compile + commit**

Run: Unity recompile.
Expected: zero errors. Menu `Tools/Lobby Setup/Build Stash Assets` appears.

```bash
git add Assets/Editor/StashSetupTool.cs
git commit -m "feat(stash): StashSetupTool (DefaultLoadout asset + LobbyPlayer prefab + wiring)"
```

---

## Task 5: ServerDataManager — scene-aware spawn + stash persistence + death no-clear

**Files:**
- Modify: `Assets/Scripts/Managers/ServerDataManager.cs`
- Modify: `Assets/Scripts/Player/Player_Health.cs`

**Interfaces:**
- Consumes: `lobbyPlayerPrefab` (NetworkObject, new field), `defaultLoadout` (DefaultLoadout, new field), `PlayerStash`, `PlayerSaveData.stash/loadout*`, `ResolveSpawnPoint()` (from Phase 0).
- Produces: `SpawnPlayerForConnection` spawns `lobbyPlayerPrefab` + initializes `PlayerStash` when in the Lobby scene; spawns the full `playerPrefab` when in the Game scene. `SavePlayerState` writes `stash` (via `PlayerStash.SaveInto`). `ClearPlayerState` no longer clears `stash`.

- [ ] **Step 1: Add lobbyPlayerPrefab + defaultLoadout fields**

In `ServerDataManager.cs`, near the existing `playerPrefab`/`defaultSpawnPoint` fields, add:

```csharp
    [Tooltip("Stripped player prefab spawned in the Lobby scene (has PlayerStash, no gameplay).")]
    public NetworkObject lobbyPlayerPrefab;

    [Tooltip("Default stash contents for a new player.")]
    public DefaultLoadout defaultLoadout;
```

- [ ] **Step 2: Scene-aware spawn in SpawnPlayerForConnection**

Replace the body of `SpawnPlayerForConnection` (from `NetworkObject playerObj = Instantiate(...)` through `base.ServerManager.Spawn(playerObj, conn)`) with scene-aware logic. The method currently always uses `playerPrefab`. Change it to:

```csharp
        // 2. Choose prefab by scene: Lobby -> LobbyPlayer (stash), Game -> full Player.
        bool inLobby = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == lobbySceneName;
        NetworkObject prefabToUse = inLobby && lobbyPlayerPrefab != null ? lobbyPlayerPrefab : playerPrefab;
        if (prefabToUse == null)
        {
            Debug.LogError("[ServerDataManager] No player prefab for scene " + (inLobby ? "Lobby" : "Game"));
            return;
        }

        Transform spawn = ResolveSpawnPoint();
        Vector3 pos = isNewProfile ? (spawn != null ? spawn.position : Vector3.zero)
                                   : data.position;
        Quaternion rot = isNewProfile ? (spawn != null ? spawn.rotation : Quaternion.identity)
                                      : data.rotation;
        NetworkObject playerObj = Instantiate(prefabToUse, pos, rot);

        // 3. Inject data before spawning.
        if (inLobby)
        {
            PlayerStash stash = playerObj.GetComponent<PlayerStash>();
            if (stash != null)
            {
                stash.ServerInitialize(data, defaultLoadout);
            }
            // Set playerID on a Player component if present (LobbyPlayer may not have one).
            var pComp = playerObj.GetComponent<Player>();
            if (pComp != null) pComp.playerID = playerID;
        }
        else
        {
            Player playerComponent = playerObj.GetComponent<Player>();
            if (playerComponent != null)
            {
                playerComponent.playerID = playerID;
                Player_Health healthComponent = playerObj.GetComponent<Player_Health>();
                if (healthComponent != null) healthComponent.ServerInitialize(data.health);
                PlayerInventory inventory = playerObj.GetComponent<PlayerInventory>();
                if (inventory != null)
                {
                    if (isNewProfile) inventory.SeedServer();
                    else inventory.ServerInitialize(data);
                }
            }
        }

        // 4. Spawn over network
        base.ServerManager.Spawn(playerObj, conn);
```

- [ ] **Step 3: Save stash in SavePlayerState**

In `SavePlayerState`, after the existing equipment-save block, add stash saving. The lobby player has `PlayerStash` (not `PlayerInventory`), so:

```csharp
        // Save stash (lobby player body).
        PlayerStash stashComp = player.GetComponent<PlayerStash>();
        if (stashComp != null)
        {
            stashComp.SaveInto(data);
        }
```

Also update the `ServerManager_OnRemoteConnectionState` disconnect handler: it currently looks up `Player` via `conn.FirstObject.GetComponent<Player>()`. The LobbyPlayer may not have a `Player` component — handle both. Replace the inner lookup:

```csharp
                // Works for both full Player (Game) and LobbyPlayer (Lobby) which carries a Player shell.
                Player player = conn.FirstObject != null ? conn.FirstObject.GetComponent<Player>() : null;
                PlayerStash stash = conn.FirstObject != null ? conn.FirstObject.GetComponent<PlayerStash>() : null;
                if (player != null && _authenticator != null)
                {
                    string playerID = _authenticator.GetIDForConnection(conn);
                    if (!string.IsNullOrEmpty(playerID))
                        SavePlayerState(playerID, player);
                }
                else if (stash != null && _authenticator != null)
                {
                    // LobbyPlayer without a Player component: save stash directly.
                    string playerID = _authenticator.GetIDForConnection(conn);
                    if (!string.IsNullOrEmpty(playerID) && _playerDataMap.TryGetValue(playerID, out var d))
                        stash.SaveInto(d);
                }
```

- [ ] **Step 4: ClearPlayerState no longer clears stash**

In `ClearPlayerState`, remove the lines that clear `stash` (it currently clears `backpack`/`equipment` only — leave those, do NOT touch `data.stash`). Confirm the method body is:

```csharp
    public void ClearPlayerState(string playerID)
    {
        if (!string.IsNullOrEmpty(playerID) && _playerDataMap.TryGetValue(playerID, out PlayerSaveData data))
        {
            data.backpack.Clear();
            data.equipment.Clear();
            data.health = 100;
            data.isRespawn = true;
            // NOTE: data.stash is intentionally preserved — death loses in-raid items only.
            Debug.Log($"[ServerDataManager] Cleared in-raid state for '{playerID}' (stash preserved).");
        }
    }
```

- [ ] **Step 5: Player_Health.Die — confirm it does not wipe stash**

Read `Assets/Scripts/Player/Player_Health.cs` `Die()`. It calls `ServerDataManager.ClearPlayerState(playerID)` — which (per Step 4) now preserves stash. No change needed IF ClearPlayerState already only clears backpack/equipment. If `Die` has any direct `data.stash.Clear()` call, remove it. Verify by reading; the expected behavior: death drops in-raid items (via `DropAllItems`) and clears in-raid state, stash untouched.

- [ ] **Step 6: Verify compile + commit**

Run: Unity recompile.
Expected: zero errors.

```bash
git add Assets/Scripts/Managers/ServerDataManager.cs Assets/Scripts/Player/Player_Health.cs
git commit -m "feat(stash): scene-aware spawn + stash persistence + death preserves stash"
```

---

## Task 6: Manual verification (full Phase 1 acceptance)

**Files:** none (verification only).

- [ ] **Step 1: Run StashSetupTool**

In Unity, run `Tools → Lobby Setup → Build Stash Assets`. Open `Assets/Scenes/Lobby.unity` and re-run the tool there too (to wire ServerDataManager in the Lobby scene). Confirm:
- `Assets/Data/DefaultLoadout.asset` exists with 3 entries.
- `Assets/Prefab/LobbyPlayer.prefab` exists, has `NetworkObject` + `PlayerStash` + a `StashPanel` child.
- The Lobby scene's `ServerDataManager` has `lobbyPlayerPrefab` and `defaultLoadout` assigned.

- [ ] **Step 2: Lobby login shows stash**

Open `Lobby.unity` → Play → `Login as Host` with ID `newplayer1`. Expected:
- The `LobbyPlayer` prefab spawns (not the full Player) at `LobbySpawnPoint`.
- The stash panel is visible (ScreenSpaceOverlay canvas) showing the 3 default items (pistol, rifle ammo x2, medkit x2) in the stash grid.
- No gameplay-script NREs (LobbyPlayer has none).

- [ ] **Step 3: Drag stash → loadout**

Drag the pistol from the stash grid to the Primary equip slot. Expected: pistol moves to Primary; stash slot empties. Drag medkit to the loadout backpack. Expected: moves there. Drag rifle_ammo to loadout backpack. Expected: moves.

- [ ] **Step 4: Drag loadout → stash**

Drag the pistol from Primary back to an empty stash slot. Expected: returns to stash.

- [ ] **Step 5: Persistence across sessions**

Stop Play. Delete the save file (`Application.persistentDataPath/server_players_data.json`) OR use a new ID. Play again, login as a fresh ID. Expected: stash seeded with default loadout again. Now login as `newplayer1` (existing profile) — expected: stash reflects the state from Step 3 (items moved persist).

- [ ] **Step 6: Commit if all pass**

If all steps pass, Phase 1 is complete. The human commits any remaining changes.

---

## Self-Review (completed)

**Spec coverage (Phase 1 of §6):**
- "PlayerSaveData 扩 stash+loadout" → Task 1. ✓
- "默认配装表 SO" → Task 1 (DefaultLoadout) + Task 4 (asset). ✓
- "大厅玩家体预制体 + PlayerStash" → Task 2 (PlayerStash) + Task 4 (LobbyPlayer prefab). ✓
- "UI_StashPanel 仓库+配装栏+配装背包拖拽" → Task 3. ✓
- "SavePlayerState 写 stash" → Task 5 Step 3. ✓
- "死亡不全清" → Task 5 Step 4/5. ✓

**Placeholder scan:** No TBD/TODO. All code blocks complete. The `UI_LoadoutEquipSlot.LabelsStatic` helper is added in Step 3 (referenced by UI_StashPanel). 

**Type consistency:** `PlayerInventory.NetItem` reused throughout (not redefined). `PlayerStash` method names match the slot call sites (`MoveStashToLoadoutEquip`, `MoveLoadoutEquipToStash`, etc.). `DefaultLoadout.StashEntry` fields (`itemId`, `count`) match usage. `lobbyPlayerPrefab`/`defaultLoadout` field names match between ServerDataManager and StashSetupTool.

**Known risks:**
1. `PlayerStash` is a `NetworkBehaviour` on the LobbyPlayer; like Phase 0's ServerDataManager, it cannot call `DontDestroyOnLoad` — but it doesn't need to (LobbyPlayer is spawned at runtime, not scene-placed, so it's an instantiated object that can be `SetIsGlobal` if needed in Phase 3; for Phase 1 it lives only in the Lobby scene).
2. `UI_LoadoutEquipSlot` disallows backpack→equip drag (v1 simplicity); equip↔equip swap not implemented (v1). Acceptable for Phase 1.
3. The LobbyPlayer has no `Player` component currently — `ServerDataManager.OnStopServer` (on the Player) won't fire for it. The disconnect handler (`ServerManager_OnRemoteConnectionState`) handles stash saving for the LobbyPlayer (Task 5 Step 3). Verify in Task 6 Step 5.
4. `UI_StashPanel` layout values are approximate; may need visual tuning in Task 6.
