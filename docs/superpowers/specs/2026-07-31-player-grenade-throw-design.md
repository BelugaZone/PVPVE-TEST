# Player Grenade Throw — Design Spec

**Date:** 2026-07-31
**Status:** Approved (pending spec review)
**Approach:** A — Grenade as a 4th weapon slot (`WeaponType.Grenade`), reusing the existing weapon/equip/fire/visual pipeline.

## 1. Goal & Requirements

Add a player grenade-throwing ability:

- Press **4** to take out the grenade (equips weapon slot 3).
- Holding the grenade reuses the **melee weapon's hold animation** (`Melee_Idle` clip) on the upper body.
- **Right-click hold** to aim → a visual parabola appears (dotted trajectory + landing marker).
- **Left-click** to throw (consumes 1 grenade). Throw uses `Assets/Animations/Player/Grenade Throw.fbx`, **upper-body only** (via `UpperBody.mask`).
- Grenade flies a ballistic arc to the aim point, explodes with AoE damage + `AddExplosionForce`, and plays the `GroundExplosion_01` explosion VFX.
- **Limited count** (default 3), no resupply in this iteration.
- **Full FishNet networking** — owner predicts locally, server authoritative for damage, RPC-replicated visuals (mirrors the bullet `CmdShoot`/`RpcShoot` and melee `CmdMeleeAttack`/`RpcMeleeAttack` patterns).

### Assets

- Grenade model: `Assets/Models/grenade/grenade/Grenade_LOW.fbx` + `Grenade_Texture.mat`.
- Explosion VFX: `Assets/SineVFX/TopDownEffects/CompleteEffects/From_v2/GroundExplosion_01/GroundExplosion_01.prefab`.
- Throw anim: `Assets/Animations/Player/Grenade Throw.fbx` (currently `clipAnimations: []` — a clip must be defined).
- UpperBody mask: `Assets/Animations/Avatar Masks/UpperBody.mask` (guid `8e55b3dbd2cfff24d8411cf9932517dd`).

## 2. Architecture (Approach A — slot reuse)

The grenade is a normal `Weapon` of a new `WeaponType.Grenade`. 1 "bullet" = 1 grenade. This reuses equip, fire-dispatch, weapon-model switching, ammo gating, and fire-rate cooldown with zero special-casing in most of the pipeline.

### Data & enums

- `WeaponType` enum (`Assets/Scripts/Player/Weapon/Weapon.cs`): add `Grenade`.
- `HoldType` enum (`Assets/Scripts/Player/Weapon/WeaponModel.cs`): add `GrenadeHold = 5` (maps to Animator layer index 5).
- New `Weapon_Data` SO asset `GrenadeWeaponData`:
  - `weaponType = Grenade`, `bulletDamage = <explosion damage>`, `bulletsInMagazine = 3`, `magazineCapacity = 3`, `totalReserveAmmo = 0` (no reload), `fireRate = <throw cooldown, e.g. 1.0>`, `gunDistance = <explosion radius>`, `cameraDistance = <same as rifle default>`, `equipmentSpeed = <equip anim speed>`.
- `Weapon.HaveEnoughBullets()` already returns `bulletsInMagazine > 0` for non-melee types — grenade works via default path. `CanReload()` returns false (reserve = 0). **No Weapon.cs logic change required** for ammo gating; only the enum addition.

### Visual model

- New child `Grenade_Model` under `weaponHolder` on the Player prefab, with a `WeaponModel` component:
  - `weaponType = Grenade`, `holdType = GrenadeHold(5)`, `equipAnimationType = Back` (equip-from-back, consistent with a grenade pull-out; adjustable).
  - Mesh: `Grenade_LOW.fbx`, material `Grenade_Texture.mat`.
  - `gunPoint`: Transform at the throwing release point (right hand).
- `Player_WeaponVisuals.SwitchOnCurrentWeaponModel()` already keys off `CurrentWeaponModel()` and `SwitchAnimationLayer((int)holdType)` — with `GrenadeHold=5` and a layer 5 present, the swap works without code changes (the loop `for i in 1..layerCount` handles it).
- For the rig: treat like melee initially — rig weight 0, no left-hand IK attach (left hand free). Can be refined later. (Mirrors the melee branch in `SwitchOnCurrentWeaponModel`.)

### Animator (new layer + state machine)

Add a 6th layer to `Assets/Animations/Animator Controllers/Player.controller`:

- **Layer 5: "Grenade Weapon Layer"** — mask = `UpperBody.mask`, `SyncedLayerIndex = -1`, default weight 0.
- State machine:
  - `Grenade_Idle` — motion = the melee hold clip (reuse the same clip referenced by `Melee_Idle`). This satisfies "持有手雷动画复用近战武器的持有动画".
  - `Grenade_Throw` — motion = `GrenadeThrow` clip extracted from `Grenade Throw.fbx` (upper-body only, guaranteed by the layer mask).
  - `Grenade_Idle --[ThrowGrenade trigger]--> Grenade_Throw --[exitTime 0.9]--> Grenade_Idle`.
- New Animator parameter: `ThrowGrenade` (Trigger).
- Animation event on `GrenadeThrow` clip at the release frame (~50–60% of clip length): function `ThrowGrenadeTrigger` (mirrors how `BeginMeleeAttackCheck` is wired on the melee clip).
- An Editor tool `PlayerGrenadeSetupTool.cs` (modeled on `PlayerRollSetupTool.cs`) automates: extract/rename the clip from the fbx, add the `ThrowGrenade` parameter, create layer 5 + states + transitions, and attach the animation event via `AnimationUtility.SetAnimationEvents`.

### Input

- **Equip (key 4):** already wired — `EquipSlot4` (key 4) → `EquipWeapon(3)`. Set `maxSlots = 4` (default) and fill slot 3 with a `new Weapon(GrenadeWeaponData)` in `EquipStartingWeapon()`.
- **Aim (right-click hold):** reuse existing `player.aim.isAimingPrecisly` (`Input.GetMouseButton(1)`).
- **Throw (left-click):** reuse existing `Fire` action → `isShooting` → `Shoot()`. A new grenade branch in `Shoot()` requires `isAimingPrecisly` true (must be aiming to throw).

### Throw flow (`Player_WeaponController`)

In `Shoot()`, add a branch (mirrors the existing melee branch):

```csharp
if (currentWeapon.weaponType == WeaponType.Grenade) { PerformGrenadeThrow(); return; }
```

`PerformGrenadeThrow()`:
- Gates: `WeaponReady()`, `currentWeapon.CanShoot()` (count > 0 + fire-rate cooldown), `player.aim.CanAimPrecisly()` (right-click held). If any fail, return.
- `currentWeapon.bulletsInMagazine--` (consume 1).
- `player.weaponVisuals.PlayGrenadeThrowAnimation()` — sets `ThrowGrenade` trigger, temporarily zeroes other upper-body layer weights, restores via coroutine (mirrors `PlayMeleeAnimation`).
- Actual grenade spawn happens on the **animation event** (`ThrowGrenadeTrigger` → `player.weapon.SpawnGrenade()`) so the projectile leaves the hand at the release frame.

`SpawnGrenade()` (owner-side):
- Compute `spawnPos = GunPoint().position`, `aimPoint = player.aim.Aim().position` (or `GetMouseHitInfo().point`).
- Owner-local predicted spawn: `ObjectPool.instance.GetObject(grenadePrefab, GunPoint())` → `Enemy_Grenade.SetupGrenade(whatIsAlly, aimPoint, timeToTarget, countdown, impactPower, grenadeDamage)` — but with a **visual-only mode** (no damage) for the owner's predicted copy.
- Call `CmdThrowGrenade(spawnPos, aimPoint)` to have the server spawn the authoritative, damaging grenade + replicate visuals to others.

### Animation events (`Player_AnimationEvents`)

Add `public void ThrowGrenadeTrigger()` → `player.weapon.SpawnGrenade()`. (Mirrors `BeginMeleeAttackCheck` → `EnableMeleeAttackCheck`.)

### Parabola visualization (new `Player_GrenadeAimVisual.cs`)

- `MonoBehaviour` on the Player, **owner-only** (disabled on non-owners).
- Active when: `currentWeapon.weaponType == Grenade` && `player.aim.isAimingPrecisly` (right-click held) && `weaponReady`.
- Computes the launch velocity via the shared `Enemy_Grenade.CalculateLaunchVelocity(start, aimPoint, timeToTarget)` (see below), samples N points along the ballistic arc (forward-integrate gravity), and renders:
  - A **dotted trajectory** — a row of small dot sprites/quads along the arc (pooled or a single `LineRenderer` with `TextureMode = Dashed` + per-segment points).
  - A **landing marker** — a flat ring sprite at the predicted landing point (where arc Y returns to terrain height).
- Clamps `aimPoint` to a max range (derived from fixed `timeToTarget` + gravity, or an explicit `maxThrowDistance`).
- Disables when aim released, weapon switched, or grenade unequipped.

### Projectile — reuse `Enemy_Grenade`

`Assets/Scripts/Enemy/Enemy_Grenade.cs` is reusable as-is:
- `SetupGrenade(LayerMask allyLayerMask, Vector3 target, float timeToTarget, float countdown, float impactPower, int grenadeDamage)`.
- `Explode()` does `OverlapSphere` → dedup by root → `IDamagable.TakeDamage` → `AddExplosionForce` → pooled `explosionFx` → `ReturnObject`.
- `IsTargetValid` honors `GameManager.instance.friendlyFire` + `allyLayerMask` — player passes `whatIsAlly` so teammates/owner are protected when friendly fire is off.
- `explosionFx` and `impactRadius` are `[SerializeField]` on the component → configured on the prefab.

**Grenade prefab** (`grenadePrefab`): a pooled prefab with `Rigidbody` + `Enemy_Grenade` + `Grenade_LOW` mesh child. Configure `explosionFx = GroundExplosion_01`, `impactRadius = <explosion radius>`.

**Refactor (one line):** make `CalculateLaunchVelocity` reusable so the aim preview and the throw share one trajectory function:

```csharp
// Was: private Vector3 CalculateLaunchVelocity(Vector3 target, float timeToTarget) using transform.position
// Becomes:
public static Vector3 CalculateLaunchVelocity(Vector3 start, Vector3 target, float timeToTarget)
```

Call sites updated: `SetupGrenade` calls `CalculateLaunchVelocity(transform.position, target, timeToTarget)`; `Player_GrenadeAimVisual` calls the same static method with the gun point as start.

**Visual-only mode for owner prediction & non-owner replicas:** add an optional `bool dealDamage = true` parameter to `SetupGrenade`. When `false`, `Explode()` runs `PlayExplosionFx()` (so the visual grenade still plays its own local explosion VFX on each client) but **skips** `OverlapSphere`/`TakeDamage`/`AddExplosionForce` — i.e. pure visual. Default `true` keeps the enemy's existing call site and the server's authoritative grenade unchanged.

### Networking (FishNet) — mirrors bullet & melee RPC triplets

One RPC total (`RpcThrowGrenade`); damage is server-authoritative; visuals are per-client.

- **Owner (local predict):** throw animation + parabola + a local **visual-only** predicted grenade (`SetupGrenade(..., dealDamage: false)`) for immediate feedback, then `CmdThrowGrenade(spawnPos, aimPoint)`.
- **Server (`[ServerRpc] CmdThrowGrenade`):** spawn the authoritative pooled grenade at `spawnPos`, `SetupGrenade(whatIsAlly, aimPoint, timeToTarget, countdown, impactPower, grenadeDamage, dealDamage: true)` — server runs the timer and does authoritative AoE damage + `AddExplosionForce` via `Enemy_Grenade.Explode`. Server then calls `[ObserversRpc(ExcludeOwner = true)] RpcThrowGrenade(spawnPos, aimPoint, timeToTarget)`.
- **Non-owner clients (`RpcThrowGrenade`):** spawn a **visual-only** grenade (`dealDamage: false`) with the same `SetupGrenade` args → it flies the identical arc and plays its own local explosion VFX on `Explode`.
- **Owner sees:** its own predicted visual grenade fly + explode (local FX). No `RpcThrowGrenade` is received (ExcludeOwner), so no double grenade/FX.
- **Damage authority:** only the server's grenade (`dealDamage: true`) applies `TakeDamage`. The owner's predicted and non-owners' replicas are visual-only, so AoE damage is server-authoritative — consistent with the existing bullet (`CmdShoot`/`RpcShoot`) and melee (`CmdMeleeAttack`/`RpcMeleeAttack`) precedents. Friendly-fire respects `Enemy_Grenade.IsTargetValid` + `GameManager.instance.friendlyFire` + `whatIsAlly`.

### ObjectPool wiring

- `grenadePrefab` (with `Enemy_Grenade` + `GroundExplosion_01` as `explosionFx`) — lazy-pooled by `GetObject` on first use; optionally pre-warm in `ObjectPool.Start` (add `grenadePrefab` + `explosionFx` SerializeFields + `InitializeNewPool` calls).
- `GroundExplosion_01` is already poolable (it's a pooled VFX via `ObjectPool.instance.GetObject(explosionFx, transform)` in `Enemy_Grenade.PlayExplosionFx`).

## 3. Scope boundaries

- Grenade is a **fixed starting slot** (slot 3) — not pickable, not droppable (`DropWeapon` already blocks melee; extend to block `Grenade` too).
- No resupply / no ammo pickups for grenades in this iteration (count 3, consumed, 0 = can't throw).
- No grenade-specific UI for the count in this iteration (existing ammo display may or may not show it; UI is out of scope unless trivial).
- Left-hand IK on the grenade is intentionally minimal (rig weight 0, like melee); polish later.

## 4. Implementation steps (aligned with "分步执行")

1. **Data layer:** add `WeaponType.Grenade` + `HoldType.GrenadeHold`; create `GrenadeWeaponData` SO.
2. **Animator (Editor tool):** `PlayerGrenadeSetupTool.cs` — extract `GrenadeThrow` clip, add `ThrowGrenade` parameter, build layer 5 + `Grenade_Idle`/`Grenade_Throw` states + transitions, attach the `ThrowGrenadeTrigger` animation event. Run it once.
3. **Visual model:** build `Grenade_Model` child + `WeaponModel` config on the Player prefab; assign mesh/material; set `gunPoint`.
4. **Throw logic:** `Player_WeaponController.PerformGrenadeThrow` + `SpawnGrenade`; `Player_AnimationEvents.ThrowGrenadeTrigger`; `Player_WeaponVisuals.PlayGrenadeThrowAnimation`.
5. **Parabola:** `Player_GrenadeAimVisual.cs` + the `CalculateLaunchVelocity` static refactor.
6. **Projectile & pooling:** build the grenade prefab (mesh + `Enemy_Grenade` + `GroundExplosion_01`); add `bool dealDamage = true` param + visual-only branch to `Enemy_Grenade.SetupGrenade`/`Explode`; wire pool pre-warm (optional).
7. **Networking:** `CmdThrowGrenade` + `RpcThrowGrenade` (ExcludeOwner) pair.
8. **Wiring & defaults:** `maxSlots = 4`, fill slot 3 in `EquipStartingWeapon`, block `DropWeapon` for `Grenade`, Editor-field assignment, compile + fix.

## 5. Risks & open values

- **Throw animation event placement:** the release frame is approximate; `PlayerGrenadeSetupTool` sets it at ~55% of clip length — tunable after first playtest.
- **`timeToTarget`, `impactPower`, `grenadeDamage`, `impactRadius`, `maxThrowDistance`, default count (3), throw cooldown:** filled with sensible defaults during implementation; all exposed as SerializeFields for tuning.
- **Synced-layer behavior:** layers 2/3 (Shotgun/Rifle) are synced to Common (layer 1). The new layer 5 is **not** synced (like Melee layer 4) — independent state machine. Confirmed consistent with the melee precedent.
- **Double-grenade/double-FX on owner:** prevented by `ExcludeOwner = true` on `RpcThrowGrenade` — the owner only ever sees its own predicted visual grenade. (Owner's predicted grenade and the server's authoritative grenade share the same `aimPoint` + `timeToTarget`, so their positions stay close; minor drift is acceptable for v1.)
