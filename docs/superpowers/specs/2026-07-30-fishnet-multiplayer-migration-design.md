# FishNet 多人联机重构设计方案

## 目的
本设计的目的是提供一个渐进式的重构策略，将现有的单机动作/射击项目转换为使用 FishNet 网络框架和状态同步机制的多人联机项目。

为了降低大规模架构变更带来的风险，整个重构过程被拆分为多个独立的、可验证的小阶段。

## 核心架构与理念
- **网络框架**: FishNet (免费、强大，且原生支持客户端预测和服务端权威)。
- **专用服务端 (Dedicated Server)**: 必须支持 Headless 模式独立运行服务端（无需渲染画面和处理本地输入）。所有后续架构设计必须严格区分仅客户端执行的逻辑（如 UI、相机、输入）与仅服务端执行的逻辑。
- **同步模型**: 状态同步 (State Synchronization)。
  - 物理碰撞、核心逻辑、AI 计算以及伤害判定均在服务端运行。
  - 坐标 (Transforms)、动画 (Animations) 和各种状态变量由服务端同步给客户端。
  - 瞬时事件（例如视觉特效、播放音效）通过 RPC 进行调用。

## 第一阶段：基础多人漫游 (客户端权威移动)
**目标**: 允许两名及以上的玩家连入同一个场景，自由移动，并看到彼此的动作和动画，而不影响现有的单机场景结构。

### 关键改造点
1. **网络环境搭建**: 新建一个专门的多人测试场景，并加入 FishNet 的 `NetworkManager`。
2. **玩家组件转换**: 
   - 将 `Player`、`Player_Movement` 和 `Player_AimController` 的继承基类从 `MonoBehaviour` 变更为 FishNet 的 `NetworkBehaviour`。
3. **输入与所有权隔离**:
   - 在所有的键盘输入处理和相机控制逻辑前加上 `if (!base.IsOwner) return;` 保护。这确保了客户端只会操控属于自己的那个化身（Avatar）。
4. **同步组件挂载**:
   - 在玩家预制体 (Prefab) 上挂载 `NetworkObject`。
   - 挂载 `NetworkTransform`，并配置为客户端权威（Client-Auth / Client-Driven），以便玩家本地流畅移动并广播坐标。
   - 挂载 `NetworkAnimator` 同步角色的跑、跳等动画状态。

## 第二阶段：武器与伤害同步 (基础 PVP)
**目标**: 实现在网络环境下射击，并准确同步玩家之间的生命值（扣血）。

### 关键改造点
1. **武器表现同步**: 开火视觉特效和换弹动作通过 `[ObserversRpc]` 或者触发 `NetworkAnimator` 的动画参数来实现全网广播。
2. **服务端权威伤害**:
   - 客户端命中目标后，通过 `[ServerRpc]` 将命中信息发给服务端。
   - 服务端进行命中合法性校验，随后在服务端调用目标 `HitBox` 与 `HealthController` 上的 `TakeDamage` 方法。
3. **状态同步 (State Sync)**: 将 `HealthController` 中的 `currentHealth` 改为 `[SyncVar]` 标签修饰。当服务端扣血时，客户端的 UI 通过 SyncVar 钩子函数自动刷新。

## 第三阶段：服务端 AI (基础 PVE)
**目标**: 引入会在所有人画面中保持一致行动逻辑的敌人。

### 关键改造点
1. **AI 隔离**: 修改 `EnemyStateMachine` 以及所有相关 AI 逻辑（如寻路、目标锁定），确保它们仅在服务端执行（`if (!base.IsServer) return;`）。
2. **敌人生成**: 敌人不能直接放置在场景中，必须由服务端调用 `InstanceFinder.ServerManager.Spawn()` 统一动态生成。
3. **敌人状态同步**: 在敌人预制体上挂载 `NetworkObject`、`NetworkTransform` (配置为 Server-Driven 服务端权威) 和 `NetworkAnimator`。

## 第四阶段：对象池与全局游戏状态
**目标**: 将项目现有的对象池接入 FishNet 的生成系统，并同步游戏全局进度。

### 关键改造点
1. **对象池改造 (Object Pool Integration)**: 将项目中现有的自定义 `ObjectPool` 逻辑进行包装，使其对接 FishNet 内置的 `DefaultObjectPool`。确保所有需要网络同步的物体（例如带有物理效果的子弹、掉落的武器）都能在全网正确 Spawn 和 Despawn。
2. **GameManager 改造**: 将 `GameManager` 转换为 `NetworkBehaviour`，利用 `[SyncVar]` 广播全图的游戏状态，例如剩余时间、比分、游戏阶段等。
