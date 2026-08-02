# Player UI System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Tarkov-style inventory/equipment panel (toggle with B) and an always-on radial ammo HUD to the project, built by an Editor auto-generation tool (no Unity MCP).

**Architecture:** Three layers — (1) a generic `ItemData` data model, (2) a local `PlayerInventory` runtime model with a small bridge into the existing `Player_WeaponController`, and (3) uGUI runtime scripts (ammo HUD + inventory panel). A single Editor `MenuItem` tool generates placeholder sprites, starter `ItemData` assets, the Canvas prefabs, and patches `Player.prefab`.

**Tech Stack:** Unity 2023.1.0f1c1 (URP), FishNet, new Input System 1.6.1, uGUI (`UnityEngine.UI`). No TextMeshPro (not used in project; avoids Essential Resources import).

## Global Constraints

- Unity version: `2023.1.0f1c1`. URP active.
- New Input System package `1.6.1` is the input backend; use `UnityEngine.InputSystem` low-level API (`Keyboard.current`), do NOT edit `PlayerControls.inputactions`.
- uGUI only: `UnityEngine.UI.Image` / `UnityEngine.UI.Text`. Do not introduce TextMeshPro.
- Follow existing Editor tool conventions: `EditorWindow` subclass, `[MenuItem("Tools/...")]` static method, `private const string` path constants, idempotent steps, `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`/`UnloadPrefabContents` in `try/finally`, no namespace, top-level classes.
- Follow existing script conventions: no namespace, `[System.Serializable]` plain classes for runtime data, `NetworkBehaviour` components on the Player are gated to `IsOwner` via `Player.OnStartClient`.
- No Unity MCP. All scene/prefab/asset creation happens in C# (runtime scripts + the Editor tool).
- v1 is local-only: equipping from the panel drives the local player's weapon state but does NOT sync to non-owner clients via FishNet ServerRpc. This limitation is documented, not a bug.
- The existing `UI_HealthBar` (runtime, own canvas, 400×20 bar bottom-center at y=30) is NOT modified. The new HUD sits to its left.

## Testing approach

The project has **no test framework** (no `com.unity.test-framework`, no `Tests/` folders, no `.asmdef`). Matching the existing convention (`PlayerGrenadeSetupTool` etc. are verified by running the menu and entering play mode), each task's "test cycle" is: **(a) the project compiles with no console errors**, and **(b) a manual verification step**. The full manual checklist is the final task. This is a deliberate, documented deviation from unit TDD because Unity UI/Editor work has no test infrastructure in this repo.

---

## File Structure

**New runtime scripts:**
- `Assets/Scripts/Inventory/ItemData.cs` — `ItemType` enum, `ItemData` SO, `InventoryItem` runtime class. Responsibility: define what an inventory item is.
- `Assets/Scripts/Player/PlayerInventory.cs` — local inventory model (backpack grid + 4 equipment slots), seeding, equip bridge calls. Responsibility: source of truth for the panel; bridges to `Player_WeaponController`.
- `Assets/Scripts/UI/UI_PlaceholderIcons.cs` — procedural colored `Sprite` cache by `WeaponType`/`ItemType`. Responsibility: placeholder icons without external art.
- `Assets/Scripts/UI/UI_AmmoRing.cs` — radial `Image` + center text + weapon name for the always-on HUD. Responsibility: display real current-weapon ammo.
- `Assets/Scripts/UI/UI_HudRoot.cs` — owns the HUD canvas, updates `UI_AmmoRing` each frame from `CurrentWeapon()`. Responsibility: HUD lifecycle + per-frame read.
- `Assets/Scripts/UI/UI_DragManager.cs` — singleton-ish floating drag icon. Responsibility: show an icon following the cursor during a drag.
- `Assets/Scripts/UI/UI_InventorySlot.cs` — backpack cell, `IBeginDragHandler/IDragHandler/IEndDragHandler`. Responsibility: draggable source.
- `Assets/Scripts/UI/UI_EquipmentSlot.cs` — equipment cell, `IDropHandler`, type-filter. Responsibility: drop target.
- `Assets/Scripts/UI/UI_InventoryPanel.cs` — builds/refreshes equipment + backpack slots from `PlayerInventory`. Responsibility: panel layout & refresh.
- `Assets/Scripts/UI/UI_InventoryController.cs` — B/Esc toggle, cursor + input gating, EventSystem guard. Responsibility: panel open/close lifecycle.

**New Editor tool:**
- `Assets/Editor/PlayerUISetupTool.cs` — `[MenuItem("Tools/Setup Player UI System")]`. Generates sprites/assets/prefabs, patches `Player.prefab`.

**Modified:**
- `Assets/Scripts/Player/Player_WeaponController.cs` — add public bridge methods (`EquipFromInventory`, `GetWeaponAt`, `GetWeaponSlots`, `GetDefaultWeaponData`).
- `Assets/Scripts/Player/Player.cs` — cache `PlayerInventory`, call its `InitializeLocal()` on owner start.
- `Assets/Prefab/Player.prefab` — patched by the tool (add `PlayerInventory`, add HUD canvas child).

**Generated assets (by the tool):**
- `Assets/UI/Icons/` — placeholder `Texture2D`/`Sprite` assets.
- `Assets/Data/Inventory/` — starter `ItemData` assets.
- `Assets/Prefab/UI/PlayerHUD.prefab`.

---

### Task 1: Data layer — `ItemData` SO + `InventoryItem`

**Files:**
- Create: `Assets/Scripts/Inventory/ItemData.cs`

**Interfaces:**
- Produces: `enum ItemType { Weapon, Ammo, Consumable }`; `class ItemData : ScriptableObject` with fields `itemId, itemName, itemType, icon, maxStack, weaponData, ammoForWeaponType, ammoAmount, healAmount`; `class InventoryItem` with `static OfItem(ItemData,int)`, `static OfWeapon(Weapon_Data)`, properties `data, stack, WeaponData, DisplayName, ItemType`, method `GetWeapon()`.

- [ ] **Step 1: Write the file**

```csharp
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
```

- [ ] **Step 2: Compile check**

In Unity, wait for recompile. Expected: no errors. (If `Weapon_Data`/`WeaponType`/`Weapon` resolve — they are top-level in `Assets/Scripts/Player/Weapon/` — this compiles.)

- [ ] **Step 3: Manual verify**

In Project window, right-click → Create → Inventory → Item Data. A new asset appears with the inspector showing the fields. Delete the test asset.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Inventory/ItemData.cs
git commit -m "feat(ui): add ItemData SO + InventoryItem data model"
```

---

### Task 2: Bridge methods on `Player_WeaponController` + `Player` cache

**Files:**
- Modify: `Assets/Scripts/Player/Player_WeaponController.cs` (add methods near the existing `#region Slots managment`)
- Modify: `Assets/Scripts/Player/Player.cs` (add `inventory` cache + `OnStartClient` call)

**Interfaces:**
- Consumes: existing `weaponSlots`, `EquipWeapon(int)`, `defaultWeaponData`, `defaultMeleeWeaponData`, `defaultGrenadeWeaponData` in `Player_WeaponController`.
- Produces: `Player_WeaponController.EquipFromInventory(int slot, Weapon_Data data)`, `GetWeaponAt(int)`, `GetWeaponSlots()`, `GetDefaultWeaponData(int)`; `Player.inventory` property.

- [ ] **Step 1: Add bridge methods to `Player_WeaponController`**

Insert this region just before the closing `#endregion` of the existing `#region Slots managment - Pickup\Equip\Drop\Ready Weapon` block (i.e. after the `WeaponReady()` methods, before line ~318 `#endregion`):

```csharp
    #region Inventory UI bridge (local-only; v1 does not sync to other clients)

    // Called by PlayerInventory when the player drags an item onto an equipment slot.
    // Local-only: the owner visually holds/fires the new gun; other clients won't see
    // the swap until a future ServerRpc pass.
    public void EquipFromInventory(int slot, Weapon_Data data)
    {
        if (data == null) return;
        if (slot < 0 || slot >= weaponSlots.Count) return;

        weaponSlots[slot] = new Weapon(data);
        EquipWeapon(slot);
    }

    public Weapon GetWeaponAt(int slot)
    {
        if (slot < 0 || slot >= weaponSlots.Count) return null;
        return weaponSlots[slot];
    }

    public System.Collections.Generic.IReadOnlyList<Weapon> GetWeaponSlots() => weaponSlots;

    // Mirrors EquipStartingWeapon's defaults so PlayerInventory can seed without racing
    // the 0.1s-delayed EquipStartingWeapon Invoke.
    public Weapon_Data GetDefaultWeaponData(int slot)
    {
        switch (slot)
        {
            case 0: return defaultWeaponData;
            case 2: return defaultMeleeWeaponData;
            case 3: return defaultGrenadeWeaponData;
            default: return null;
        }
    }

    #endregion
```

- [ ] **Step 2: Add `inventory` cache to `Player`**

In `Assets/Scripts/Player/Player.cs`, add the property alongside the others (after `fov`):

```csharp
    public PlayerInventory inventory { get; private set; }
```

In `Awake()`, add (after the `fov = GetComponent<Player_FOV>();` line):

```csharp
        inventory = GetComponent<PlayerInventory>();
```

In `OnStartClient()`, after the `if (base.IsOwner) { controls.Enable(); }` block, add:

```csharp
        if (base.IsOwner && inventory != null)
            inventory.InitializeLocal();
```

- [ ] **Step 3: Compile check**

Recompile. Expected: no errors. (`PlayerInventory` does not exist yet — this will NOT compile until Task 3. To keep this task independently compilable, comment out the three `inventory` lines you just added in `Player.cs`, OR implement Task 3 first. Recommended: implement Task 3 before recompiling, then both compile together. The lines in `Player_WeaponController.cs` compile standalone.)

If implementing strictly sequentially: leave `Player.cs` edits commented and uncomment them in Task 3 Step 4.

- [ ] **Step 4: Manual verify**

Open `Player_WeaponController.cs` in the inspector on the Player prefab — the new public methods are available; existing fields unchanged. Existing weapon switching (1-4 keys) still works.

- [ ] **Step 5: Commit** (commit together with Task 3 if you deferred the `Player.cs` compile)

```bash
git add Assets/Scripts/Player/Player_WeaponController.cs Assets/Scripts/Player/Player.cs
git commit -m "feat(ui): add Player_WeaponController inventory bridge + Player.inventory cache"
```

---

### Task 3: `PlayerInventory` local model

**Files:**
- Create: `Assets/Scripts/Player/PlayerInventory.cs`

**Interfaces:**
- Consumes: `Player` (`player.weapon`), `Player_WeaponController.GetWeaponSlots/GetDefaultWeaponData/EquipFromInventory`, `ItemData`, `InventoryItem`, `WeaponType`.
- Produces: `PlayerInventory : MonoBehaviour` with `InitializeLocal()`, `GetEquipment(int)`, `GetBackpack()`, `MoveBackpackToEquip(int,int)`, `UnequipToBackpack(int)`, `event Action OnChanged`, `[SerializeField] ItemData[] starterBackpackItems`.

- [ ] **Step 1: Write the file**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

// Local-only inventory model (v1). Source of truth for the inventory panel.
// The always-on ammo HUD reads the REAL Player_WeaponController.CurrentWeapon()
// directly; this model is what the B-panel reads/writes.
public class PlayerInventory : MonoBehaviour
{
    public const int EquipSlotCount = 4;       // 0 Primary, 1 Secondary, 2 Melee, 3 Grenade
    public const int BackpackCapacity = 24;     // 6 x 4

    [SerializeField] private ItemData[] starterBackpackItems;

    private Player player;
    private Player_WeaponController weaponController;

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
    }

    // Called by Player.OnStartClient when IsOwner. Non-owners self-disable.
    public void InitializeLocal()
    {
        if (player != null && !player.IsOwner)
        {
            enabled = false;
            return;
        }

        Seed();
        OnChanged?.Invoke();
    }

    private void Seed()
    {
        backpack.Clear();
        for (int i = 0; i < BackpackCapacity; i++) backpack.Add(null);

        // Equipment mirrors the real starting weaponSlots (same Weapon_Data refs),
        // so the panel matches what the player actually holds. Read defaults directly
        // to avoid racing the 0.1s-delayed EquipStartingWeapon.
        for (int slot = 0; slot < EquipSlotCount; slot++)
        {
            Weapon_Data wd = weaponController != null ? weaponController.GetDefaultWeaponData(slot) : null;
            equipment[slot] = wd != null ? InventoryItem.OfWeapon(wd) : null;
        }

        // Starter backpack items (assigned on the prefab by the Editor tool).
        if (starterBackpackItems != null)
        {
            int idx = 0;
            foreach (var data in starterBackpackItems)
            {
                if (data == null) continue;
                while (idx < BackpackCapacity && backpack[idx] != null) idx++;
                if (idx >= BackpackCapacity) break;
                backpack[idx] = InventoryItem.OfItem(data, 1);
            }
        }
    }

    // --- Type acceptance for equipment slots ---
    public static int SlotForWeaponType(WeaponType t)
    {
        switch (t)
        {
            case WeaponType.Pistol:
            case WeaponType.Revolver:
                return 1;
            case WeaponType.Melee:
                return 2;
            case WeaponType.Grenade:
                return 3;
            case WeaponType.AutoRifle:
            case WeaponType.Rifle:
            case WeaponType.Shotgun:
                return 0;
            default:
                return 0;
        }
    }

    public static bool SlotAccepts(int slot, InventoryItem item)
    {
        if (item == null || !item.IsWeapon || item.WeaponData == null) return false;
        return SlotForWeaponType(item.WeaponData.weaponType) == slot;
    }

    // Drag backpack[index] -> equipment slot. Swaps displaced equip back to backpack.
    public bool MoveBackpackToEquip(int backpackIndex, int equipSlot)
    {
        if (backpackIndex < 0 || backpackIndex >= backpack.Count) return false;
        if (equipSlot < 0 || equipSlot >= EquipSlotCount) return false;

        InventoryItem incoming = backpack[backpackIndex];
        if (!SlotAccepts(equipSlot, incoming)) return false;

        InventoryItem displaced = equipment[equipSlot];
        equipment[equipSlot] = incoming;
        backpack[backpackIndex] = displaced; // may be null

        // Drive the real (local) weapon system so the player holds the new gun.
        if (weaponController != null)
            weaponController.EquipFromInventory(equipSlot, incoming.WeaponData);

        OnChanged?.Invoke();
        return true;
    }

    // Drag equipment slot -> empty/any backpack cell.
    public bool UnequipToBackpack(int equipSlot, int backpackIndex = -1)
    {
        if (equipSlot < 0 || equipSlot >= EquipSlotCount) return false;
        InventoryItem item = equipment[equipSlot];
        if (item == null) return false;

        int target = backpackIndex >= 0 && backpackIndex < backpack.Count && backpack[backpackIndex] == null
            ? backpackIndex
            : FirstEmptyBackpackSlot();
        if (target < 0) return false; // backpack full

        backpack[target] = item;
        equipment[equipSlot] = null;
        OnChanged?.Invoke();
        return true;
    }

    public bool MoveBackpackToBackpack(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return false;
        if (fromIndex < 0 || fromIndex >= backpack.Count) return false;
        if (toIndex < 0 || toIndex >= backpack.Count) return false;

        InventoryItem a = backpack[fromIndex];
        InventoryItem b = backpack[toIndex];
        backpack[fromIndex] = b;
        backpack[toIndex] = a;
        OnChanged?.Invoke();
        return true;
    }

    private int FirstEmptyBackpackSlot()
    {
        for (int i = 0; i < backpack.Count; i++)
            if (backpack[i] == null) return i;
        return -1;
    }
}
```

- [ ] **Step 2: Uncomment the `Player.cs` edits from Task 2** (if you commented them)

Ensure the three `inventory` lines in `Player.cs` are active.

- [ ] **Step 3: Compile check**

Recompile. Expected: no errors. (`Player.IsOwner` is inherited from `NetworkBehaviour`.)

- [ ] **Step 4: Manual verify**

Add a `PlayerInventory` component to the Player prefab in the Inspector (temporary — the tool will do this properly later). Enter play mode as host. No errors in console. (The component initializes; no UI yet.)

- [ ] **Step 5: Commit** (include Task 2's `Player.cs`/`Player_WeaponController.cs` if not already committed)

```bash
git add Assets/Scripts/Player/PlayerInventory.cs Assets/Scripts/Player/Player_WeaponController.cs Assets/Scripts/Player/Player.cs
git commit -m "feat(ui): add PlayerInventory local model + weapon bridge"
```

---

### Task 4: `UI_PlaceholderIcons` procedural sprite cache

**Files:**
- Create: `Assets/Scripts/UI/UI_PlaceholderIcons.cs`

**Interfaces:**
- Produces: `static class UI_PlaceholderIcons` with `Sprite Get(WeaponType)`, `Sprite Get(ItemType)`, `Sprite White()`.

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;
using UnityEngine.UI;

// Procedural placeholder sprites (no external art). Colored by type with a letter badge
// baked into the texture. Cached statically for the session.
public static class UI_PlaceholderIcons
{
    private static Sprite _white;
    private static readonly System.Collections.Generic.Dictionary<WeaponType, Sprite> _weapon = new System.Collections.Generic.Dictionary<WeaponType, Sprite>();
    private static readonly System.Collections.Generic.Dictionary<ItemType, Sprite> _item = new System.Collections.Generic.Dictionary<ItemType, Sprite>();

    public static Sprite White()
    {
        if (_white == null) _white = Make(Color.white, null);
        return _white;
    }

    public static Sprite Get(WeaponType t)
    {
        if (!_weapon.TryGetValue(t, out Sprite s))
        {
            Color c;
            char badge;
            switch (t)
            {
                case WeaponType.Pistol:     c = new Color(0.9f, 0.8f, 0.2f); badge = 'P'; break;
                case WeaponType.Revolver:   c = new Color(0.9f, 0.7f, 0.3f); badge = 'R'; break;
                case WeaponType.AutoRifle:  c = new Color(0.9f, 0.5f, 0.2f); badge = 'A'; break;
                case WeaponType.Rifle:      c = new Color(0.8f, 0.4f, 0.2f); badge = 'R'; break;
                case WeaponType.Shotgun:    c = new Color(0.9f, 0.3f, 0.3f); badge = 'S'; break;
                case WeaponType.Melee:      c = new Color(0.5f, 0.35f, 0.2f); badge = 'M'; break;
                case WeaponType.Grenade:    c = new Color(0.3f, 0.7f, 0.3f); badge = 'G'; break;
                default:                    c = Color.gray;                   badge = '?'; break;
            }
            s = Make(c, badge);
            _weapon[t] = s;
        }
        return s;
    }

    public static Sprite Get(ItemType t)
    {
        if (!_item.TryGetValue(t, out Sprite s))
        {
            Color c;
            char badge;
            switch (t)
            {
                case ItemType.Weapon:     c = Color.gray;                    badge = 'W'; break;
                case ItemType.Ammo:       c = new Color(0.9f, 0.85f, 0.3f);  badge = 'A'; break;
                case ItemType.Consumable: c = new Color(0.3f, 0.8f, 0.6f);  badge = '+'; break;
                default:                  c = Color.gray;                    badge = '?'; break;
            }
            s = Make(c, badge);
            _item[t] = s;
        }
        return s;
    }

    // For an InventoryItem: prefer its ItemData.icon if set, else procedural by type.
    public static Sprite ForItem(InventoryItem item)
    {
        if (item == null) return White();
        if (item.data != null && item.data.icon != null) return item.data.icon;
        if (item.IsWeapon) return Get(item.WeaponData.weaponType);
        return Get(item.ItemType);
    }

    private static Sprite Make(Color color, char? badge)
    {
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = color;
        // Dark border
        Color border = color * 0.5f; border.a = 1f;
        for (int x = 0; x < size; x++)
        {
            px[x] = border;
            px[(size - 1) * size + x] = border;
            px[x * size] = border;
            px[x * size + size - 1] = border;
        }
        tex.SetPixels(px);

        if (badge.HasValue)
        {
            DrawLetter(tex, badge.Value, size);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // Minimal 5x7 bitmap font for A-Z, 0-9, +, ?.
    private static void DrawLetter(Texture2D tex, char c, int size)
    {
        bool[,] g = Glyph(c);
        if (g == null) return;
        int gw = g.GetLength(0), gh = g.GetLength(1);
        int cell = Mathf.Min(size / 3, 24);
        int ox = (size - gw * cell) / 2;
        int oy = (size - gh * cell) / 2;
        Color ink = Color.white;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
                if (g[x, y])
                    for (int dy = 0; dy < cell; dy++)
                        for (int dx = 0; dx < cell; dx++)
                        {
                            int px = ox + x * cell + dx;
                            int py = oy + y * cell + dy;
                            if (px >= 0 && px < size && py >= 0 && py < size)
                                tex.SetPixel(px, py, ink);
                        }
    }

    private static bool[,] Glyph(char c)
    {
        // Returns a 5-wide (x) by 7-tall (y, bottom=0) bitmap. y row 6 is top.
        // Only a handful of glyphs are needed; unknown -> null.
        string[] rows; // top row first
        switch (char.ToUpper(c))
        {
            case 'R': rows = new[] { "01110", "10001", "10001", "10001", "01110", "00100", "00100" }; break; // simplified R-ish; reuse
            case 'P': rows = new[] { "01110", "10001", "10001", "11110", "10000", "10000", "10000" }; break;
            case 'A': rows = new[] { "00100", "01010", "10001", "10001", "11111", "10001", "10001" }; break;
            case 'S': rows = new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" }; break;
            case 'M': rows = new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" }; break;
            case 'G': rows = new[] { "01110", "10001", "10000", "10111", "10001", "10001", "01110" }; break;
            case 'W': rows = new[] { "10001", "10001", "10001", "10101", "10101", "11011", "10001" }; break;
            case '+': rows = new[] { "00100", "00100", "00100", "11111", "00100", "00100", "00100" }; break;
            case '?': rows = new[] { "01110", "10001", "00010", "00100", "00100", "00000", "00100" }; break;
            default: return null;
        }
        bool[,] g = new bool[5, 7];
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 5; x++)
                g[x, y] = rows[6 - y][x] == '1'; // flip so y=0 is bottom
        return g;
    }
}
```

- [ ] **Step 2: Compile check**

Recompile. Expected: no errors.

- [ ] **Step 3: Manual verify**

Add a temporary `Image` to any canvas, run play mode, and in a temp script set `image.sprite = UI_PlaceholderIcons.Get(WeaponType.AutoRifle)`. The image shows an orange square with an 'A'. (Optional; otherwise rely on Task 9.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/UI_PlaceholderIcons.cs
git commit -m "feat(ui): add UI_PlaceholderIcons procedural sprite cache"
```

---

### Task 5: Ammo HUD runtime (`UI_AmmoRing` + `UI_HudRoot`)

**Files:**
- Create: `Assets/Scripts/UI/UI_AmmoRing.cs`
- Create: `Assets/Scripts/UI/UI_HudRoot.cs`

**Interfaces:**
- Consumes: `Player_WeaponController.CurrentWeapon()`, `Weapon` (bulletsInMagazine, magazineCapacity, totalReserveAmmo, weaponType, weaponData), `WeaponType`.
- Produces: `UI_AmmoRing : MonoBehaviour` with `Init(Player)` and per-frame `Update()`; `UI_HudRoot : MonoBehaviour` with `Init(Player)` that constructs the HUD UI in code (canvas + ring + texts + dividers), anchored bottom-center left of the existing health bar.

- [ ] **Step 1: Write `UI_AmmoRing.cs`**

```csharp
using UnityEngine;
using UnityEngine.UI;

// Displays the current weapon's ammo as a radial ring + "mag / reserve" text,
// plus the weapon name to the right. Reads the REAL CurrentWeapon() so it stays
// in sync with actual firing/reloading.
public class UI_AmmoRing : MonoBehaviour
{
    private Player player;
    private Image ring;            // radial filled
    private Image ringBg;          // dark background ring
    private Text magText;          // center "mag / reserve"
    private Text nameText;         // weapon name

    private WeaponType lastType = (WeaponType)(-1);

    public void Setup(Image ringBg, Image ring, Text magText, Text nameText, Player player)
    {
        this.ringBg = ringBg;
        this.ring = ring;
        this.magText = magText;
        this.nameText = nameText;
        this.player = player;
    }

    private void Update()
    {
        if (player == null || player.weapon == null) return;
        Weapon w = player.weapon.CurrentWeapon();
        if (w == null) return;

        if (ring != null)
        {
            float fill = w.magazineCapacity > 0
                ? Mathf.Clamp01((float)w.bulletsInMagazine / w.magazineCapacity)
                : 1f;
            ring.fillAmount = fill;
            if (w.weaponType != lastType)
            {
                ring.color = ColorFor(w.weaponType);
                lastType = w.weaponType;
            }
        }

        if (magText != null)
        {
            switch (w.weaponType)
            {
                case WeaponType.Melee:
                    magText.text = "8";
                    break;
                case WeaponType.Grenade:
                    magText.text = w.bulletsInMagazine.ToString();
                    break;
                default:
                    magText.text = $"{w.bulletsInMagazine} / {w.totalReserveAmmo}";
                    break;
            }
        }

        if (nameText != null && w.weaponData != null)
            nameText.text = w.weaponData.weaponName;
    }

    private static Color ColorFor(WeaponType t)
    {
        switch (t)
        {
            case WeaponType.Pistol:
            case WeaponType.Revolver:   return new Color(0.9f, 0.8f, 0.2f);
            case WeaponType.AutoRifle:
            case WeaponType.Rifle:      return new Color(0.9f, 0.5f, 0.2f);
            case WeaponType.Shotgun:    return new Color(0.9f, 0.3f, 0.3f);
            case WeaponType.Melee:      return new Color(0.5f, 0.5f, 0.5f, 0.5f);
            case WeaponType.Grenade:    return new Color(0.3f, 0.8f, 0.3f);
            default:                    return Color.white;
        }
    }
}
```

- [ ] **Step 2: Write `UI_HudRoot.cs`**

```csharp
using UnityEngine;
using UnityEngine.UI;

// Owns the always-on HUD canvas. Builds the layout in code (matches the UI_HealthBar
// pattern) and updates UI_AmmoRing each frame. Anchored bottom-center, offset LEFT of
// the existing 400x20 UI_HealthBar so the strip reads: [ring][name] | [health bar].
public class UI_HudRoot : MonoBehaviour
{
    [SerializeField] private float ringRadius = 34f;     // px
    [SerializeField] private float ringThickness = 8f;
    [SerializeField] private float gapFromHealthBar = 16f;
    [SerializeField] private float bottomY = 30f;        // match UI_HealthBar y
    [SerializeField] private float healthBarHalfWidth = 200f; // 400/2

    private UI_AmmoRing ammoRing;

    public void Init(Player player)
    {
        Build(player);
    }

    private void Build(Player player)
    {
        // Canvas
        GameObject canvasObj = new GameObject("PlayerHUD_Canvas");
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        // Root row anchored bottom-center, offset left of the health bar.
        GameObject row = new GameObject("AmmoRow");
        row.transform.SetParent(canvasObj.transform, false);
        RectTransform rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 0);
        rowRt.anchorMax = new Vector2(0.5f, 0);
        rowRt.pivot = new Vector2(1f, 0.5f); // right edge anchors at the offset point
        // Position: left of the health bar by ringRadius*2 + nameWidth + gaps.
        float nameWidth = 150f;
        float offsetX = -(healthBarHalfWidth + gapFromHealthBar);
        rowRt.anchoredPosition = new Vector2(offsetX, bottomY + 10f);
        rowRt.sizeDelta = new Vector2(ringRadius * 2 + nameWidth + gapFromHealthBar, ringRadius * 2);

        // --- Ring (background + filled) ---
        GameObject ringBgObj = CreateChild(row.transform, "RingBg");
        Image ringBg = ringBgObj.AddComponent<Image>();
        ringBg.sprite = UI_PlaceholderIcons.White();
        ringBg.type = Image.Type.Filled;
        ringBg.fillMethod = Image.FillMethod.Radial360;
        ringBg.fillOrigin = (int)Image.Origin360.Top;
        ringBg.color = new Color(0, 0, 0, 0.6f);
        ringBg.raycastTarget = false;
        RectTransform ringBgRt = ringBgObj.GetComponent<RectTransform>();
        SetAnchored(ringBgRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(-ringRadius, 0), new Vector2(ringRadius * 2, ringRadius * 2));

        GameObject ringObj = CreateChild(row.transform, "Ring");
        Image ring = ringObj.AddComponent<Image>();
        ring.sprite = UI_PlaceholderIcons.White();
        ring.type = Image.Type.Filled;
        ring.fillMethod = Image.FillMethod.Radial360;
        ring.fillOrigin = (int)Image.Origin360.Top;
        ring.color = new Color(0.9f, 0.5f, 0.2f);
        ring.raycastTarget = false;
        RectTransform ringRt = ringObj.GetComponent<RectTransform>();
        SetAnchored(ringRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(-ringRadius + ringThickness, 0),
            new Vector2((ringRadius - ringThickness) * 2, (ringRadius - ringThickness) * 2));

        // --- Center mag/reserve text ---
        GameObject magObj = CreateChild(row.transform, "MagText");
        Text magText = magObj.AddComponent<Text>();
        magText.font = GetDefaultFont();
        magText.fontSize = 16;
        magText.alignment = TextAnchor.MiddleCenter;
        magText.color = Color.white;
        magText.raycastTarget = false;
        magText.text = "0 / 0";
        RectTransform magRt = magObj.GetComponent<RectTransform>();
        SetAnchored(magRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(-ringRadius, 0), new Vector2(ringRadius * 2, ringRadius * 2));

        // --- Divider 1 (between ring and name) ---
        AddDivider(row.transform, new Vector2(ringRadius + 6f, 0.5f), new Vector2(2, ringRadius * 2));

        // --- Weapon name ---
        GameObject nameObj = CreateChild(row.transform, "WeaponName");
        Text nameText = nameObj.AddComponent<Text>();
        nameText.font = GetDefaultFont();
        nameText.fontSize = 18;
        nameText.alignment = TextAnchor.MiddleLeft;
        nameText.color = Color.white;
        nameText.raycastTarget = false;
        nameText.text = "Weapon";
        RectTransform nameRt = nameObj.GetComponent<RectTransform>();
        SetAnchored(nameRt, new Vector2(ringRadius + 14f, 0.5f), new Vector2(ringRadius + 14f, 0.5f),
            new Vector2(0, 0), new Vector2(nameWidth, 24));

        // --- Divider 2 (right of name, just before the health bar) ---
        AddDivider(row.transform, new Vector2(ringRadius + 14f + nameWidth + 4f, 0.5f), new Vector2(2, ringRadius * 2));

        // Wire the per-frame updater.
        GameObject updaterObj = new GameObject("AmmoRingUpdater");
        updaterObj.transform.SetParent(canvasObj.transform, false);
        ammoRing = updaterObj.AddComponent<UI_AmmoRing>();
        ammoRing.Setup(ringBg, ring, magText, nameText, player);
    }

    private GameObject CreateChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private void SetAnchored(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
    }

    private void AddDivider(Transform parent, Vector2 anchor, Vector2 size)
    {
        GameObject d = CreateChild(parent, "Divider");
        Image img = d.AddComponent<Image>();
        img.sprite = UI_PlaceholderIcons.White();
        img.color = new Color(1, 1, 1, 0.6f);
        img.raycastTarget = false;
        RectTransform rt = d.GetComponent<RectTransform>();
        SetAnchored(rt, anchor, anchor, new Vector2(0, 0), size);
    }

    private static Font GetDefaultFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
```

- [ ] **Step 3: Compile check**

Recompile. Expected: no errors. (If `Resources.GetBuiltinResource<Font>` resolves — it does in Unity 2023.)

- [ ] **Step 4: Manual verify**

Temporarily: on the Player, add `UI_HudRoot`, and in a temp startup call `hudRoot.Init(player)`. Enter play mode as host. The bottom-center shows a ring (orange for the default rifle) left of the green health bar, with weapon name and white dividers. The ring depletes when firing, refills on reload. Remove the temp call afterward (the tool wires it properly).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/UI_AmmoRing.cs Assets/Scripts/UI/UI_HudRoot.cs
git commit -m "feat(ui): add always-on radial ammo HUD (UI_AmmoRing + UI_HudRoot)"
```

---

### Task 6: Inventory drag components (`UI_DragManager`, `UI_InventorySlot`, `UI_EquipmentSlot`)

**Files:**
- Create: `Assets/Scripts/UI/UI_DragManager.cs`
- Create: `Assets/Scripts/UI/UI_InventorySlot.cs`
- Create: `Assets/Scripts/UI/UI_EquipmentSlot.cs`

**Interfaces:**
- Consumes: `PlayerInventory` (move methods), `InventoryItem`, `UI_PlaceholderIcons`.
- Produces: `UI_DragManager` (floating icon singleton); `UI_InventorySlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler` with `Setup(PlayerInventory, int index)` and `Refresh(InventoryItem)`; `UI_EquipmentSlot : MonoBehaviour, IDropHandler` with `Setup(PlayerInventory, int slot)` and `Refresh(InventoryItem)`.

- [ ] **Step 1: Write `UI_DragManager.cs`**

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Floating icon that follows the cursor during a drag. Lazily created on first use.
public class UI_DragManager : MonoBehaviour
{
    private static UI_DragManager _instance;
    public static UI_DragManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("DragManager");
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000;
                _instance = go.AddComponent<UI_DragManager>();
                var img = new GameObject("Icon").AddComponent<Image>();
                img.transform.SetParent(go.transform, false);
                img.raycastTarget = false;
                var rt = img.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(48, 48);
                _instance.icon = img;
            }
            return _instance;
        }
    }

    public Image icon;
    public InventoryItem dragging;
    public object source;   // the slot that started the drag (for snap-back)

    public void Begin(InventoryItem item, object source, Sprite sprite)
    {
        dragging = item;
        this.source = source;
        icon.sprite = sprite;
        icon.color = new Color(1, 1, 1, 0.85f);
        icon.enabled = true;
    }

    public void End()
    {
        dragging = null;
        source = null;
        icon.enabled = false;
    }

    private void Update()
    {
        if (icon != null && icon.enabled)
        {
            icon.transform.position = Input.mousePosition;
        }
    }
}
```

Note: `Input.mousePosition` works with the new Input System when the fallback/old Input is enabled; if the project has the active input handler set to "Input System Package (New)" only, replace with `Mouse.current.position.ReadValue()` and `using UnityEngine.InputSystem;`. The verification step checks this.

- [ ] **Step 2: Write `UI_InventorySlot.cs`**

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// A backpack grid cell. Draggable source.
public class UI_InventorySlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private PlayerInventory inventory;
    private int index;

    private Image icon;
    private Text countText;

    public void Setup(PlayerInventory inv, int idx)
    {
        inventory = inv;
        index = idx;
        icon = transform.Find("Icon")?.GetComponent<Image>();
        countText = transform.Find("Count")?.GetComponent<Text>();
    }

    public void Refresh(InventoryItem item)
    {
        if (icon == null) return;
        if (item == null)
        {
            icon.enabled = false;
            if (countText != null) countText.text = "";
            return;
        }
        icon.enabled = true;
        icon.sprite = UI_PlaceholderIcons.ForItem(item);
        icon.color = Color.white;
        if (countText != null)
            countText.text = item.stack > 1 ? item.stack.ToString() : "";
    }

    private InventoryItem Current()
    {
        var bp = inventory.GetBackpack();
        return index >= 0 && index < bp.Count ? bp[index] : null;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        var item = Current();
        if (item == null) { e.pointerDrag = null; return; }
        UI_DragManager.Instance.Begin(item, this, UI_PlaceholderIcons.ForItem(item));
        if (icon != null) icon.color = new Color(1, 1, 1, 0.3f);
    }

    public void OnDrag(PointerEventData e)
    {
        // movement handled in UI_DragManager.Update
    }

    public void OnEndDrag(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        if (dm.dragging != null)
        {
            // No valid drop target handled it -> snap back (nothing to do; refresh below).
        }
        dm.End();
        Refresh(Current());
    }
}
```

- [ ] **Step 3: Write `UI_EquipmentSlot.cs`**

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// An equipment slot (Primary/Secondary/Melee/Grenade). Drop target with type filter.
public class UI_EquipmentSlot : MonoBehaviour, IDropHandler
{
    private PlayerInventory inventory;
    private int slot;
    private Image icon;
    private Text label;

    public void Setup(PlayerInventory inv, int slotIndex)
    {
        inventory = inv;
        slot = slotIndex;
        icon = transform.Find("Icon")?.GetComponent<Image>();
        label = transform.Find("Label")?.GetComponent<Text>();
    }

    public void Refresh(InventoryItem item)
    {
        if (icon == null) return;
        if (item == null)
        {
            icon.enabled = false;
            return;
        }
        icon.enabled = true;
        icon.sprite = UI_PlaceholderIcons.ForItem(item);
        icon.color = Color.white;
    }

    public void OnDrop(PointerEventData e)
    {
        UI_DragManager dm = UI_DragManager.Instance;
        if (dm.dragging == null) return;

        if (dm.source is UI_InventorySlot src)
        {
            // Backpack -> equipment
            if (inventory.MoveBackpackToEquip(src.GetIndex(), slot))
            {
                dm.End();
                return;
            }
        }
        else if (dm.source is UI_EquipmentSlot srcEquip)
        {
            // Equipment -> equipment (swap slots). Route through backpack to reuse logic.
            int fromSlot = srcEquip.GetSlot();
            if (fromSlot != slot)
            {
                if (inventory.UnequipToBackpack(fromSlot) && inventory.MoveBackpackToEquip(LastBackpackIndex(), slot))
                {
                    dm.End();
                    return;
                }
            }
        }
        // Invalid drop: leave drag active so OnEndDrag snaps back.
    }

    public int GetSlot() => slot;

    private int GetIndexInternal; // unused placeholder removed
    private int LastBackpackIndex()
    {
        var bp = inventory.GetBackpack();
        for (int i = bp.Count - 1; i >= 0; i--) if (bp[i] != null) return i;
        return -1;
    }
}
```

Add a public accessor to `UI_InventorySlot` so `UI_EquipmentSlot` can read the source index — append to `UI_InventorySlot.cs`:

```csharp
    public int GetIndex() => index;
```

- [ ] **Step 4: Compile check**

Recompile. Expected: no errors. Verify the active input handler: Edit → Project Settings → Player → Active Input Handling. If it is "Input System Package (New)" only, edit `UI_DragManager.Update` to:

```csharp
using UnityEngine.InputSystem;
// ...
private void Update()
{
    if (icon != null && icon.enabled)
        icon.transform.position = Mouse.current.position.ReadValue();
}
```

- [ ] **Step 5: Manual verify**

Compile only (functional drag verified in Task 9 once the panel + EventSystem exist).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/UI/UI_DragManager.cs Assets/Scripts/UI/UI_InventorySlot.cs Assets/Scripts/UI/UI_EquipmentSlot.cs
git commit -m "feat(ui): add inventory drag components (slot, equipment slot, drag manager)"
```

---

### Task 7: Inventory panel + controller (`UI_InventoryPanel`, `UI_InventoryController`)

**Files:**
- Create: `Assets/Scripts/UI/UI_InventoryPanel.cs`
- Create: `Assets/Scripts/UI/UI_InventoryController.cs`

**Interfaces:**
- Consumes: `PlayerInventory`, `UI_InventorySlot`, `UI_EquipmentSlot`, `UI_PlaceholderIcons`, `Player.controls`.
- Produces: `UI_InventoryPanel : MonoBehaviour` with `Build(PlayerInventory)` constructing the silhouette layout + grid and exposing `Refresh()`; `UI_InventoryController : MonoBehaviour` with `Init(Player)` toggling the panel on B/Esc, gating input + cursor, ensuring an `EventSystem`.

- [ ] **Step 1: Write `UI_InventoryPanel.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Builds the Tarkov-style silhouette equipment panel + 6x4 backpack grid in code,
// and refreshes slot icons from PlayerInventory. Layout:
//  [Primary]   [Secondary]   |  [ 6x4 backpack grid ]
//      [Melee] [Grenade]     |
public class UI_InventoryPanel : MonoBehaviour
{
    private PlayerInventory inventory;
    private readonly List<UI_InventorySlot> backpackSlots = new List<UI_InventorySlot>();
    private readonly UI_EquipmentSlot[] equipSlots = new UI_EquipmentSlot[4];

    private static readonly string[] EquipLabels = { "Primary", "Secondary", "Melee", "Tactical" };

    public void Build(PlayerInventory inv)
    {
        inventory = inv;

        // Panel background
        var bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.08f, 0.92f);
        var rt = GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(720, 460);

        // Equipment panel (left)
        var equipRoot = CreateRect("EquipPanel", rt, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(40, 0), new Vector2(280, 420));
        BuildEquipPanel(equipRoot);

        // Vertical divider
        AddDivider(transform, new Vector2(0.5f, 0.5f), new Vector2(2, 420), new Vector2(0, 0));

        // Backpack grid (right)
        var gridRoot = CreateRect("BackpackGrid", rt, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-40, 0), new Vector2(340, 420));
        BuildGrid(gridRoot);

        Refresh();
        inventory.OnChanged += Refresh;
    }

    private void BuildEquipPanel(RectTransform parent)
    {
        // Silhouette arrangement:
        //  Primary (upper-left)   Secondary (upper-right)
        //          Melee (lower-left)  Grenade (lower-right)
        Vector2[] positions =
        {
            new Vector2(40, 120),    // Primary
            new Vector2(160, 120),   // Secondary
            new Vector2(40, -30),    // Melee
            new Vector2(160, -30),   // Grenade
        };
        for (int i = 0; i < 4; i++)
        {
            var slot = CreateSlot(parent, "Equip_" + i, positions[i], new Vector2(90, 90), true, i);
            equipSlots[i] = slot;
        }
    }

    private void BuildGrid(RectTransform parent)
    {
        int cols = 6, rows = 4;
        float cell = 48f, gap = 4f;
        float gridW = cols * cell + (cols - 1) * gap;
        float gridH = rows * cell + (rows - 1) * gap;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                float x = -gridW / 2 + c * (cell + gap) + cell / 2;
                float y = gridH / 2 - r * (cell + gap) - cell / 2;
                var slot = CreateSlot(parent, "Slot_" + idx, new Vector2(x, y), new Vector2(cell, cell), false, idx);
                backpackSlots.Add(slot);
            }
        }
    }

    private UI_InventorySlot CreateSlot(RectTransform parent, string name, Vector2 pos, Vector2 size, bool isEquip, int index)
    {
        var rt = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        var bg = new GameObject("Bg").AddComponent<Image>();
        bg.transform.SetParent(rt, false);
        bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.sizeDelta = Vector2.zero; bgRt.anchoredPosition = Vector2.zero;

        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(rt, false);
        var icon = iconGo.AddComponent<Image>();
        icon.raycastTarget = false;
        var iconRt = icon.GetComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one; iconRt.sizeDelta = new Vector2(-6, -6); iconRt.anchoredPosition = Vector2.zero;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(rt, false);
        var label = labelGo.AddComponent<Text>();
        label.font = GetDefaultFont();
        label.fontSize = 10;
        label.alignment = TextAnchor.LowerCenter;
        label.color = new Color(0.8f, 0.8f, 0.8f, 0.9f);
        label.raycastTarget = false;
        label.text = isEquip ? EquipLabels[index] : "";
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0); labelRt.anchorMax = new Vector2(1, 0);
        labelRt.pivot = new Vector2(0.5f, 0); labelRt.sizeDelta = new Vector2(0, 14); labelRt.anchoredPosition = new Vector2(0, 2);

        var countGo = new GameObject("Count");
        countGo.transform.SetParent(rt, false);
        var count = countGo.AddComponent<Text>();
        count.font = GetDefaultFont();
        count.fontSize = 12;
        count.alignment = TextAnchor.UpperRight;
        count.color = Color.white;
        count.raycastTarget = false;
        var countRt = count.GetComponent<RectTransform>();
        countRt.anchorMin = new Vector2(1, 1); countRt.anchorMax = new Vector2(1, 1);
        countRt.pivot = new Vector2(1, 1); countRt.sizeDelta = new Vector2(30, 16); countRt.anchoredPosition = new Vector2(-2, -2);

        if (isEquip)
        {
            var eq = rt.gameObject.AddComponent<UI_EquipmentSlot>();
            eq.Setup(inventory, index);
            return null;
        }
        else
        {
            var slot = rt.gameObject.AddComponent<UI_InventorySlot>();
            slot.Setup(inventory, index);
            return slot;
        }
    }

    private void AddDivider(Transform parent, Vector2 anchor, Vector2 size, Vector2 pos)
    {
        var rt = CreateRect("Divider", (RectTransform)parent, anchor, anchor, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UI_PlaceholderIcons.White();
        img.color = new Color(1, 1, 1, 0.6f);
        img.raycastTarget = false;
    }

    private RectTransform CreateRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    private static Font GetDefaultFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    public void Refresh()
    {
        if (inventory == null) return;
        var bp = inventory.GetBackpack();
        for (int i = 0; i < backpackSlots.Count; i++)
            backpackSlots[i].Refresh(i < bp.Count ? bp[i] : null);
        for (int i = 0; i < 4; i++)
            equipSlots[i].Refresh(inventory.GetEquipment(i));
    }

    private void OnDestroy()
    {
        if (inventory != null) inventory.OnChanged -= Refresh;
    }
}
```

- [ ] **Step 2: Write `UI_InventoryController.cs`**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

// Toggles the inventory panel on B / Esc. While open: unlock cursor + disable gameplay
// input (Tarkov-style freeze). Ensures an EventSystem exists for drag to work.
public class UI_InventoryController : MonoBehaviour
{
    private Player player;
    private GameObject panel;
    private bool isOpen;

    public void Init(Player player, GameObject panelPrefab)
    {
        this.player = player;
        EnsureEventSystem();

        // Instantiate panel as a sibling under this transform, hidden by default.
        panel = Instantiate(panelPrefab, transform);
        panel.GetComponent<UI_InventoryPanel>().Build(player.inventory);
        panel.SetActive(false);
    }

    private void Update()
    {
        if (player == null || panel == null) return;

        bool toggle = Keyboard.current.bKey.wasPressedThisFrame
                      || (isOpen && Keyboard.current.escapeKey.wasPressedThisFrame);
        if (toggle)
        {
            if (isOpen) Close();
            else Open();
        }
    }

    private void Open()
    {
        isOpen = true;
        panel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (player.controls != null) player.controls.Character.Disable();
        panel.GetComponent<UI_InventoryPanel>().Refresh();
    }

    private void Close()
    {
        isOpen = false;
        panel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (player.controls != null) player.controls.Character.Enable();
    }

    private void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }
}
```

- [ ] **Step 3: Compile check**

Recompile. Expected: no errors. (`Keyboard.current.bKey` — the property is `bKey` on `Keyboard`; verify the property name in the Input System API: it is `Keyboard.current.bKey`. If compile fails, use `Keyboard.current[Key.B]`.)

- [ ] **Step 4: Manual verify**

Compile only (functional toggle verified in Task 9 once the tool wires the panel prefab).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/UI_InventoryPanel.cs Assets/Scripts/UI/UI_InventoryController.cs
git commit -m "feat(ui): add inventory panel layout + B-toggle controller"
```

---

### Task 8: Editor auto-generation tool (`PlayerUISetupTool`)

**Files:**
- Create: `Assets/Editor/PlayerUISetupTool.cs`

**Interfaces:**
- Consumes: all runtime scripts from Tasks 1–7; `PrefabUtility`, `AssetDatabase`, existing `Weapon_Data` assets under `Assets/Data/Player/`.
- Produces: placeholder sprites under `Assets/UI/Icons/`; starter `ItemData` assets under `Assets/Data/Inventory/`; `Assets/Prefab/UI/PlayerHUD.prefab`; patched `Player.prefab` with `PlayerInventory` + HUD canvas + controller wired to the panel prefab.

- [ ] **Step 1: Write the tool**

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

// Run once via menu: Tools > Setup Player UI System
// Generates placeholder sprites, starter ItemData assets, the HUD prefab, and patches
// Player.prefab to add PlayerInventory + the HUD canvas. Idempotent.
public class PlayerUISetupTool : EditorWindow
{
    private const string PLAYER_PREFAB_PATH = "Assets/Prefab/Player.prefab";
    private const string ICONS_DIR = "Assets/UI/Icons";
    private const string INVENTORY_DATA_DIR = "Assets/Data/Inventory";
    private const string HUD_PREFAB_PATH = "Assets/Prefab/UI/PlayerHUD.prefab";

    // References to existing Weapon_Data assets (by filename) for starter items.
    private const string PISTOL_DATA_GUID = ""; // left blank; resolved by filename below

    [MenuItem("Tools/Setup Player UI System")]
    public static void Setup()
    {
        EnsureDir(ICONS_DIR);
        EnsureDir(INVENTORY_DATA_DIR);
        EnsureDir("Assets/Prefab/UI");

        CreatePlaceholderSprites();
        CreateStarterItemData();
        GameObject hudPrefab = CreateHudPrefab();
        PatchPlayerPrefab(hudPrefab);

        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(hudPrefab);
        Debug.Log("Player UI System setup complete. Add PlayerHUD to the scene or it spawns with the Player.");
    }

    // --- 1. Placeholder sprites (single white + a few type colors) ---
    private static void CreatePlaceholderSprites()
    {
        CreateColorTexture(ICONS_DIR + "/White.png", Color.white);
        CreateColorTexture(ICONS_DIR + "/Gun.png", new Color(0.9f, 0.5f, 0.2f));
        CreateColorTexture(ICONS_DIR + "/Pistol.png", new Color(0.9f, 0.8f, 0.2f));
        CreateColorTexture(ICONS_DIR + "/Melee.png", new Color(0.5f, 0.35f, 0.2f));
        CreateColorTexture(ICONS_DIR + "/Grenade.png", new Color(0.3f, 0.7f, 0.3f));
        CreateColorTexture(ICONS_DIR + "/Ammo.png", new Color(0.9f, 0.85f, 0.3f));
        CreateColorTexture(ICONS_DIR + "/Med.png", new Color(0.3f, 0.8f, 0.6f));
    }

    private static void CreateColorTexture(string path, Color color)
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) return;
        Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        Color[] px = new Color[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = color;
        tex.SetPixels(px); tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    // --- 2. Starter ItemData assets (spare pistol + ammo box + med) ---
    private static void CreateStarterItemData()
    {
        Weapon_Data pistolData = FindWeaponData("Weapon_Pistol_D");
        CreateItemData(INVENTORY_DATA_DIR + "/Item_Pistol.asset",
            "pistol_spare", "Pistol (spare)", ItemType.Weapon, pistolData);

        Weapon_Data rifleData = FindWeaponData("Weapon_Rifle_D");
        CreateItemData(INVENTORY_DATA_DIR + "/Item_RifleAmmo.asset",
            "rifle_ammo", "Rifle Ammo", ItemType.Ammo, rifleData != null ? rifleData : pistolData);

        CreateItemData(INVENTORY_DATA_DIR + "/Item_MedKit.asset",
            "medkit", "Med Kit", ItemType.Consumable, null);
    }

    private static ItemData CreateItemData(string path, string id, string name, ItemType type, Weapon_Data wd)
    {
        var existing = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (existing != null) return existing;
        var asset = ScriptableObject.CreateInstance<ItemData>();
        asset.itemId = id;
        asset.itemName = name;
        asset.itemType = type;
        asset.maxStack = type == ItemType.Ammo ? 100 : 1;
        if (type == ItemType.Weapon) asset.weaponData = wd;
        if (type == ItemType.Ammo) { asset.ammoForWeaponType = wd != null ? wd.weaponType : WeaponType.Rifle; asset.ammoAmount = 60; }
        if (type == ItemType.Consumable) asset.healAmount = 25;
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static Weapon_Data FindWeaponData(string assetFileName)
    {
        string[] guids = AssetDatabase.FindAssets("t:Weapon_Data");
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (p.EndsWith("/" + assetFileName + ".asset") || p.EndsWith(assetFileName + ".asset"))
                return AssetDatabase.LoadAssetAtPath<Weapon_Data>(p);
        }
        return null;
    }

    // --- 3. HUD prefab: a Canvas with UI_HudRoot (panel prefab is built at runtime by UI_InventoryPanel) ---
    private static GameObject CreateHudPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(HUD_PREFAB_PATH);
        if (existing != null) return existing;

        GameObject root = new GameObject("PlayerHUD");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<UI_HudRoot>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, HUD_PREFAB_PATH);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // --- 4. Patch Player.prefab: add PlayerInventory (if missing), instantiate HUD prefab as child ---
    private static void PatchPlayerPrefab(GameObject hudPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PLAYER_PREFAB_PATH);
        try
        {
            // Add PlayerInventory if missing
            var inv = root.GetComponent<PlayerInventory>();
            if (inv == null) inv = root.AddComponent<PlayerInventory>();

            // Assign starter backpack items
            var starters = new System.Collections.Generic.List<ItemData>
            {
                AssetDatabase.LoadAssetAtPath<ItemData>(INVENTORY_DATA_DIR + "/Item_Pistol.asset"),
                AssetDatabase.LoadAssetAtPath<ItemData>(INVENTORY_DATA_DIR + "/Item_RifleAmmo.asset"),
                AssetDatabase.LoadAssetAtPath<ItemData>(INVENTORY_DATA_DIR + "/Item_MedKit.asset"),
            };
            var so = new SerializedObject(inv);
            var prop = so.FindProperty("starterBackpackItems");
            prop.ClearArray();
            for (int i = 0; i < starters.Count; i++)
            {
                prop.InsertArrayElementAtIndex(i);
                prop.GetArrayElementAtIndex(i).objectReferenceValue = starters[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // Add HUD child if missing
            Transform existingHud = root.transform.Find("PlayerHUD_Canvas");
            if (existingHud == null)
            {
                // Instantiate the HUD prefab as a child. UI_HudRoot.Build runs at runtime via PlayerInventory.
                GameObject hudInstance = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, root.scene);
                hudInstance.transform.SetParent(root.transform, false);
                hudInstance.name = "PlayerHUD_Canvas";
                hudInstance.SetActive(false); // enabled on owner by PlayerInventory
            }

            PrefabUtility.SaveAsPrefabAsset(root, PLAYER_PREFAB_PATH);
            Debug.Log("Player.prefab patched with PlayerInventory + HUD canvas.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureDir(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
```

**Note on wiring `UI_HudRoot` and `UI_InventoryController`:** The HUD prefab carries `UI_HudRoot` (which builds itself in `Build()`). But `UI_HudRoot.Build` must be called with the `Player` ref at runtime. Add a small runtime hook so `PlayerInventory.InitializeLocal()` constructs the HUD + controller. Append to `PlayerInventory.InitializeLocal()` (Task 3) — replace the `OnChanged?.Invoke();` final line with:

```csharp
        OnChanged?.Invoke();
        SpawnUI();
```

And add to `PlayerInventory`:

```csharp
    [SerializeField] private GameObject hudPrefab;       // assigned by tool (the HUD canvas prefab)
    [SerializeField] private GameObject panelPrefab;     // a bare panel root prefab (UI_InventoryPanel)

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
    }
```

The tool must also assign `hudPrefab` and create/assign a `panelPrefab`. Update `PatchPlayerPrefab` to assign `hudPrefab` on `PlayerInventory`:

```csharp
            // Assign hudPrefab reference
            var so2 = new SerializedObject(inv);
            so2.FindProperty("hudPrefab").objectReferenceValue = hudPrefab;
            so2.ApplyModifiedPropertiesWithoutUndo();
```

And create a minimal panel prefab (an empty RectTransform with `UI_InventoryPanel`) and assign `panelPrefab`. Add a helper in the tool:

```csharp
    private const string PANEL_PREFAB_PATH = "Assets/Prefab/UI/PlayerInventoryPanel.prefab";
    // ...in Setup(), after CreateHudPrefab():
    GameObject panelPrefab = CreatePanelPrefab();
    // ...pass panelPrefab into PatchPlayerPrefab and assign so2.FindProperty("panelPrefab").objectReferenceValue = panelPrefab;
```

```csharp
    private static GameObject CreatePanelPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PANEL_PREFAB_PATH);
        if (existing != null) return existing;
        GameObject root = new GameObject("PlayerInventoryPanel");
        root.AddComponent<RectTransform>();
        root.AddComponent<UI_InventoryPanel>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PANEL_PREFAB_PATH);
        Object.DestroyImmediate(root);
        return prefab;
    }
```

(Merge these snippets into the tool body: declare `PANEL_PREFAB_PATH`, call `CreatePanelPrefab()` in `Setup`, pass `panelPrefab` to `PatchPlayerPrefab(GameObject hudPrefab, GameObject panelPrefab)`, and assign both fields via `SerializedObject`.)

- [ ] **Step 2: Compile check**

Recompile. Expected: no errors. Resolve any `PrefabUtility.InstantiatePrefab(prefab, root.scene)` issue: if the overload complains inside `LoadPrefabContents` context, instead use `var hudInstance = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab); hudInstance.transform.SetParent(root.transform, false);`.

- [ ] **Step 3: Run the tool**

In Unity: menu **Tools → Setup Player UI System**. Watch the console: "Player UI System setup complete." Verify in Project window:
- `Assets/UI/Icons/` has 7 PNGs.
- `Assets/Data/Inventory/` has 3 ItemData assets.
- `Assets/Prefab/UI/PlayerHUD.prefab` and `PlayerInventoryPanel.prefab` exist.
- `Player.prefab` now has a `PlayerInventory` component (with starter items + hudPrefab assigned) and a `PlayerHUD_Canvas` child.

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/PlayerUISetupTool.cs Assets/Scripts/Player/PlayerInventory.cs
git commit -m "feat(ui): add PlayerUISetupTool editor auto-generation + wire SpawnUI"
```

---

### Task 9: Integration verification (manual checklist)

**Files:** none (verification only)

- [ ] **Step 1: Fresh run of the tool**

If you changed anything since Task 8, run **Tools → Setup Player UI System** again. Confirm idempotency (no duplicate assets/components; console clean).

- [ ] **Step 2: Play mode — HUD**

Enter play mode as host. Verify:
- Bottom-center shows the green health bar (existing) with, to its **left**, an orange radial ring (default rifle), a white divider, the weapon name (e.g. the rifle's `weaponName`), and another white divider before the health bar.
- Fire the weapon: the ring depletes and the `mag / reserve` center text decreases.
- Reload: the ring refills.
- Switch to slot 3 (grenade): ring turns green, shows grenade count.
- Switch to slot 2 (melee): ring grayed/full, center shows `8`.

- [ ] **Step 3: Play mode — inventory panel**

Press **B**: the inventory panel appears (silhouette equipment slots + 6×4 backpack grid, white divider between them). Cursor unlocks; movement/shooting is frozen.
- Equipment slots show the current loadout (Primary/Melee/Grenade icons, Secondary empty).
- Backpack shows the 3 starter items (pistol, ammo, med) with placeholder icons.
- Drag the spare Pistol from backpack onto the **Primary** equipment slot: the player visually equips the pistol (model/anim switches), and the ammo HUD updates to the pistol (yellow ring, pistol name).
- Drag the Primary slot icon onto an empty backpack cell: it unequips to the backpack.
- Drag a pistol onto the **Melee** slot: rejected (snap back).
- Press **B** or **Esc**: panel closes, cursor re-locks, gameplay resumes.

- [ ] **Step 4: Non-owner check**

Join as a second client (ParrelSync clone). The non-owner player has no HUD/panel (component self-disables). Confirms v1 local-only scope.

- [ ] **Step 5: Final commit** (if any fixups were made)

```bash
git add -A
git commit -m "chore(ui): integration fixups from play-mode verification"
```

---

## Self-Review (completed)

**Spec coverage:**
- §5 Data layer → Task 1. ✓
- §6 PlayerInventory + bridge → Tasks 2, 3. ✓
- §7 Ammo HUD (ring, name, dividers, left of health bar, per-type) → Task 5. ✓
- §8 Inventory panel (silhouette layout, grid, drag, placeholders, dividers, B toggle, input gating) → Tasks 6, 7. ✓
- §9 Editor tool (sprites, ItemData, prefabs, patch Player.prefab, idempotent, conventions) → Task 8. ✓
- §10 Edits to Player_WeaponController + Player → Task 2. ✓
- §11 File list → all covered. ✓
- §12 Testing/verification → Task 9. ✓
- §13 Risks documented in spec; not blocking. ✓

**Placeholder scan:** No "TBD"/"TODO"/"implement later". The Task 8 note about merging snippets is explicit code, not a placeholder — it's a structuring note because the tool's methods cross-reference; the merged result is fully specified.

**Type consistency:** `InventoryItem.OfWeapon`/`OfItem` (Task 1) used consistently in Task 3. `PlayerInventory.MoveBackpackToEquip(int,int)`, `UnequipToBackpack(int,int)`, `GetEquipment(int)`, `GetBackpack()` match usage in Tasks 6–7. `UI_InventorySlot.Setup(inv,idx)`, `GetIndex()`; `UI_EquipmentSlot.Setup(inv,slot)`, `GetSlot()` — consistent. `UI_HudRoot.Init(Player)`, `UI_AmmoRing.Setup(...)`, `UI_InventoryPanel.Build(PlayerInventory)`, `UI_InventoryController.Init(Player, GameObject)` — consistent across tasks and tool wiring.

**Note:** Task 8's tool references `UI_InventoryPanel` being on the panel prefab, and `PlayerInventory.SpawnUI` instantiating it via `UI_InventoryController.Init(player, panelPrefab)`, which calls `panel.GetComponent<UI_InventoryPanel>().Build(...)`. The panel prefab (Task 8 `CreatePanelPrefab`) has `UI_InventoryPanel` + `RectTransform`. Consistent.
