# Player UI System — Design Spec

**Date:** 2026-08-01
**Branch:** feature/player-grenade-throw
**Status:** Design (pending implementation plan)

## 1. Goal

Add a complete UI system to the project: a Tarkov-style inventory + equipment panel
(toggle with **B**) and an always-on ammo HUD. No Unity MCP — an Editor auto-generation
tool script builds the Canvas prefabs, placeholder sprites, wires runtime scripts, and
patches `Player.prefab`, matching the existing `PlayerGrenadeSetupTool` conventions.

## 2. Scope decisions (confirmed with user)

- **Backpack grid model:** simplified grid, **1 cell per item** (no tetris size collision).
- **Backpack contents:** weapons + ammo/consumables → requires a new generic `ItemData` model.
- **Drag-to-equip:** **local-only for v1** — the panel drives the local player's weapon
  state but does NOT sync to other clients via FishNet ServerRpc yet.
- **Panel layout:** Tarkov-style human-silhouette equipment panel (primary/secondary guns on
  the body sides, melee at the hip, grenade at the chest) + backpack grid on the right.

### Out of scope (v1)

- Network sync of inventory/equip to non-owner clients (future ServerRpc pass).
- Ammo/consumable *consumption* effects wired into the new item model (picking items up
  *into* the new inventory). The existing weapon firing/reloading already works; the HUD
  reflects real weapon state.
- Grid size/rotation/stacking tetris (1 cell = 1 item; stack count for ammo only).

## 3. Existing codebase facts (anchors)

- Unity 2023.1.0f1c1, URP, FishNet, new Input System (`PlayerControls` C#-generated, no
  Inventory action exists).
- `Player_WeaponController.weaponSlots` is `List<Weapon>` of `maxSlots = 4`: slot 0 primary
  gun, 1 secondary gun, 2 melee, 3 grenade. `currentWeapon` is the equipped one; public
  `CurrentWeapon()` accessor. `EquipWeapon(int)` is local (sets currentWeapon, plays equip
  anim, changes camera distance) — not itself a ServerRpc.
- `Weapon` is a `[System.Serializable]` plain class built from a `Weapon_Data` SO; holds
  `bulletsInMagazine`, `magazineCapacity`, `totalReserveAmmo`, `weaponType`, `weaponData`.
- `WeaponType { Pistol, Revolver, AutoRifle, Shotgun, Rifle, Melee, Grenade }`.
- `UI_HealthBar` (runtime, code-generated canvas) creates a **400×20** bottom-center bar
  (anchorMin/Max = 0.5/0 bottom, y=30, width 400) on its own ScreenSpaceOverlay canvas
  (sortingOrder 100, CanvasScaler 1920×1080). Local player only (gated by `IsOwner`).
- Editor tools: `EditorWindow` + `[MenuItem("Tools/...")]` static method, `private const
  string` paths, idempotent, `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`/
  `UnloadPrefabContents` in try/finally, no namespace.

## 4. Architecture

Three layers, cleanly separated:

1. **Data layer** — generic item model (new), network-agnostic.
2. **Runtime inventory model** — local `PlayerInventory` (source of truth for the panel)
   with a small bridge into `Player_WeaponController` so equipping actually makes the
   player hold the gun.
3. **UI layer** — always-on **ammo HUD** (reads real `CurrentWeapon()`) and the **B-toggle
   inventory panel** (reads/writes `PlayerInventory`).

```
┌─ UI layer ────────────────────────────────┐   ┌─ runtime ──────────┐   ┌─ existing ─┐
│ UI_AmmoRing  ─reads─► CurrentWeapon()──────┼──►│ Player_WeaponCtrl  │   │ weaponSlots│
│ UI_InventoryPanel ─reads/writes─►          │   │  + EquipFromInv()  │──►│ currentWeapon│
│ UI_InventoryController (B toggle, drag)    │   │ PlayerInventory    │   └────────────┘
└────────────────────────────────────────────┘   └────────────────────┘
        built by Editor tool → Canvas prefabs + placeholder sprites
```

## 5. Data layer

**File:** `Assets/Scripts/Inventory/ItemData.cs`

```csharp
public enum ItemType { Weapon, Ammo, Consumable }

[CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item Data")]
public class ItemData : ScriptableObject
{
    public string itemId;
    public string itemName;
    public ItemType itemType;
    public Sprite icon;            // optional; null → procedural placeholder colored by type
    public int maxStack = 1;
    // weapon payload (itemType == Weapon): reuses existing SO
    public Weapon_Data weaponData;
    // ammo payload (itemType == Ammo): mirrors Pickup_Ammo.AmmoData
    public WeaponType ammoForWeaponType;
    public int ammoAmount;
    // consumable payload (itemType == Consumable)
    public int healAmount;
}

// Runtime instance (like Weapon wraps Weapon_Data). [System.Serializable]
public class InventoryItem
{
    public ItemData data;
    public int stack;
    private Weapon _cachedWeapon;
    public Weapon GetWeapon() => _cachedWeapon ??= new Weapon(data.weaponData);
}
```

Weapons reuse existing `Weapon_Data` assets — no duplication. The Editor tool generates a
few starter `ItemData` assets (spare pistol, rifle ammo box, med kit) under
`Assets/Data/Inventory/`.

## 6. Runtime inventory model

**File:** `Assets/Scripts/Player/PlayerInventory.cs` — plain `MonoBehaviour` on the Player.

- `List<InventoryItem> backpack` — capacity 24 (6×4).
- `InventoryItem[] equipment = new InventoryItem[4]` — Primary, Secondary, Melee, Grenade.
- **Seeded at start** to mirror current defaults (slot0 = `defaultWeaponData`, slot2 =
  `defaultMeleeWeaponData`, slot3 = `defaultGrenadeWeaponData`) + starter backpack items, so
  the UI is demonstrable. Reads the same `Weapon_Data` refs the controller uses.
- Local-only: gated to owner. `Player.OnStartClient` calls `inventory.InitializeLocal()`
  when `IsOwner`; otherwise the component self-disables and the UI is not created.
- Public API:
  - `InventoryItem GetEquipment(int slot)`
  - `IReadOnlyList<InventoryItem> GetBackpack()`
  - `bool MoveBackpackToEquip(int backpackIndex, int equipSlot)` — type-checks (guns→0/1,
    melee→2, grenade→3); on success swaps the displaced equipped item back to backpack and
    calls the bridge.
  - `bool UnequipToBackpack(int equipSlot)`
  - `event Action OnChanged` — UI refreshes on change.

**Bridge into existing weapon system** (new public methods on `Player_WeaponController`):

```csharp
public void EquipFromInventory(int slot, Weapon_Data data)
{
    weaponSlots[slot] = new Weapon(data);
    EquipWeapon(slot);   // existing local path: currentWeapon + anim + camera distance
}
public Weapon GetWeaponAt(int slot) => slot < weaponSlots.Count ? weaponSlots[slot] : null;
public IReadOnlyList<Weapon> GetWeaponSlots() => weaponSlots;
```

Because `EquipWeapon` is owner-local, the local player visually holds and fires the new gun
immediately. Other clients do not see the swap (documented v1 limitation).

## 7. Ammo HUD (always-on)

**Files:** `Assets/Scripts/UI/UI_HudRoot.cs`, `Assets/Scripts/UI/UI_AmmoRing.cs`.

A new ScreenSpaceOverlay canvas (CanvasScaler 1920×1080, sortingOrder 100) anchored
bottom-center, offset **left** of the existing 400×20 health bar. Layout strip:

```
 [ ring ] │ weaponName │ │  ▓▓▓▓▓▓░░░  (existing health bar, separate canvas)
   30/120             white dividers
```

- **Ring**: `Image.Type.Filled`, `FillMethod.Radial360`, `fillOrigin = Top`,
  `fillAmount = bulletsInMagazine / magazineCapacity`. Color by weapon type:
  AutoRifle/Rifle=orange, Pistol/Revolver=yellow, Shotgun=red, Melee=gray, Grenade=green.
- **Center text** (TextMeshPro): `mag / reserve` (e.g. `30 / 120`).
- **Weapon name** (TMP) to the right of the ring: `currentWeapon.weaponData.weaponName`.
- **White dividers**: 2px white `Image` at ~60% alpha between ring→name and name→health bar.
- Reads `player.weapon.CurrentWeapon()` each frame → reflects real shooting/reloading.
- Per-type behavior:
  - **Grenade**: ring = remaining count / capacity, center shows count, label "GRENADE".
  - **Melee**: ring full + dimmed, center "∞", label "MELEE".
- `UI_HudRoot` holds references and updates both; only enabled for the owner.

**Positioning note:** the health bar lives on its own canvas at bottom-center width 400.
The HUD canvas places the ring at `anchoredPosition = (-(400/2 + ringRadius + gap), 30)` so
the strip sits flush-left of the bar. Offset is serialized for tuning.

## 8. Inventory panel (B toggle)

**Files:** `Assets/Scripts/UI/UI_InventoryPanel.cs`, `UI_InventoryController.cs`,
`UI_InventorySlot.cs`, `UI_EquipmentSlot.cs`, `UI_DragManager.cs`.

### Layout (Tarkov-style human silhouette)

```
┌─ Equipment ──────────┐ │ ┌─ Backpack (6×4) ──────────┐
│  ┌──┐        ┌──┐    │ │ ┌─┐┌─┐┌─┐┌─┐┌─┐┌─┐          │
│  │P │        │S │    │ │ │R││ ││ ││ ││ ││ │          │
│  │  │        │  │    │ │ └─┘└─┘└─┘└─┘└─┘└─┘          │
│  └──┘        └──┘    │ │ ┌─┐┌─┐┌─┐┌─┐┌─┐┌─┐          │
│       ┌──┐ ┌──┐     │white│ ││A││ ││M││ ││ │          │
│       │G │ │M │     │div │ └─┘└─┘└─┘└─┘└─┘└─┘          │
│       │  │ │  │     │ │ ...                         │
│       └──┘ └──┘     │ │ ┌─┐┌─┐┌─┐┌─┐┌─┐┌─┐          │
│  Primary  Secondary │ │ │ ││ ││ ││ ││ ││ │          │
│  Melee  Grenade     │ │ └─┘└─┘└─┘└─┘└─┘└─┘          │
└─────────────────────┘ │ └────────────────────────────┘
                  white vertical divider between the two panels
```

- **Equipment panel** (left): 4 `UI_EquipmentSlot` arranged in a silhouette: Primary (upper-
  left), Secondary (upper-right), Melee (lower-center-left), Grenade (lower-center-right).
  Each slot shows a placeholder icon (if equipped), a label, and accepts only its type.
- **Backpack grid** (right): 6 columns × 4 rows of `UI_InventorySlot`, each showing item
  icon + stack count badge.
- **Drag-drop**:
  - `UI_InventorySlot` implements `IBeginDragHandler, IDragHandler, IEndDragHandler` —
    spawns a floating `UI_DragManager` icon following the cursor.
  - `UI_EquipmentSlot` implements `IDropHandler` — validates type; on success calls
    `PlayerInventory.MoveBackpackToEquip`; on failure snaps back.
  - Dragging an equipped slot onto an empty backpack cell → `UnequipToBackpack`.
  - Dragging backpack→backpack reorders.
- **Placeholder icons**: `UI_PlaceholderIcons` caches procedurally generated `Sprite`s
  (1×1 → scaled `Texture2D`, colored by `ItemType`/`WeaponType`) with a letter badge
  (R/P/S/M/G). No external art required.
- **White dividers**: 2px white Images at ~60% alpha between equipment/backpack panels and
  between header rows, consistent with the HUD.
- **Toggle**: `UI_InventoryController.Update()` polls `Keyboard.current.b.wasPressedThisFrame`
  (and Esc) to toggle the panel `GameObject` active state. On open, refreshes all slots from
  `PlayerInventory`. While open: cursor unlocks (`Cursor.lockState = None`,
  `Cursor.visible = true`) and **gameplay input is disabled** via
  `player.controls.Character.Disable()` (blocks Fire/Move/etc., Tarkov-style freeze); on
  close, `Cursor.lockState = Locked`, `Cursor.visible = false`, and
  `player.controls.Character.Enable()`.

### Input

Use the new Input System low-level `Keyboard.current.b.wasPressedThisFrame` — **no edit** to
`PlayerControls.inputactions`, no C# regeneration. (Migration to a proper `PlayerControls`
action is a noted future task.)

## 9. Editor auto-generation tool

**File:** `Assets/Editor/PlayerUISetupTool.cs`

`[MenuItem("Tools/Setup Player UI System")]` — fire-and-forget, idempotent, logs via
`Debug.Log`. Steps:

1. **Placeholder sprites** — generate `Texture2D` assets (white square, type-colored squares
   + letter badges) under `Assets/UI/Icons/`; skip if present.
2. **Starter ItemData assets** — create a few `ItemData` SOs under `Assets/Data/Inventory/`
   referencing existing `Weapon_Data` assets; skip if present.
3. **HUD Canvas prefab** — `Assets/Prefab/UI/PlayerHUD.prefab`: ScreenSpaceOverlay canvas +
   CanvasScaler (1920×1080) + GraphicRaycaster; bottom strip with AmmoRing (radial filled
   Image) + center TMP + weapon name TMP + dividers; attaches `UI_HudRoot` + `UI_AmmoRing`,
   wires SerializeField refs.
4. **Inventory panel prefab** — `Assets/Prefab/UI/PlayerInventoryPanel.prefab` (or nested in
   HUD): equipment silhouette (4 `UI_EquipmentSlot`) + backpack grid (24 `UI_InventorySlot`)
   + dividers; attaches `UI_InventoryPanel` + `UI_InventoryController` + `UI_DragManager`,
   wires all slot refs. Hidden by default.
5. **Patch Player.prefab** — via `PrefabUtility.LoadPrefabContents` (try/finally): add
   `PlayerInventory` component (if missing), add the HUD canvas as a child (disabled by
   default; `PlayerInventory.InitializeLocal()` enables it for the owner), wire
   `PlayerInventory` → `Player_WeaponController` refs. `SaveAsPrefabAsset` + `UnloadPrefabContents`.
6. `AssetDatabase.SaveAssets()` + `EditorUtility.SetDirty` as needed.

Conventions: `private const string` path/GUID constants, no namespace, idempotent per step,
`try/finally` for prefab contents, matches `PlayerGrenadeSetupTool` style.

## 10. Edits to existing files

- **`Player_WeaponController.cs`** — add `EquipFromInventory(int, Weapon_Data)`,
  `GetWeaponAt(int)`, `GetWeaponSlots()`. Minimal, public, no change to existing behavior.
- **`Player.cs`** — cache `public PlayerInventory inventory { get; private set; }` in
  `Awake()`; in `OnStartClient`, if `IsOwner`, call `inventory.InitializeLocal()`.

No other existing files are modified. `UI_HealthBar` is untouched (the HUD sits beside it).

## 11. File summary

**New runtime scripts:**
- `Assets/Scripts/Inventory/ItemData.cs` (SO + `ItemType` + `InventoryItem`)
- `Assets/Scripts/Player/PlayerInventory.cs`
- `Assets/Scripts/UI/UI_HudRoot.cs`
- `Assets/Scripts/UI/UI_AmmoRing.cs`
- `Assets/Scripts/UI/UI_InventoryPanel.cs`
- `Assets/Scripts/UI/UI_InventoryController.cs`
- `Assets/Scripts/UI/UI_InventorySlot.cs`
- `Assets/Scripts/UI/UI_EquipmentSlot.cs`
- `Assets/Scripts/UI/UI_DragManager.cs`
- `Assets/Scripts/UI/UI_PlaceholderIcons.cs`

**New editor tool:**
- `Assets/Editor/PlayerUISetupTool.cs`

**New assets (generated by the tool):**
- `Assets/UI/Icons/*.png` (placeholder sprites)
- `Assets/Data/Inventory/*.asset` (starter ItemData)
- `Assets/Prefab/UI/PlayerHUD.prefab`
- `Assets/Prefab/UI/PlayerInventoryPanel.prefab`

**Edited:**
- `Assets/Scripts/Player/Player_WeaponController.cs`
- `Assets/Scripts/Player/Player.cs`
- `Assets/Prefab/Player.prefab` (via tool)

## 12. Testing / verification

- Run `Tools/Setup Player UI System` in editor → prefabs/assets created, Player.prefab
  patched, no errors in console.
- Enter play mode as host (owner): HUD shows ammo ring + weapon name bottom-center-left of
  health bar; ring depletes as you fire, refills on reload; switching weapons (1-4) updates
  ring color + name; grenade shows count, melee shows ∞.
- Press B → inventory panel opens, cursor unlocks; equipment slots show current loadout;
  backpack shows starter items. Drag a backpack pistol onto the Primary slot → player
  visually equips it (animator + model switch), ammo ring updates. Drag equipped→backpack →
  unequips. Invalid drops snap back.
- Press B / Esc → panel closes, cursor re-locks.
- Non-owner clients: UI not created (component self-disables) — no HUD/panel for them (v1).

## 13. Risks / follow-ups

- **Equip not networked** (v1): other clients won't see drag-equips. Future: add a
  `CmdEquipFromInventory` ServerRpc + `RpcSyncEquip` ObserversRpc, gate the local bridge
  behind `IsOwner`, and have the server authoritative on `weaponSlots`.
- **Input action**: migrate `Keyboard.current.b` polling to a proper `PlayerControls` action
  (edit `.inputactions`, let Unity regenerate C#).
- **Pickup integration**: currently pickups go through `Pickup_Weapon`/`Pickup_Ammo` into
  `weaponSlots`/`totalReserveAmmo`, bypassing `PlayerInventory`. Future: route pickups into
  the inventory model so the panel reflects in-world pickups.
- **Health bar co-location**: the HUD sits beside the separately-canvased `UI_HealthBar`.
  A future cleanup could merge both into one HUD canvas for unified layout.
