# Player Grenade Throw Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a player grenade-throwing ability: press 4 to equip the grenade (reusing melee hold anim), right-click-hold to aim with a visual parabola, left-click to throw a ballistic grenade that explodes with `GroundExplosion_01` VFX and AoE damage.

**Architecture:** Approach A — grenade as a 4th weapon slot (`WeaponType.Grenade`, `HoldType.GrenadeHold=5`) reusing the existing `Weapon`/`Weapon_Data`/`WeaponModel`/equip/fire/visual pipeline. New Animator layer 5 (UpperBody mask) with `Grenade_Idle` (reuses melee hold clip) + `Grenade_Throw` (Grenade Throw.fbx). Reuses `Enemy_Grenade` ballistics + `GroundExplosion_01` prefab. FishNet owner-predict + `[ServerRpc]`/`[ObserversRpc(ExcludeOwner)]` replication, mirroring the bullet and melee RPC patterns.

**Tech Stack:** Unity 2022+ (URP), C#, FishNet Networking (NetworkBehaviour, `[ServerRpc]`, `[ObserversRpc]`), Unity Input System (`PlayerControls`), Animation Rigging, `ObjectPool`.

**Reference spec:** `docs/superpowers/specs/2026-07-31-player-grenade-throw-design.md`

## Global Constraints

- **FishNet** is already integrated (`FishNet.Object` namespace, `[ServerRpc]`, `[ObserversRpc(ExcludeOwner = true)]`, `base.IsOwner`, `base.OwnerId`). Follow the exact RPC attribute style used in `Player_WeaponController` (`Cmd*`/`Rpc*`).
- **No new Input System actions** — reuse `EquipSlot4` (key 4 → `EquipWeapon(3)`, already wired), the `Fire` action (left-click → `isShooting` → `Shoot()`), and legacy `Input.GetMouseButton(1)` for aim.
- **UpperBody mask** guid `8e55b3dbd2cfff24d8411cf9932517dd` (`Assets/Animations/Avatar Masks/UpperBody.mask`) — layer 5 must use this mask.
- **HoldType values map directly to Animator layer indices** (`GrenadeHold = 5`).
- **ObjectPool API:** `ObjectPool.instance.GetObject(GameObject prefab, Transform target)` (positions at `target.position`), `ReturnObject(GameObject obj, float delay = .001f)`. Lazy-initializes a pool on first use.
- **Enemy_Grenade API (current):** `SetupGrenade(LayerMask allyLayerMask, Vector3 target, float timeToTarget, float countdown, float impactPower, int grenadeDamage)`; `CalculateLaunchVelocity(Vector3 target, float timeToTarget)` is private and uses `transform.position`.
- **Asset paths (verbatim):**
  - Grenade model: `Assets/Models/grenade/grenade/Grenade_LOW.fbx`, material `Assets/Models/grenade/grenade/Grenade_Texture.mat`
  - Explosion VFX: `Assets/SineVFX/TopDownEffects/CompleteEffects/From_v2/GroundExplosion_01/GroundExplosion_01.prefab`
  - Throw anim: `Assets/Animations/Player/Grenade Throw.fbx`
  - Animator controller: `Assets/Animations/Animator Controllers/Player.controller`
  - Editor tool precedent: `Assets/Editor/PlayerRollSetupTool.cs`
- **Commit convention:** end messages with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. Commit only C# scripts + .asset/.meta + .controller/.meta + prefab/.meta that this plan creates/modifies. Do NOT commit `Library/`, `obj/`, `Logs/`, `.csproj`, `.sln` (gitignored).
- **Verification:** Unity has no unit-test harness in this project. Each task is verified by (a) the Unity Editor compiling with no errors, and (b) a runtime/play-mode or Editor-menu check described per task. The implementer should run the `Tools/...` Editor menu items where specified and press Play to confirm; report any compile errors verbatim.

---

## File Structure

**New files:**
- `Assets/Scripts/Player/Player_GrenadeAimVisual.cs` — owner-only parabola preview (dotted trajectory + landing marker). Uses shared `Enemy_Grenade.CalculateLaunchVelocity`.
- `Assets/Editor/PlayerGrenadeSetupTool.cs` — Editor menu tool (modeled on `PlayerRollSetupTool.cs`) that extracts the `GrenadeThrow` clip from the fbx, adds the `ThrowGrenade` trigger parameter, creates Animator layer 5 + states + transitions, and attaches the `ThrowGrenadeTrigger` animation event. Run once.
- `Assets/Prefab/Player_Grenade.prefab` (under existing prefab folder) — pooled grenade prefab: `Rigidbody` + `Enemy_Grenade` + child mesh `Grenade_LOW`. Field `explosionFx` = `GroundExplosion_01`.
- `Assets/Scripts/Player/Weapon/GrenadeWeaponData.asset` — `Weapon_Data` ScriptableObject instance for the grenade (created via the Editor `[CreateAssetMenu]`).

**Modified files:**
- `Assets/Scripts/Player/Weapon/Weapon.cs` — add `Grenade` to `WeaponType` enum.
- `Assets/Scripts/Player/Weapon/WeaponModel.cs` — add `GrenadeHold = 5` to `HoldType` enum.
- `Assets/Scripts/Player/Weapon/Weapon_Data.cs` — no code change needed (the SO fields already cover grenade stats); only a new `.asset` instance is created.
- `Assets/Scripts/Enemy/Enemy_Grenade.cs` — refactor `CalculateLaunchVelocity` to `public static` taking an explicit `start` param; add `bool dealDamage = true` param to `SetupGrenade` and a guard in `Explode` to skip damage when false (FX still plays).
- `Assets/Scripts/Player/Player_WeaponController.cs` — `maxSlots` default 3→4; fill slot 3 in `EquipStartingWeapon`; block `DropWeapon` for `Grenade`; add grenade branch in `Shoot()` → `PerformGrenadeThrow()`; add `SpawnGrenade()` (owner-predict) + `CmdThrowGrenade` + `RpcThrowGrenade`; serialize `grenadePrefab`, `grenadeStartPoint` (fallback to `GunPoint`), grenade tunables.
- `Assets/Scripts/Player/Player_WeaponVisuals.cs` — add `PlayGrenadeThrowAnimation()` (set `ThrowGrenade` trigger, zero other upper-body layers, restore via coroutine mirroring melee).
- `Assets/Scripts/Player/Player_AnimationEvents.cs` — add `ThrowGrenadeTrigger()` → `weaponController.SpawnGrenade()`.
- `Assets/Animations/Animator Controllers/Player.controller` — modified by the Editor tool (adds layer 5, states, transitions, `ThrowGrenade` parameter, animation event). No manual edit.
- `Assets/Prefab/Player.prefab` — add `Grenade_Model` child (WeaponModel + mesh), add `Player_GrenadeAimVisual` component, assign `grenadePrefab`/`grenadeStartPoint`/tunables on `Player_WeaponController`, set `defaultGrenadeWeaponData` field (needs adding).

**Boundary note:** Each task below produces a self-contained, independently-compilable increment. Code-only tasks compile cleanly even before the Editor tool / prefab wiring tasks run, because the Animator layer + prefab are referenced via SerializeFields (null-safe until wired).

---

## Task 1: Data layer — enums + grenade Weapon_Data asset

**Files:**
- Modify: `Assets/Scripts/Player/Weapon/Weapon.cs` (enum)
- Modify: `Assets/Scripts/Player/Weapon/WeaponModel.cs` (enum)
- Create: `Assets/Scripts/Player/Weapon/GrenadeWeaponData.asset` (ScriptableObject instance)

**Interfaces:**
- Produces: `WeaponType.Grenade`, `HoldType.GrenadeHold` (=5). The grenade `Weapon_Data` asset exposes `weaponType=Grenade`, `bulletsInMagazine=3`, `magazineCapacity=3`, `totalReserveAmmo=0`, plus tunables `bulletDamage` (explosion damage), `fireRate` (throw cooldown), `gunDistance` (explosion radius), `cameraDistance`, `equipmentSpeed`, `reloadSpeed`.

- [ ] **Step 1: Add `Grenade` to `WeaponType`**

In `Assets/Scripts/Player/Weapon/Weapon.cs`, lines 3–11, add `Grenade` after `Melee`:

```csharp
public enum WeaponType
{
    Pistol,
    Revolver,
    AutoRifle,
    Shotgun,
    Rifle,
    Melee,
    Grenade
}
```

- [ ] **Step 2: Add `GrenadeHold = 5` to `HoldType`**

In `Assets/Scripts/Player/Weapon/WeaponModel.cs`, line 5:

```csharp
public enum HoldType { None = 0, CommonHold = 1, LowHold = 2, HighHold = 3, MeleeHold = 4, GrenadeHold = 5 };
```

- [ ] **Step 3: Verify `Weapon.HaveEnoughBullets()` needs no change**

Open `Assets/Scripts/Player/Weapon/Weapon.cs:219-223`. `HaveEnoughBullets()` returns `true` only for `WeaponType.Melee`, else `bulletsInMagazine > 0`. For `Grenade`, it falls through to `bulletsInMagazine > 0` — correct (1 grenade = 1 bullet). `CanReload()` returns false when `totalReserveAmmo == 0` — correct (no resupply). No edit.

- [ ] **Step 4: Create the `GrenadeWeaponData` ScriptableObject asset**

In Unity Editor: Project window → right-click `Assets/Scripts/Player/Weapon/` → Create → Player → Weapon Data (the `[CreateAssetMenu]` on `Weapon_Data`). Name it `GrenadeWeaponData`. Set in Inspector:
- `Weapon Name`: `Grenade`
- `Weapon Type`: `Grenade`
- `Bullet Damage`: `80` (explosion damage — tunable)
- `Bullets In Magazine`: `3`
- `Magazine Capacity`: `3`
- `Total Reserve Ammo`: `0`
- `Shoot Type`: `Single`
- `Bullets Per Shot`: `1`
- `Fire Rate`: `1` (throws/sec → 1s cooldown)
- `Reload Speed`: `1`
- `Equipment Speed`: `1`
- `Gun Distance`: `5` (explosion radius — tunable)
- `Camera Distance`: `6`

(Leave spread/burst fields at defaults; they're unused for grenades.)

- [ ] **Step 5: Verify compile + commit**

Run: `Tools` menu not needed yet. Confirm Unity console has no compile errors.
Expected: No errors. `WeaponType.Grenade` and `HoldType.GrenadeHold` resolve everywhere.

```bash
git add Assets/Scripts/Player/Weapon/Weapon.cs Assets/Scripts/Player/Weapon/WeaponModel.cs Assets/Scripts/Player/Weapon/GrenadeWeaponData.asset Assets/Scripts/Player/Weapon/GrenadeWeaponData.asset.meta
git commit -m "feat(grenade): add WeaponType.Grenade + HoldType.GrenadeHold + GrenadeWeaponData SO

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 2: Refactor `Enemy_Grenade` for reuse (static launch calc + visual-only mode)

**Files:**
- Modify: `Assets/Scripts/Enemy/Enemy_Grenade.cs`

**Interfaces:**
- Produces: `public static Vector3 CalculateLaunchVelocity(Vector3 start, Vector3 target, float timeToTarget)` — reusable by `Player_GrenadeAimVisual`.
- Produces: `SetupGrenade(LayerMask allyLayerMask, Vector3 target, float timeToTarget, float countdown, float impactPower, int grenadeDamage, bool dealDamage = true)` — `dealDamage=false` makes `Explode` play FX but skip damage/force (visual-only for owner-prediction + non-owner replicas).
- Consumes: nothing new. Existing enemy call site (`Enemy_Range.ThrowGrenade`) keeps working because `dealDamage` defaults `true`.

- [ ] **Step 1: Add a `dealDamage` field and refactor `CalculateLaunchVelocity` to static**

Open `Assets/Scripts/Enemy/Enemy_Grenade.cs`. Add a private field near the other privates (after `private int grenadeDamage;` on line 18):

```csharp
private bool dealDamage = true;
```

Replace the private `CalculateLaunchVelocity` (lines 98–111) with a public static version taking an explicit start:

```csharp
public static Vector3 CalculateLaunchVelocity(Vector3 start, Vector3 target, float timeToTarget)
{
    Vector3 direction = target - start;
    Vector3 directionXZ = new Vector3(direction.x, 0, direction.z);

    Vector3 velocityXZ = directionXZ / timeToTarget;

    float velocityY =
        (direction.y - (Physics.gravity.y * Mathf.Pow(timeToTarget, 2)) / 2) / timeToTarget;

    Vector3 launchVelocity = velocityXZ + Vector3.up * velocityY;

    return launchVelocity;
}
```

- [ ] **Step 2: Update `SetupGrenade` signature + call site**

Replace `SetupGrenade` (lines 74–83) with:

```csharp
public void SetupGrenade(LayerMask allyLayerMask, Vector3 target, float timeToTarget, float countdown, float impactPower, int grenadeDamage, bool dealDamage = true)
{
    canExplode = true;
    this.dealDamage = dealDamage;
    this.grenadeDamage = grenadeDamage;
    this.allyLayerMask = allyLayerMask;
    rb.velocity = CalculateLaunchVelocity(transform.position, target, timeToTarget);
    timer = countdown + timeToTarget;
    this.impactPower = impactPower;
}
```

- [ ] **Step 3: Guard `Explode` to skip damage when `dealDamage == false`**

In `Explode` (lines 30–57), wrap the damage/force loop so visual-only grenades still play FX but don't damage. Replace `Explode` body with:

```csharp
private void Explode()
{
    canExplode = false;
    PlayExplosionFx();

    if (!dealDamage)
        return;

    HashSet<GameObject> uniqueEntities = new HashSet<GameObject>();
    Collider[] colliders = Physics.OverlapSphere(transform.position, impactRadius);

    foreach (Collider hit in colliders)
    {
        IDamagable damagable = hit.GetComponent<IDamagable>();

        if (damagable != null)
        {
            if (IsTargetValid(hit) == false)
                continue;

            GameObject rootEntity = hit.transform.root.gameObject;
            if (uniqueEntities.Add(rootEntity) == false)
                continue;

            damagable.TakeDamage(grenadeDamage);
        }

        ApplyPhysicalForceTo(hit);
    }
}
```

- [ ] **Step 4: Verify the enemy call site is unaffected**

Open `Assets/Scripts/Enemy/Enemy_Range/Enemy_Range.cs` (the `ThrowGrenade` method around line 126). Its existing call `g.SetupGrenade(whatIsAlly, player.transform.position, timeToTarget, explosionTimer, impactPower, grenadeDamage)` still compiles because `dealDamage` defaults `true`. No edit.

- [ ] **Step 5: Verify compile + commit**

Confirm Unity console has no errors. Enemy grenade behavior unchanged (default `dealDamage=true`).

```bash
git add Assets/Scripts/Enemy/Enemy_Grenade.cs
git commit -m "refactor(grenade): make Enemy_Grenade reusable for player (static launch calc, visual-only mode)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 3: Animator setup — Editor tool `PlayerGrenadeSetupTool`

**Files:**
- Create: `Assets/Editor/PlayerGrenadeSetupTool.cs`
- Modify (via running the tool): `Assets/Animations/Animator Controllers/Player.controller`

**Interfaces:**
- Produces: Animator layer 5 "Grenade Weapon Layer" (UpperBody mask, weight 0), states `Grenade_Idle` (motion = melee idle clip) and `Grenade_Throw` (motion = `GrenadeThrow` clip), transition `Grenade_Idle → Grenade_Throw` on `ThrowGrenade` trigger (no exit time, 0.1s blend) and `Grenade_Throw → Grenade_Idle` (exitTime 0.9, 0.15s blend), a new `ThrowGrenade` Trigger parameter, and an animation event on `GrenadeThrow` calling `ThrowGrenadeTrigger` at ~55% of clip length.

- [ ] **Step 1: Write the Editor tool**

Create `Assets/Editor/PlayerGrenadeSetupTool.cs`. Model it on `Assets/Editor/PlayerRollSetupTool.cs`. Full content:

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

public class PlayerGrenadeSetupTool : EditorWindow
{
    private const string FBX_PATH = "Assets/Animations/Player/Grenade Throw.fbx";
    private const string CONTROLLER_PATH = "Assets/Animations/Animator Controllers/Player.controller";
    private const string UPPERBODY_MASK_GUID = "8e55b3dbd2cfff24d8411cf9932517dd";
    private const string CLIP_NAME = "GrenadeThrow";
    private const string IDLE_STATE = "Grenade_Idle";
    private const string THROW_STATE = "Grenade_Throw";
    private const string LAYER_NAME = "Grenade Weapon Layer";
    private const string TRIGGER_PARAM = "ThrowGrenade";

    [MenuItem("Tools/Setup Player Grenade Animation")]
    public static void SetupGrenade()
    {
        // 1. Define the clip on the FBX (rename default take to GrenadeThrow)
        ModelImporter importer = AssetImporter.GetAtPath(FBX_PATH) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"Could not find FBX at {FBX_PATH}");
            return;
        }

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            // Create a clip definition from the default take
            clips = new ModelImporterClipAnimation[]
            {
                new ModelImporterClipAnimation
                {
                    name = CLIP_NAME,
                    takeName = importer.clipAnimations.Length > 0 ? importer.clipAnimations[0].takeName : (importer.defaultClipAnimations.Length > 0 ? importer.defaultClipAnimations[0].takeName : "")
                }
            };
        }
        else if (clips[0].name != CLIP_NAME)
        {
            clips[0].name = CLIP_NAME;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        // 2. Load the AnimationClip from the FBX
        AnimationClip throwClip = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(FBX_PATH))
        {
            if (asset is AnimationClip c && !asset.name.StartsWith("__preview__"))
            {
                if (throwClip == null || asset.name == CLIP_NAME)
                    throwClip = c;
            }
        }
        if (throwClip == null)
        {
            Debug.LogError("Could not load GrenadeThrow AnimationClip from FBX.");
            return;
        }

        // 3. Add the ThrowGrenade trigger parameter
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
        if (controller == null)
        {
            Debug.LogError($"Could not find AnimatorController at {CONTROLLER_PATH}");
            return;
        }
        if (!controller.parameters.Any(p => p.name == TRIGGER_PARAM))
            controller.AddParameter(TRIGGER_PARAM, AnimatorControllerParameterType.Trigger);

        // 4. Add layer 5 "Grenade Weapon Layer" with UpperBody mask
        AvatarMask upperBodyMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(
            AssetDatabase.GUIDToAssetPath(UPPERBODY_MASK_GUID));
        if (upperBodyMask == null)
            Debug.LogWarning("UpperBody mask not found; layer will have no mask.");

        AnimatorControllerLayer grenadeLayer = controller.layers.FirstOrDefault(l => l.name == LAYER_NAME);
        if (grenadeLayer == null)
        {
            grenadeLayer = new AnimatorControllerLayer
            {
                name = LAYER_NAME,
                defaultWeight = 0f,
                syncedLayerIndex = -1,
                blendingMode = AnimatorLayerBlendingMode.Override,
                mask = upperBodyMask
            };
            var layerList = controller.layers.ToList();
            layerList.Add(grenadeLayer);
            controller.layers = layerList.ToArray();
            Debug.Log("Added 'Grenade Weapon Layer' (index 5).");
        }
        AnimatorStateMachine sm = grenadeLayer.stateMachine;

        // 5. Find the melee idle clip to reuse for Grenade_Idle.
        // Load it from the Base Layer's Melee_Idle state motion (that state already
        // references the melee hold clip the player uses when holding a melee weapon).
        AnimatorStateMachine baseSm = controller.layers[0].stateMachine;
        AnimatorState meleeIdleState = null;
        foreach (var cs in baseSm.states)
        {
            if (cs.state.name == "Melee_Idle") { meleeIdleState = cs.state; break; }
        }
        AnimationClip meleeIdleClip = null;
        if (meleeIdleState != null && meleeIdleState.motion is AnimationClip mc)
            meleeIdleClip = mc;

        // 6. Create / find states
        AnimatorState idleState = null, throwState = null;
        foreach (var cs in sm.states)
        {
            if (cs.state.name == IDLE_STATE) idleState = cs.state;
            if (cs.state.name == THROW_STATE) throwState = cs.state;
        }
        if (idleState == null) idleState = sm.AddState(IDLE_STATE);
        if (throwState == null) throwState = sm.AddState(THROW_STATE);
        idleState.motion = meleeIdleClip != null ? meleeIdleClip : throwClip; // reuse melee hold; fallback to throw clip
        throwState.motion = throwClip;

        // 7. Transitions: Idle --ThrowGrenade--> Throw --exitTime 0.9--> Idle
        AnimatorStateTransition idleToThrow = idleState.transitions.FirstOrDefault(t => t.destinationState == throwState);
        if (idleToThrow == null)
        {
            idleToThrow = idleState.AddTransition(throwState);
            idleToThrow.hasExitTime = false;
            idleToThrow.duration = 0.1f;
            idleToThrow.AddCondition(AnimatorConditionMode.If, 0, TRIGGER_PARAM);
        }
        AnimatorStateTransition throwToIdle = throwState.transitions.FirstOrDefault(t => t.destinationState == idleState);
        if (throwToIdle == null)
        {
            throwToIdle = throwState.AddTransition(idleState);
            throwToIdle.hasExitTime = true;
            throwToIdle.exitTime = 0.9f;
            throwToIdle.duration = 0.15f;
        }

        // 8. Animation event on the throw clip at ~55% length -> ThrowGrenadeTrigger
        var events = AnimationUtility.GetAnimationEvents(throwClip);
        if (events == null || events.Length == 0 || System.Array.Find(events, e => e.functionName == "ThrowGrenadeTrigger") == null)
        {
            var evt = new AnimationEvent
            {
                functionName = "ThrowGrenadeTrigger",
                time = throwClip.length * 0.55f,
                floatParameter = 0f,
                intParameter = 0,
                stringParameter = "",
                objectReferenceParameter = null,
                messageOptions = SendMessageOptions.DontRequireReceiver
            };
            var newEvents = new AnimationEvent[events == null ? 1 : events.Length + 1];
            if (events != null) System.Array.Copy(events, newEvents, events.Length);
            newEvents[newEvents.Length - 1] = evt;
            AnimationUtility.SetAnimationEvents(throwClip, newEvents);
            // Mark clip dirty so the event persists on the imported sub-asset
            EditorUtility.SetDirty(throwClip);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Player Grenade Animation Setup Completed. Run this once. Then verify layer index == 5 in the Animator window.");
    }
}
```

- [ ] **Step 2: Run the tool**

In Unity Editor: menu `Tools → Setup Player Grenade Animation`. Watch the Console.
Expected: logs "Added 'Grenade Weapon Layer' (index 5)." and "Player Grenade Animation Setup Completed." with no errors. If the upperbody-mask warning appears, manually assign the mask to layer 5 in the Animator window after running.

- [ ] **Step 3: Verify the Animator**

Open `Assets/Animations/Animator Controllers/Player.controller`. Confirm:
- Layer 5 exists, named "Grenade Weapon Layer", mask = `UpperBody.mask`, default weight 0.
- Parameters list contains `ThrowGrenade` (Trigger).
- Layer 5 state machine has `Grenade_Idle` (motion = melee idle clip) and `Grenade_Throw` (motion = `GrenadeThrow`).
- `Grenade_Throw` clip has an animation event `ThrowGrenadeTrigger` at ~55%.

If the mask wasn't assigned (warning), set it manually now.

- [ ] **Step 4: Verify compile + commit**

Confirm no console errors.

```bash
git add Assets/Editor/PlayerGrenadeSetupTool.cs Assets/Editor/PlayerGrenadeSetupTool.cs.meta "Assets/Animations/Animator Controllers/Player.controller.meta" "Assets/Animations/Animator Controllers/Player.controller" "Assets/Animations/Player/Grenade Throw.fbx.meta"
git commit -m "feat(grenade): add PlayerGrenadeSetupTool + Animator layer 5 (Grenade_Idle/Throw, ThrowGrenade trigger, anim event)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 4: Throw animation + visuals — `Player_WeaponVisuals.PlayGrenadeThrowAnimation` + `Player_AnimationEvents.ThrowGrenadeTrigger`

**Files:**
- Modify: `Assets/Scripts/Player/Player_WeaponVisuals.cs`
- Modify: `Assets/Scripts/Player/Player_AnimationEvents.cs`

**Interfaces:**
- Produces: `Player_WeaponVisuals.PlayGrenadeThrowAnimation()` (sets `ThrowGrenade` trigger, zeroes other upper-body layers, restores via coroutine).
- Produces: `Player_AnimationEvents.ThrowGrenadeTrigger()` → calls `weaponController.SpawnGrenade()`.
- Consumes: `ThrowGrenade` trigger parameter + layer 5 from Task 3; `weaponController.SpawnGrenade()` from Task 6 (called at runtime; compiles fine before Task 6 because `SpawnGrenade` is defined in the same `Player_WeaponController` partial? — no, it's the same class; define a stub now or define `SpawnGrenade` in Task 6 and accept that this task compiles only if the method exists. To keep this task independently compilable, add `SpawnGrenade` as a no-op stub in `Player_WeaponController` here, and Task 6 replaces its body.)

- [ ] **Step 1: Add `PlayGrenadeThrowAnimation` to `Player_WeaponVisuals`**

In `Assets/Scripts/Player/Player_WeaponVisuals.cs`, after `PlayMeleeAnimation` (after line 69), add:

```csharp
public void PlayGrenadeThrowAnimation()
{
    anim.SetTrigger("ThrowGrenade");
    leftHandIK.weight = 0;
    ReduceRigWeight();

    // Zero other upper-body layers so only the Grenade layer plays the throw
    for (int i = 1; i < anim.layerCount; i++)
    {
        anim.SetLayerWeight(i, 0);
    }
    anim.SetLayerWeight(5, 1); // ensure grenade layer active during throw

    StartCoroutine(RestoreWeaponLayersRoutine());
}
```

- [ ] **Step 2: Add a temporary no-op `SpawnGrenade` stub in `Player_WeaponController`**

In `Assets/Scripts/Player/Player_WeaponController.cs`, after `public void EnableMeleeAttackCheck(bool enable)` (around line 75), add:

```csharp
// Temporary stub — replaced by real implementation in the networking task.
public void SpawnGrenade() { }
```

(This keeps Task 4 independently compilable; Task 6 fills in the body.)

- [ ] **Step 3: Add `ThrowGrenadeTrigger` to `Player_AnimationEvents`**

In `Assets/Scripts/Player/Player_AnimationEvents.cs`, after `FinishMeleeAttackCheck` (after line 51), add:

```csharp
// --- Grenade Throw Event ---
public void ThrowGrenadeTrigger()
{
    weaponController.SpawnGrenade();
}
```

- [ ] **Step 4: Verify compile + commit**

Confirm no console errors. (The stub keeps it compiling.)

```bash
git add Assets/Scripts/Player/Player_WeaponVisuals.cs Assets/Scripts/Player/Player_AnimationEvents.cs Assets/Scripts/Player/Player_WeaponController.cs
git commit -m "feat(grenade): PlayGrenadeThrowAnimation + ThrowGrenadeTrigger anim-event bridge

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 5: Parabola preview — `Player_GrenadeAimVisual`

**Files:**
- Create: `Assets/Scripts/Player/Player_GrenadeAimVisual.cs`

**Interfaces:**
- Consumes: `Enemy_Grenade.CalculateLaunchVelocity(Vector3 start, Vector3 target, float timeToTarget)` (Task 2); `player.aim.Aim()`, `player.aim.CanAimPrecisly()`, `player.aim.GetMouseHitInfo()`; `player.weapon.CurrentWeapon()`, `player.weapon.WeaponReady()`.
- Produces: a `MonoBehaviour` rendering a dotted trajectory + landing ring while the grenade is equipped + right-click held. Owner-only.

- [ ] **Step 1: Write the component**

Create `Assets/Scripts/Player/Player_GrenadeAimVisual.cs`:

```csharp
using UnityEngine;

public class Player_GrenadeAimVisual : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Player player;
    [SerializeField] private Transform gunPoint; // fallback if null: player.weapon.GunPoint()

    [Header("Trajectory")]
    [SerializeField] private float timeToTarget = 1.2f;
    [SerializeField] private int sampleCount = 20;
    [SerializeField] private float maxThrowDistance = 25f;
    [SerializeField] private float dotSpacing = 0.4f;

    [Header("Visuals")]
    [SerializeField] private GameObject dotPrefab;     // small quad/sprite
    [SerializeField] private GameObject landingRingPrefab; // flat ring sprite
    [SerializeField] private float dotScale = 0.08f;

    private bool visualsReady;
    private GameObject[] dots;
    private GameObject landingRing;

    private void Awake()
    {
        // Owner-only; disable for everyone else
        if (player == null) player = GetComponent<Player>();
    }

    private void OnEnable()
    {
        // only enable on owner
        if (player != null && !player.IsOwner) enabled = false;
    }

    private void EnsureVisuals()
    {
        if (visualsReady) return;
        if (dotPrefab == null || landingRingPrefab == null)
        {
            // Fallback: create simple primitive dots + ring so the feature works without prefabs
            dotPrefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dotPrefab.GetComponent<Collider>());
            dotPrefab.transform.localScale = Vector3.one * dotScale;
            dotPrefab.SetActive(false);

            landingRingPrefab = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(landingRingPrefab.GetComponent<Collider>());
            landingRingPrefab.transform.localScale = new Vector3(2f, 0.02f, 2f);
            landingRingPrefab.SetActive(false);
        }

        dots = new GameObject[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            dots[i] = Instantiate(dotPrefab, transform);
            dots[i].SetActive(false);
        }
        landingRing = Instantiate(landingRingPrefab, transform);
        landingRing.SetActive(false);
        visualsReady = true;
    }

    private void Update()
    {
        if (player == null || !player.IsOwner) { HideAll(); return; }

        bool shouldShow = player.weapon != null
            && player.weapon.CurrentWeapon() != null
            && player.weapon.CurrentWeapon().weaponType == WeaponType.Grenade
            && player.aim != null && player.aim.CanAimPrecisly()
            && player.weapon.WeaponReady();

        if (!shouldShow) { HideAll(); return; }

        EnsureVisuals();

        Transform start = gunPoint != null ? gunPoint : player.weapon.GunPoint();
        Vector3 startPos = start.position;
        Vector3 aimPoint = player.aim.GetMouseHitInfo().point;

        // Clamp to max distance
        Vector3 toAim = aimPoint - startPos;
        if (toAim.magnitude > maxThrowDistance)
            aimPoint = startPos + toAim.normalized * maxThrowDistance;

        Vector3 v0 = Enemy_Grenade.CalculateLaunchVelocity(startPos, aimPoint, timeToTarget);
        DrawTrajectory(startPos, v0, aimPoint);
    }

    private void DrawTrajectory(Vector3 startPos, Vector3 v0, Vector3 aimPoint)
    {
        float dt = timeToTarget / (sampleCount - 1);
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i * dt;
            Vector3 p = startPos + v0 * t + 0.5f * Physics.gravity * t * t;
            if (dots[i] != null)
            {
                dots[i].SetActive(true);
                dots[i].transform.position = p;
            }
        }
        // Landing marker: approximate impact = where y returns near terrain
        Vector3 landing = ComputeLanding(startPos, v0);
        if (landingRing != null)
        {
            landingRing.SetActive(true);
            landingRing.transform.position = new Vector3(landing.x, startPos.y, landing.z);
        }
    }

    private Vector3 ComputeLanding(Vector3 startPos, Vector3 v0)
    {
        // Step the projectile until it drops below start Y (simplified; matches gravity model)
        Vector3 p = startPos;
        Vector3 v = v0;
        for (int i = 0; i < 300; i++)
        {
            p += v * 0.02f;
            v += Physics.gravity * 0.02f;
            if (p.y <= startPos.y && i > 0) break;
        }
        return p;
    }

    private void HideAll()
    {
        if (!visualsReady) return;
        foreach (var d in dots) if (d != null) d.SetActive(false);
        if (landingRing != null) landingRing.SetActive(false);
    }
}
```

- [ ] **Step 2: Verify compile + commit**

Confirm no console errors. (`Player.IsOwner` — verify `Player` exposes `IsOwner` via `NetworkBehaviour`; if `Player` is a `NetworkBehaviour` then `player.IsOwner` is inherited and valid. The codebase uses `base.IsOwner` inside `Player`'s own methods; from outside, `player.IsOwner` works because `NetworkBehaviour.IsOwner` is public.)

```bash
git add Assets/Scripts/Player/Player_GrenadeAimVisual.cs Assets/Scripts/Player/Player_GrenadeAimVisual.cs.meta
git commit -m "feat(grenade): Player_GrenadeAimVisual parabola preview (dotted trajectory + landing ring)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 6: Throw logic + networking in `Player_WeaponController`

**Files:**
- Modify: `Assets/Scripts/Player/Player_WeaponController.cs`

**Interfaces:**
- Consumes: `WeaponType.Grenade` (Task 1); `Player_WeaponVisuals.PlayGrenadeThrowAnimation()` (Task 4); `Enemy_Grenade.SetupGrenade(...)` with `dealDamage` (Task 2); `ObjectPool.instance`; `player.aim.Aim()`, `player.aim.CanAimPrecisly()`, `player.aim.GetMouseHitInfo()`.
- Produces: `SpawnGrenade()` (replaces stub from Task 4), `PerformGrenadeThrow()`, `[ServerRpc] CmdThrowGrenade(Vector3, Vector3)`, `[ObserversRpc(ExcludeOwner=true)] RpcThrowGrenade(Vector3, Vector3, float)`.

- [ ] **Step 1: Add SerializeFields for grenade config**

In `Assets/Scripts/Player/Player_WeaponController.cs`, in the `[Header("Inventory")]` area (after `defaultMeleeWeaponData`, ~line 33), add:

```csharp
[Header("Grenade")]
[SerializeField] private Weapon_Data defaultGrenadeWeaponData;
[SerializeField] private GameObject grenadePrefab;          // Player_Grenade prefab (Rigidbody + Enemy_Grenade)
[SerializeField] private float grenadeTimeToTarget = 1.2f;
[SerializeField] private float grenadeCountdown = 0.2f;     // fuse after landing
[SerializeField] private float grenadeImpactPower = 500f;
```

- [ ] **Step 2: Set `maxSlots` default to 4 + fill slot 3 in `EquipStartingWeapon`**

Change `maxSlots` default (line 29):

```csharp
[SerializeField] private int maxSlots = 4;
```

In `EquipStartingWeapon` (lines 137–156), after the melee slot fill (after `weaponSlots[2] = new Weapon(defaultMeleeWeaponData);`), add:

```csharp
if (defaultGrenadeWeaponData != null)
{
    weaponSlots[3] = new Weapon(defaultGrenadeWeaponData);
}
```

- [ ] **Step 3: Block `DropWeapon` for Grenade**

In `DropWeapon` (lines 199–212), after the melee guard (`if (currentWeapon.weaponType == WeaponType.Melee) return;`), add:

```csharp
// Don't drop the grenade slot
if (currentWeapon.weaponType == WeaponType.Grenade)
    return;
```

- [ ] **Step 4: Add the grenade branch in `Shoot()`**

In `Shoot()`, right after the `Melee` branch's closing `return;` (after line 264, before the second `WeaponReady()` check at 266), insert:

```csharp
if (currentWeapon.weaponType == WeaponType.Grenade)
{
    PerformGrenadeThrow();
    return;
}
```

- [ ] **Step 5: Implement `PerformGrenadeThrow`**

Add near `PerformMeleeAttack` (after line 297):

```csharp
private void PerformGrenadeThrow()
{
    // Must be aiming (right-click held) and have grenades
    if (player.aim == null || !player.aim.CanAimPrecisly())
        return;
    if (currentWeapon.CanShoot() == false) // count > 0 + fire-rate cooldown
        return;

    isShooting = false;
    currentWeapon.bulletsInMagazine--; // consume 1 grenade

    player.weaponVisuals.PlayGrenadeThrowAnimation();
    // Actual spawn happens on the ThrowGrenadeTrigger animation event -> SpawnGrenade()
}
```

- [ ] **Step 6: Replace the `SpawnGrenade` stub with the real owner-predict implementation**

Replace the stub `public void SpawnGrenade() { }` added in Task 4 with:

```csharp
public void SpawnGrenade()
{
    if (!base.IsOwner) return; // only the owning client spawns from the anim event

    Transform start = GunPoint();
    Vector3 startPos = start.position;
    Vector3 aimPoint = player.aim.Aim().position;

    // Owner-local predicted, visual-only grenade
    SpawnGrenadeVisual(startPos, aimPoint, dealDamage: false);

    CmdThrowGrenade(startPos, aimPoint);
}

private void SpawnGrenadeVisual(Vector3 startPos, Vector3 aimPoint, bool dealDamage)
{
    if (grenadePrefab == null) { Debug.LogWarning("grenadePrefab not assigned on Player_WeaponController"); return; }
    // GetObject positions the instance at startPos (we pass a throwaway transform)
    GameObject grenadeObj = ObjectPool.instance.GetObject(grenadePrefab, transform);
    grenadeObj.transform.position = startPos;

    Enemy_Grenade g = grenadeObj.GetComponent<Enemy_Grenade>();
    if (g != null)
    {
        g.SetupGrenade(
            whatIsAlly,
            aimPoint,
            grenadeTimeToTarget,
            grenadeCountdown,
            grenadeImpactPower,
            currentWeapon.bulletDamage,
            dealDamage);
    }
    else
    {
        Debug.LogWarning("grenadePrefab missing Enemy_Grenade component");
    }
}

[ServerRpc]
private void CmdThrowGrenade(Vector3 spawnPos, Vector3 aimPoint)
{
    // Server spawns the authoritative, damaging grenade
    SpawnGrenadeVisual(spawnPos, aimPoint, dealDamage: true);

    // Replicate a visual grenade to non-owner clients
    RpcThrowGrenade(spawnPos, aimPoint, grenadeTimeToTarget);
}

[ObserversRpc(ExcludeOwner = true)]
private void RpcThrowGrenade(Vector3 spawnPos, Vector3 aimPoint, float timeToTarget)
{
    // Non-owner clients spawn a visual-only grenade following the same arc
    SpawnGrenadeVisual(spawnPos, aimPoint, dealDamage: false);
}
```

- [ ] **Step 7: Verify compile + commit**

Confirm no console errors. Note: `GunPoint()` (line 441) returns `player.weaponVisuals.CurrentWeaponModel().gunPoint` — requires `Grenade_Model`'s `WeaponModel` to be wired on the Player prefab (Task 8); until then, calling `SpawnGrenade` at runtime would NRE, but the code compiles.

```bash
git add Assets/Scripts/Player/Player_WeaponController.cs
git commit -m "feat(grenade): throw logic + CmdThrowGrenade/RpcThrowGrenade networking (owner-predict + server-authoritative damage)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 7: Grenade prefab (pooled projectile with `Enemy_Grenade` + `GroundExplosion_01`)

**Files:**
- Create: `Assets/Prefab/Player_Grenade.prefab`

**Interfaces:**
- Produces: a pooled grenade prefab assigned to `Player_WeaponController.grenadePrefab` (wired in Task 8). The prefab carries `Rigidbody` + `Enemy_Grenade` (with `explosionFx = GroundExplosion_01`, `impactRadius = 5`) + a child mesh `Grenade_LOW` with `Grenade_Texture.mat`.

- [ ] **Step 1: Build the prefab in the Editor**

In Unity:
1. Create an empty GameObject named `Player_Grenade`.
2. Add a `Rigidbody` component (use defaults; `Enemy_Grenade` sets `rb.velocity`).
3. Add the `Enemy_Grenade` component.
4. Drag `Assets/Models/grenade/grenade/Grenade_LOW.fbx` into the hierarchy as a child; assign `Grenade_Texture.mat` to its material slot. Scale the child so the grenade is visible (≈ 50x, adjust).
5. On the `Enemy_Grenade` component, set:
   - `Explosion Fx` → `Assets/SineVFX/TopDownEffects/CompleteEffects/From_v2/GroundExplosion_01/GroundExplosion_01.prefab`
   - `Impact Radius` → `5` (match `Weapon_Data.gunDistance`)
   - `Upwards Multiplier` → `1`
6. Save as a prefab at `Assets/Prefab/Player_Grenade.prefab` (delete the scene instance).

- [ ] **Step 2: Verify `GroundExplosion_01` is pool-friendly**

`Enemy_Grenade.PlayExplosionFx` calls `ObjectPool.instance.GetObject(explosionFx, transform)` + `ReturnObject(newFx, 1)`. Confirm the `GroundExplosion_01` prefab has a self-disable / return-to-pool behavior OR relies on `ReturnObject`'s 1s delayed disable. If the prefab's own particle systems run longer than 1s, increase the `ReturnObject` delay in `Enemy_Grenade.PlayExplosionFx` (line 70) — but do not change it here unless testing shows the FX is cut short. Note any observation for Task 8.

- [ ] **Step 3: Verify + commit**

Confirm prefab saved, no console errors.

```bash
git add Assets/Prefab/Player_Grenade.prefab Assets/Prefab/Player_Grenade.prefab.meta
git commit -m "feat(grenade): Player_Grenade prefab (Enemy_Grenade + GroundExplosion_01 + Grenade_LOW mesh)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 8: Wire everything onto the Player prefab + ObjectPool pre-warm

**Files:**
- Modify: `Assets/Prefab/Player.prefab`
- Modify: `Assets/Scripts/Managers/Object Pool/ObjectPool.cs` (optional pre-warm)

**Interfaces:**
- Consumes: all prior tasks. Final integration + runtime verification.

- [ ] **Step 1: Add the `Grenade_Model` child to the Player prefab**

In the Player prefab (under the same parent as the other `WeaponModel` children, i.e. under `weaponHolder`):
1. Create empty child `Grenade_Model`.
2. Add `WeaponModel` component: `Weapon Type = Grenade`, `Hold Type = GrenadeHold`, `Equip Animation Type = BackEquipAnimation`.
3. Add the `Grenade_LOW.fbx` mesh as a child of `Grenade_Model`; assign `Grenade_Texture.mat`. Position it in the right hand (copy transforms from the melee `WeaponModel`'s hold as a starting point).
4. Add an empty child `GunPoint` under `Grenade_Model` at the throwing release point (near the right hand); assign it to `WeaponModel.gunPoint`.
5. Set `Grenade_Model` inactive by default (the equip animation event activates it via `SwitchOnCurrentWeaponModel`).

- [ ] **Step 2: Add `Player_GrenadeAimVisual` to the Player**

Add the `Player_GrenadeAimVisual` component to the Player root. Assign:
- `Player` → the Player root.
- `Gun Point` → the `Grenade_Model/GunPoint` (or leave null to fall back to `player.weapon.GunPoint()`).
- (Dot/Ring prefabs optional — the script auto-creates primitives if unassigned.)

- [ ] **Step 3: Wire `Player_WeaponController` grenade fields**

On the `Player_WeaponController` component of the Player prefab, assign:
- `Default Grenade Weapon Data` → `Assets/Scripts/Player/Weapon/GrenadeWeaponData.asset`
- `Grenade Prefab` → `Assets/Prefab/Player_Grenade.prefab`
- Leave `Grenade Time To Target`, `Grenade Countdown`, `Grenade Impact Power` at defaults (1.2 / 0.2 / 500) or tune.

- [ ] **Step 4: (Optional) pre-warm the grenade + explosion pools**

In `Assets/Scripts/Managers/Object Pool/ObjectPool.cs`, add SerializeFields + `InitializeNewPool` calls in `Start`:

```csharp
[Header("To Initialize")]
[SerializeField] private GameObject weaponPickup;
[SerializeField] private GameObject ammoPickup;
[SerializeField] private GameObject grenadePrefab;      // new
[SerializeField] private GameObject explosionFx;        // new (GroundExplosion_01)

private void Start()
{
    InitializeNewPool(weaponPickup);
    InitializeNewPool(ammoPickup);
    if (grenadePrefab != null) InitializeNewPool(grenadePrefab);
    if (explosionFx != null) InitializeNewPool(explosionFx);
}
```

Assign these on the `ObjectPool` GameObject in the scene. (Lazy init still works if skipped.)

- [ ] **Step 5: Runtime verification**

Enter Play mode (single-client is fine for first pass; the project is multiplayer but a host/server test confirms networking too). With the Player selected:
1. Press **4** — the grenade model equips; the upper body plays the melee hold (`Grenade_Idle`).
2. Hold **right-click** — a dotted parabola + landing ring appears, following the mouse.
3. **Left-click** — `Grenade_Throw` anim plays; at ~55% a grenade spawns at `GunPoint`, flies the ballistic arc, and on landing plays `GroundExplosion_01` + deals AoE damage to nearby enemies/objects.
4. Throw 3 times — count depletes to 0; a 4th left-click does nothing (no grenade).
5. Press **1** — re-equips the rifle, grenade unequips, parabola hidden.

Expected: All of the above works. Report any NREs/exceptions verbatim.

- [ ] **Step 6: Multiplayer verification (host + client)**

Run a host + a second client (use the project's existing multiplayer test setup / FishNet runner). On the host (owner):
- Throw a grenade. The host sees it immediately (predicted). The client sees the grenade fly (via `RpcThrowGrenade`) and the explosion FX. Damage applies on the server (host) only — verify a test enemy's health drops. Friendly-fire respects `GameManager.instance.friendlyFire`.

Expected: No double grenade/FX on the owner. Non-owner sees one grenade + one explosion. Damage is server-authoritative.

- [ ] **Step 7: Commit**

```bash
git add Assets/Prefab/Player.prefab Assets/Prefab/Player.prefab.meta "Assets/Scripts/Managers/Object Pool/ObjectPool.cs"
git commit -m "feat(grenade): wire Grenade_Model + Player_GrenadeAimVisual + grenade fields on Player prefab; pre-warm pools

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Self-Review Notes

**Spec coverage:**
- §1 Requirements (press 4, melee hold reuse, right-click parabola, left-click throw, upper-body throw, explosion VFX, limited count, FishNet) → Tasks 1,3,4,5,6,7,8. ✓
- §2 Data & enums → Task 1. ✓
- §2 Visual model (Grenade_Model, WeaponModel, UpperBody-only throw) → Tasks 3, 8. ✓
- §2 Animator (layer 5, Grenade_Idle reuses melee idle, Grenade_Throw, ThrowGrenade trigger, anim event) → Task 3. ✓
- §2 Input (reuse EquipSlot4 + Fire + GetMouseButton(1)) → Tasks 6 (no new actions). ✓
- §2 Throw flow (Shoot branch, PerformGrenadeThrow, anim-event spawn) → Tasks 4, 6. ✓
- §2 Animation events (ThrowGrenadeTrigger → SpawnGrenade) → Task 4. ✓
- §2 Parabola (Player_GrenadeAimVisual, dotted + landing marker, shared CalculateLaunchVelocity) → Tasks 2, 5. ✓
- §2 Projectile (reuse Enemy_Grenade, GroundExplosion_01 prefab, dealDamage visual-only) → Tasks 2, 7. ✓
- §2 Networking (owner predict + CmdThrowGrenade + RpcThrowGrenade ExcludeOwner, server-authoritative damage) → Task 6. ✓
- §2 ObjectPool wiring → Task 8. ✓
- §3 Scope boundaries (no resupply, not droppable, minimal IK) → Tasks 1 (reserve=0), 6 (DropWeapon guard), 8 (rig like melee via SwitchOnCurrentWeaponModel melee branch — note: the existing melee branch keys off `WeaponType.Melee`; see note below). ✓ with note.

**Note / known gap (rig weight for grenade):** `Player_WeaponVisuals.SwitchOnCurrentWeaponModel` line 133 only zeroes the rig for `WeaponType.Melee`. A grenade would keep the rig at full weight, which may cause the left-hand IK to target a non-existent hold point. Mitigation: either (a) extend that melee branch to also cover `WeaponType.Grenade`, or (b) ensure `Grenade_Model.holdPoint` is configured so `AttachLeftHand` lands the left hand reasonably. This is a polish item; add a follow-up if the left hand looks wrong in Task 8 step 5. Not adding a task now to keep scope minimal, but flagging it here so the implementer checks it.

**Placeholder scan:** No TBD/TODO/"implement later". Tunable values are concrete numbers. ✓
**Type consistency:** `SpawnGrenade()` signature consistent across Task 4 (stub) and Task 6 (real). `PlayGrenadeThrowAnimation` consistent. `ThrowGrenadeTrigger` consistent. `CalculateLaunchVelocity(start, target, timeToTarget)` consistent across Task 2 + Task 5. ✓
