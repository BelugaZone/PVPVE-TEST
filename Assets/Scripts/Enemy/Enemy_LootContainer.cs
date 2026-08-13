using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

/// <summary>
/// Server-authoritative loot container on enemy corpses.
///
/// Lifecycle:
///   1. Component is always present but trigger disabled until the enemy dies.
///   2. OnStartServer: loot list is initialized from possibleLoot[].
///   3. Enemy.Die() / RpcDie() calls EnableLootTrigger() to activate the trigger collider.
///   4. Player presses F → UI_CorpseSearch opens; it reads the SyncList directly.
///   5. Player double-clicks a slot → player's own PlayerInventory.CmdTakeLootFromEnemy()
///      ServerRpc is called, which calls ServerTakeLootSlot() on this container.
///   6. SyncList.OnChange fires on every client → UI refreshes automatically.
///   7. Late-joiners receive full SyncList state on connect — no resync needed.
/// </summary>

public class Enemy_LootContainer : NetworkBehaviour
{
    // ──────────────────────────────────────────
    // Inspector configuration
    // ──────────────────────────────────────────

    [Serializable]
    public struct LootEntry
    {
        [Tooltip("weaponName from Weapon_Data, or itemId from ItemData")]
        public string itemId;
        [Tooltip("Stack count to give")]
        public int count;
        [Range(0f, 1f)]
        [Tooltip("Drop probability 0-1")]
        public float dropChance;
    }

    [Header("Loot Configuration")]
    [SerializeField] private LootEntry[] possibleLoot;
    [SerializeField] internal ItemRegistry itemRegistry;   // internal so PlayerInventory can access it

    // ──────────────────────────────────────────
    // Network-synced loot state
    // ──────────────────────────────────────────

    [Serializable]
    public struct LootSlot : IEquatable<LootSlot>
    {
        public string itemId;
        public int    count;
        public bool   taken;

        public bool Equals(LootSlot other) =>
            itemId == other.itemId && count == other.count && taken == other.taken;
    }

    // All clients share this list; only the server writes to it.
    public readonly SyncList<LootSlot> lootSlots = new SyncList<LootSlot>();

    // Global registry for fast distance-based lookup instead of physics triggers
    public static readonly HashSet<Enemy_LootContainer> ActiveLootContainers = new HashSet<Enemy_LootContainer>();

    // ──────────────────────────────────────────
    // Highlight State
    // ──────────────────────────────────────────
    private Renderer _renderer;
    private Material _defaultMat;
    private Material _highlightMat;

    // ──────────────────────────────────────────
    // Events (UI subscribes)
    // ──────────────────────────────────────────
    public event Action OnLootChanged;

    // ──────────────────────────────────────────
    // Private state
    // ──────────────────────────────────────────
    private Collider  _trigger;
    private bool      _lootReady;
    private Transform _ragdollHip;  // The physics-driven hip bone that follows the ragdoll

    // For standalone player corpses (CorpseLoot prefab): the dead player's body this
    // container is bound to. The CorpseLoot prefab itself has no renderer and no animator,
    // so without this link highlight can't glow the body and LootPosition can't track the
    // ragdoll. Set on the server at spawn, synced to clients. Enemy corpses don't use this —
    // their container lives on the enemy's own transform.root, so the mesh is found directly.
    public readonly SyncVar<NetworkObject> corpseBody = new SyncVar<NetworkObject>();
    private Renderer _linkedRenderer;

    /// <summary>
    /// World position of the loot — follows the ragdoll body even after physics moves it.
    /// Falls back to this.transform.position if no ragdoll bone was found.
    /// </summary>
    public Vector3 LootPosition
    {
        get
        {
            if (_ragdollHip != null) return _ragdollHip.position;
            ResolveCorpseBody();
            if (_ragdollHip != null) return _ragdollHip.position;
            return transform.position;
        }
    }

    // ──────────────────────────────────────────
    // Unity / FishNet lifecycle
    // ──────────────────────────────────────────

    private void Awake()
    {
        _trigger = GetComponent<Collider>();
        if (_trigger != null)
        {
            _trigger.isTrigger = true;
            _trigger.enabled   = false;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        InitializeLootServer();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        lootSlots.OnChange += HandleLootChange;

        // For standalone corpses (like player corpses), enable trigger on clients too
        if (GetComponentInParent<Enemy>() == null)
        {
            EnableLootTrigger();
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        lootSlots.OnChange -= HandleLootChange;
    }

    private void OnDestroy()
    {
        ActiveLootContainers.Remove(this);
    }

    // ──────────────────────────────────────────
    // Server: initialize loot list
    // ──────────────────────────────────────────

    private void InitializeLootServer()
    {
        lootSlots.Clear();
        if (possibleLoot != null)
        {
            foreach (var entry in possibleLoot)
            {
                if (string.IsNullOrEmpty(entry.itemId)) continue;
                if (UnityEngine.Random.value <= entry.dropChance)
                {
                    lootSlots.Add(new LootSlot
                    {
                        itemId = entry.itemId,
                        count  = Mathf.Max(1, entry.count),
                        taken  = false
                    });
                }
            }
        }

        // 如果宿主是Boss敌人，追加核心物品（测试用）
        if (GetComponentInParent<Enemy_Boss>() != null || gameObject.name.ToLower().Contains("boss"))
        {
            lootSlots.Add(new LootSlot { itemId = "core", count = 1, taken = false });
            Debug.Log($"[Enemy_LootContainer] Injected core item into boss enemy: {gameObject.name}");
        }

        Debug.Log($"[Enemy_LootContainer] Initialized {lootSlots.Count} loot slots on {gameObject.name}");
    }

    /// <summary>
    /// 在 InitializeLootServer 之后追加一个 loot slot（仅服务器调用）
    /// </summary>
    public void AddLootSlot(string itemId, int count = 1)
    {
        if (!IsServer) return;
        lootSlots.Add(new LootSlot { itemId = itemId, count = count, taken = false });
    }

    public void ServerPopulateLoot(List<LootSlot> newLoot)
    {
        if (!IsServer) return;
        lootSlots.Clear();
        foreach (var slot in newLoot)
        {
            lootSlots.Add(slot);
        }
        Debug.Log($"[Enemy_LootContainer] ServerPopulateLoot added {lootSlots.Count} items on {gameObject.name}");
    }

    // ──────────────────────────────────────────
    // Highlight logic
    // ──────────────────────────────────────────

    public void HighlightActive(bool active)
    {
        ResolveCorpseBody();

        // Prefer the linked corpse-body renderer (player corpses); fall back to a renderer
        // on this container's own root (enemy corpses, where the container sits on the body).
        Renderer r = _linkedRenderer ?? _renderer;
        if (r == null) return;

        if (_highlightMat == null)
        {
            var p = FindObjectOfType<Interactable>(true);
            if (p != null)
            {
                var f = typeof(Interactable).GetField("highlightMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) _highlightMat = (Material)f.GetValue(p);
            }
        }

        if (_highlightMat != null)
        {
            r.material = active ? _highlightMat : _defaultMat;
        }
    }

    /// <summary>
    /// Lazily resolves the body renderer + ragdoll hip used for highlight and LootPosition.
    /// For enemy corpses these live on transform.root. For standalone player corpses they
    /// live on the synced <see cref="corpseBody"/> player object — the CorpseLoot prefab
    /// itself has neither a renderer nor an animator, so without resolving corpseBody the
    /// body never glows and the search prompt sits at the static death spot instead of the
    /// ragdoll.
    /// </summary>
    private void ResolveCorpseBody()
    {
        if (_linkedRenderer == null && corpseBody.Value != null)
        {
            var body = corpseBody.Value;
            Renderer rend = body.GetComponentInChildren<SkinnedMeshRenderer>();
            if (rend == null) rend = body.GetComponentInChildren<MeshRenderer>();
            _linkedRenderer = rend;
            if (_linkedRenderer != null && _defaultMat == null)
                _defaultMat = _linkedRenderer.sharedMaterial;

            if (_ragdollHip == null)
            {
                var a = body.GetComponentInChildren<Animator>();
                if (a != null && a.isHuman)
                    _ragdollHip = a.GetBoneTransform(HumanBodyBones.Hips);
            }
        }

        if (_renderer == null)
        {
            _renderer = transform.root.GetComponentInChildren<SkinnedMeshRenderer>();
            if (_renderer == null)
                _renderer = transform.root.GetComponentInChildren<MeshRenderer>();

            if (_renderer != null && _defaultMat == null) _defaultMat = _renderer.sharedMaterial;
        }
    }

    /// <summary>Server-side: bind this loot container to the dead player's body so each
    /// client can resolve the ragdoll hip + body mesh for tracking and highlight.</summary>
    public void LinkToCorpseBody(NetworkObject corpse)
    {
        if (!IsServer || corpse == null) return;
        corpseBody.Value = corpse;
    }

    // ──────────────────────────────────────────
    // Enable trigger on death
    // ──────────────────────────────────────────

    public void EnableLootTrigger()
    {
        _lootReady = true;

        // Try to find the animator's hips bone, which is the most reliable way!
        var anim = transform.root.GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            var hip = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (hip != null)
                _ragdollHip = hip;
        }

        // Fallback
        if (_ragdollHip == null)
        {
            var rb = transform.root.GetComponentInChildren<Rigidbody>();
            if (rb != null) _ragdollHip = rb.transform;
        }

        if (_trigger != null)
        {
            _trigger.enabled = true;
        }

        ActiveLootContainers.Add(this);
    }

    // ──────────────────────────────────────────
    // Server-side: called by PlayerInventory.CmdTakeLootFromEnemy()
    // Returns true if item was successfully transferred.
    // ──────────────────────────────────────────

    public bool ServerTakeLootSlot(int slotIndex, PlayerInventory inv)
    {
        if (!IsServer)
        {
            Debug.LogError("[Enemy_LootContainer] ServerTakeLootSlot called on non-server!");
            return false;
        }

        if (slotIndex < 0 || slotIndex >= lootSlots.Count)
        {
            Debug.LogWarning($"[Enemy_LootContainer] Invalid slot index {slotIndex} (count={lootSlots.Count})");
            return false;
        }

        LootSlot slot = lootSlots[slotIndex];
        if (slot.taken)
        {
            Debug.LogWarning($"[Enemy_LootContainer] Slot {slotIndex} already taken.");
            return false;
        }
        if (string.IsNullOrEmpty(slot.itemId))
        {
            Debug.LogWarning($"[Enemy_LootContainer] Slot {slotIndex} is empty.");
            return false;
        }
        if (itemRegistry == null)
        {
            // The CorpseLoot prefab (player corpses) ships without an itemRegistry assigned
            // in the inspector. Every PlayerInventory owns one, so resolve it from the player
            // taking the loot — otherwise taking items from player corpses silently fails.
            if (inv != null) itemRegistry = inv.itemRegistry;
        }
        if (itemRegistry == null)
        {
            Debug.LogError("[Enemy_LootContainer] itemRegistry is null! Cannot transfer item.");
            return false;
        }

        bool added = TryAddItemToInventory(inv, slot.itemId, slot.count);
        if (!added)
        {
            Debug.LogWarning($"[Enemy_LootContainer] Could not add '{slot.itemId}' to player inventory (full or unknown item).");
            return false;
        }

        // Mark the slot as taken and explicitly clear the item ID so the UI definitively hides it.
        lootSlots[slotIndex] = new LootSlot
        {
            itemId = "", // Cleared!
            count  = 0,
            taken  = true
        };
        lootSlots.Dirty(slotIndex); // Force SyncList update on clients

        // Fire event immediately (needed for host mode where the server IS the client;
        // the SyncList OnChange callback fires on the next tick which is too late if
        // UpdateClosestContainer already closed the UI in the same frame).
        OnLootChanged?.Invoke();

        Debug.Log($"[Enemy_LootContainer] Player took item from slot {slotIndex}.");
        return true;
    }

    // ──────────────────────────────────────────
    // Inventory transfer (server-side helper)
    // ──────────────────────────────────────────

    private bool TryAddItemToInventory(PlayerInventory inv, string itemId, int count)
    {
        // First: check ItemData (consumables, ammo, non-weapon items)
        ItemData itemData = itemRegistry.GetItem(itemId);
        if (itemData != null)
        {
            int target = FirstEmptyNetBackpackSlot(inv);
            if (target < 0) return false;
            inv.netBackpack[target] = new PlayerInventory.NetItem
            {
                itemId = itemData.itemId,
                count  = count
            };
            return true;
        }

        // Second: check Weapon_Data (weapons identified by weaponName)
        Weapon_Data wd = itemRegistry.GetWeapon(itemId);
        if (wd != null)
        {
            return TryAddWeaponToInventory(inv, wd);
        }

        Debug.LogError($"[Enemy_LootContainer] '{itemId}' not found in ItemRegistry (checked both GetItem and GetWeapon).");
        return false;
    }

    private bool TryAddWeaponToInventory(PlayerInventory inv, Weapon_Data wd)
    {
        // For firearms (Pistol, Revolver, AutoRifle, Rifle, Shotgun): try equip slots 0 then 1
        if (wd.weaponType == WeaponType.Pistol   || wd.weaponType == WeaponType.Revolver  ||
            wd.weaponType == WeaponType.AutoRifle || wd.weaponType == WeaponType.Rifle     ||
            wd.weaponType == WeaponType.Shotgun)
        {
            for (int slot = 0; slot < 2; slot++)
            {
                if (string.IsNullOrEmpty(inv.netEquipment[slot].itemId))
                {
                    inv.netEquipment[slot] = MakeWeaponNetItem(wd);
                    return true;
                }
            }
        }

        // For Melee: equip slot 2
        if (wd.weaponType == WeaponType.Melee)
        {
            if (string.IsNullOrEmpty(inv.netEquipment[2].itemId))
            {
                inv.netEquipment[2] = MakeWeaponNetItem(wd);
                return true;
            }
        }

        // For Grenade: equip slot 3
        if (wd.weaponType == WeaponType.Grenade)
        {
            if (string.IsNullOrEmpty(inv.netEquipment[3].itemId))
            {
                inv.netEquipment[3] = MakeWeaponNetItem(wd);
                return true;
            }
        }

        // All appropriate equip slots occupied — fall back to backpack
        int target = FirstEmptyNetBackpackSlot(inv);
        if (target < 0)
        {
            Debug.LogWarning("[Enemy_LootContainer] Player backpack is full, cannot store weapon.");
            return false;
        }
        inv.netBackpack[target] = new PlayerInventory.NetItem
        {
            itemId       = wd.weaponName,
            count        = 1,
            isWeaponOnly = false,
            ammoInMag    = wd.bulletsInMagazine,
            ammoReserve  = wd.totalReserveAmmo
        };
        return true;
    }

    private static PlayerInventory.NetItem MakeWeaponNetItem(Weapon_Data wd) =>
        new PlayerInventory.NetItem
        {
            itemId       = wd.weaponName,
            count        = 1,
            isWeaponOnly = true,
            ammoInMag    = wd.bulletsInMagazine,
            ammoReserve  = wd.totalReserveAmmo
        };

    private static int FirstEmptyNetBackpackSlot(PlayerInventory inv)
    {
        for (int i = 0; i < inv.netBackpack.Count; i++)
            if (string.IsNullOrEmpty(inv.netBackpack[i].itemId)) return i;
        return -1;
    }

    // ──────────────────────────────────────────
    // SyncList change handler
    // ──────────────────────────────────────────

    private void HandleLootChange(SyncListOperation op, int index, LootSlot oldSlot, LootSlot newSlot, bool asServer)
    {
        OnLootChanged?.Invoke();
    }

    // ──────────────────────────────────────────
    // Helpers for UI
    // ──────────────────────────────────────────

    public bool HasLoot()
    {
        foreach (var s in lootSlots)
            if (!s.taken && !string.IsNullOrEmpty(s.itemId)) return true;
        return false;
    }

    private void EnsureItemRegistry()
    {
        if (itemRegistry != null) return;
        var playerInv = FindObjectOfType<PlayerInventory>();
        if (playerInv != null) itemRegistry = playerInv.itemRegistry;
    }

    public string GetDisplayName(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= lootSlots.Count) return "";
        var slot = lootSlots[slotIndex];
        EnsureItemRegistry();
        if (itemRegistry == null) return slot.itemId;

        ItemData   id = itemRegistry.GetItem(slot.itemId);
        if (id != null) return id.itemName;

        Weapon_Data wd = itemRegistry.GetWeapon(slot.itemId);
        if (wd != null) return wd.weaponName;

        return slot.itemId;
    }

    public Sprite GetIcon(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= lootSlots.Count) return UI_PlaceholderIcons.White();
        var slot = lootSlots[slotIndex];
        EnsureItemRegistry();
        if (itemRegistry == null) return UI_PlaceholderIcons.White();

        // Try custom icon by itemId first (covers both ItemData.itemId and Weapon_Data.weaponName).
        var custom = UI_PlaceholderIcons.GetCustomIcon(slot.itemId);
        if (custom != null) return custom;

        ItemData id = itemRegistry.GetItem(slot.itemId);
        if (id != null)
            return id.icon != null ? id.icon : UI_PlaceholderIcons.Get(id.itemType);

        Weapon_Data wd = itemRegistry.GetWeapon(slot.itemId);
        if (wd != null) return UI_PlaceholderIcons.Get(wd.weaponType);

        return UI_PlaceholderIcons.White();
    }

    /// <summary>Returns all un-taken loot slots (for restoring to player on respawn).</summary>
    public List<LootSlot> GetRemainingLoot()
    {
        var remaining = new List<LootSlot>();
        foreach (var s in lootSlots)
        {
            if (!s.taken && !string.IsNullOrEmpty(s.itemId))
                remaining.Add(s);
        }
        return remaining;
    }
}
