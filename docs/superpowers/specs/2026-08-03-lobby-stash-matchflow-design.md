# 大厅 / 仓库 / 对局生命周期 — 设计文档

**日期**: 2026-08-03
**分支**: feature/player-grenade-throw → 新建议分支 `feature/lobby-stash-matchflow`
**状态**: 设计已确认，待实现

## 目标

为 PVPVE 搜打撤游戏增加「大厅—房间—对局」元层，覆盖玩家从登录到对局结束返回大厅的完整循环。基于现有 FishNet 局内玩法之上改造，不重写局内战斗。

### 需求清单（用户提出）

1. 启动服务器
2. 玩家登录（主机或客机）→ 登录后见仓库、装备栏、背包栏，拖拽配装
3. 创建房间（主机或客机均可创建），其他玩家点击加入，房间最多 4 人
4. 房主点开始 → 对局开始
5. 玩家死亡可点复活，无需重启客户端
6. 客户端中途关闭后重连恢复（**本阶段延后，不实现**）
7. 对局结束返回大厅（仓库界面），局内装备与背包保留，可继续配装与建房

## 关键决策（已与用户确认）

| 决策点 | 结论 |
|---|---|
| 房间并发模型 | **单局制**：房间=集结大厅，同时只跑一局 |
| 仓库模型 | **独立跨局仓库 + 死亡掉全** |
| 场景结构 | **双场景**：Lobby + Game |
| 初始仓库 | 新玩家**预置基础配装**（默认配装表 SO） |
| 死亡复活 | **同局重生**（掉全后带默认近战） |
| 重连 | 本阶段不做 |
| 对局结束 | core 撤离 **或** 定时到；保留分数排名；返回大厅；存活者局内物品回收到仓库 |

## 现状摘要（改造起点）

- **服务器启动**：无 bootstrap，靠 FishNet demo `NetworkHudCanvas` 的 OnGUI 按钮手动起；`_autoStartType=Disabled`；无 headless 自启。
- **登录**：`UI_Login`（在 Game 场景内）→ `ClientManager.StartConnection()` → `CustomAuthenticator` 广播鉴权 → `ServerDataManager.SpawnPlayerForConnection` 直接生成进玩法世界。无大厅。
- **无房间/匹配概念**。单一共享世界；`MatchManager` 跑 180s 定时局，core 撤离或超时 → `RestartMatchRoutine` 停连接 + 毁 NetworkManager + Unity 原生重载场景。
- **库存**：Tarkov 式 4 装备槽 + 24 背包槽，服务端权威 SyncList，JSON 按 playerID 持久化，**死亡全清**。无仓库概念。
- **死亡**：尸体常驻、`RemoveOwnership`、存档全清、`isRespawn=true`；无局内重生，须断线重连。
- **单场景** `Scene_multi`（build settings 唯一）。

## 架构

### 核心选型

**长驻服务器 + FishNet SceneManager 场景切换**。连接在 大厅↔对局 之间保持不断，实现「无缝返回仓库」。

被否决备选：
- 沿用硬重载（停连接+毁NM+LoadScene）：每次回大厅要重新登录，违背诉求。
- 单场景 UI 覆盖：敌人常驻、房间集结模型冲突，已排除。

### 场景与对象布局

| 场景 | build index | 内容 | 何时加载 |
|---|---|---|---|
| **Lobby** | 0 | NetworkManager、ServerDataManager、RoomManager、登录/仓库/房间 UI | 服务器启动即加载；对局结束切回 |
| **Game**（原 `Scene_multi` 拆出纯玩法） | 1 | 关卡几何、敌人、ExtractionZone、MatchManager、SpawnPoint | 房主点开始加载；结束切出 |

- **NetworkManager** 从「场景内 FishNet demo 预制体」改为 Lobby 场景内 + `DontDestroyOnLoad`，跨场景常驻、不再被销毁。移除 demo `NetworkHudCanvas`。
- **ServerDataManager** `DontDestroyOnLoad` 化，持有 `playerPrefab`、仓库 JSON、按场景决定生成位置。
- **MatchManager** 留 Game 场景，仅对局期间存活。
- **RoomManager**（新增，DontDestroyOnLoad）：房间状态机 + FishNet SceneManager 调用。

### 连接生命周期

```
[服务器启动] → 加载 Lobby → ServerManager.StartConnection()
玩家登录(StartConnection + 鉴权)
  → ServerDataManager 按当前场景生成玩家体:
     - Lobby 场景 → 大厅玩家体(仓库交互)
     - Game 场景  → 对局玩家体(玩法,对局中插入=默认近战)
[房主点开始] → RoomManager.SceneManager.LoadScene("Game", Replace)
  → 成员 loadout 从 stash MOVE 到局内
  → 销毁大厅玩家体,Game 场景生成对局玩家体
  → MatchManager.OnStartServer 开局
[对局中] 死亡→掉全为尸体→点复活→同局重生点(默认近战)
[对局结束: core撤离/定时到] → 结算分数 → RoomManager.LoadScene("Lobby", Replace)
  → 存活者局内物品回收到 stash → 生成大厅玩家体 → 回仓库界面(连接未断)
```

## 数据模型

### 三层数据

| 层 | 作用 | 何时存在 | 存哪 |
|---|---|---|---|
| **仓库 (Stash)** | 跨局持久存储，玩家全部家当 | 大厅可见，对局中冻结 | `PlayerSaveData.stash`（JSON） |
| **配装 (Loadout)** | 开战前从仓库拖出的「本局要带的」 | 大厅配装阶段 | `PlayerSaveData.loadout*`（内存/暂存） |
| **局内 (In-raid)** | 对局中实际背包+装备 | 仅对局期间 | 现有 `PlayerInventory.netBackpack/netEquipment` |

### 搜打撤循环（MOVE 语义，非 COPY）

```
[大厅] 仓库 ──拖拽提交──> 配装      (stash 移出 → loadout)
[开战] 配装 ──实例化──> 局内背包/装备 (loadout 清空 → 对局 SyncList)
[对局中] 拾取战利品 → 局内背包增长
[死亡] 局内物品全掉落为可搜刮尸体 → 局内清空 → 重生只带默认近战
[对局结束·存活] 局内 ──回收──> 仓库 (MOVE 回 stash)
[对局结束·已死重生者] 仅带回重生后搜集到的物品
```

开战 commit、撤离 deposit，物品守恒；死亡永久丢失局内物品。

### PlayerSaveData 扩展

```csharp
class PlayerSaveData {
    string playerID;
    Vector3 position; Quaternion rotation;   // 仅对局中用
    int health;
    bool isRespawn;
    List<InventoryItemData> stash;           // 新增：跨局仓库
    List<InventoryItemData> loadoutEquip;    // 新增：配装装备(4)
    List<InventoryItemData> loadoutBackpack; // 新增：配装背包(24)
    // 旧 backpack/equipment 保留为「局内」，语义不变
}
```

- `stash` 持久化 JSON；`loadout*` 大厅暂存（**内存，不入 JSON**——重连延后，无需抗掉线）。`PlayerSaveData` 类上有字段但不参与序列化。
- 新玩家首次登录：`stash` 用**默认配装表 SO** 填充。
- 仓库复用现有 `NetItem`/`InventoryItem`/`ItemRegistry`，不另造；`PlayerStash`（大厅玩家体）复用 `PlayerInventory`（对局玩家体）的 UI 拖拽模式。

### 持久化改造

- `ServerDataManager.SavePlayerState` 额外写 `stash`。
- **死亡不再全清存档**：`ClearPlayerState` 改为只清局内 `backpack/equipment`，`stash` 不动。死亡损失体现在「局内物品已掉落」。
- 撤离/对局结束时「回收局内→仓库」由 RoomManager 在切回大厅前调用一次。

## 房间状态机与对局生命周期

### RoomManager（新增 DontDestroyOnLoad NetworkBehaviour）

```
Empty ──创建房间──> Gathering ──房主开始──> InMatch ──core撤离/定时到──> MatchEnding ──> Gathering
```

| 状态 | 含义 | 行为 |
|---|---|---|
| `Empty` | 无人建房 | 大厅面板显「创建房间」 |
| `Gathering` | 房主已开房，等人加入 | 成员≤4；房主见「开始」，他人见「等待」 |
| `InMatch` | 对局进行中 | 新登录玩家直接生成进 Game 场景（默认近战，不提交 loadout） |
| `MatchEnding` | 结算分数排名 | 短暂态，播完排名切大厅 |

### 房间数据

```csharp
class RoomManager : NetworkBehaviour {
    SyncVar<RoomState> state;
    SyncVar<int> ownerClientId;
    SyncList<int> memberClientIds;   // 最多4
    SyncVar<int> maxMembers = 4;
}
```

- `CmdCreateRoom`：任意客户端 → owner=该连接、state=Gathering、自己入 members。
- `CmdJoinRoom`：校验 state==Gathering && members<4 → 加入。
- `CmdStartMatch`：仅 owner，校验 Gathering → 场景切换。
- 房主断线 → owner 转让给最早加入的 member；members 空 → state=Empty。
- 对局中全员断线 → 自动结束切大厅（防卡死）。

### 对局启动时序

```
CmdStartMatch (owner)
[服务器] state=InMatch；每个 member 的 loadout 从 stash MOVE 到局内
[服务器] SceneManager.LoadScene("Game", Replace)
         → 服务器加载 Game；通知 member 客户端加载（连接不断）
[客户端 OnClientLoadedScene] ServerDataManager 生成对局玩家体，注入局内数据+玩法
[Game] MatchManager.OnStartServer 开局
```

### 对局结束时序

```
MatchManager.EndMatch (core撤离 或 定时0)
  → ScoreManager.CalculateScores (保留)
  → RpcShowMatchEndUI (UI_MatchEndScreen 保留)
[短暂展示] RoomManager 接管:
  → 存活 member 局内背包/装备 MOVE 回 stash
  → SceneManager.LoadScene("Lobby", Replace)
  → 销毁对局玩家体/敌人/战利品
  → 各客户端加载 Lobby；生成大厅玩家体，绑定仓库
  → state=Gathering（保留房间，可继续开下一局）
```

### 对局中插入玩家（保留现有行为，简化重构）

- ServerDataManager 生成逻辑统一为「按当前场景生成」：Lobby 场景→大厅玩家体；Game 场景→对局玩家体。
- 对局中登录的玩家 = 跟重生同处理：默认近战、不提交 loadout（没赶上 CmdStartMatch）。可进场搜刮、打架、抢 core。
- loadout 提交只对 Gathering 时在场 members 生效。
- 无「旁观/等待」态，无多场景。

### 分工

- `ServerDataManager`：连接/断线、生成玩家体、JSON 读写（扩展 stash）。
- `RoomManager`：房间状态、场景切换、开/结对局、loadout↔局内搬运。
- 生成时机由 RoomManager 状态 + 当前场景驱动。

## 死亡 / 重生

### 重生流程

```
[死亡] Player_Health.Die (服务器)
  1. 局内背包/装备掉落为可搜刮尸体 (复用 DropAllItems + Enemy_LootContainer)
  2. 清空局内 SyncList
  3. 不再 ClearPlayerState 全清 —— stash 不动，只清局内
  4. 不再 RemoveOwnership —— 玩家体复用，保留所有权
  5. isDead=true → 客户端进死亡 UI 态
[已死亡·客户端] UI_DeathScreen 显「复活」按钮 → 玩家点 → CmdRespawn
[服务器] CmdRespawn
  1. 校验 isDead==true
  2. 选 SpawnPoint
  3. 挪玩家体到重生点，Player_Health.ServerInitialize(满血)
  4. 给默认近战 (EquipFromInventory slot2)
  5. isDead=false → 客户端恢复操作
```

### 关键改造

- 不再 `RemoveOwnership`：保留所有权，客户端能接收 isDead 并点复活。尸体改用局内临时 loot 容器（复用 Enemy_LootContainer），不依赖所有权转移。
- 不再 `ClearPlayerState` 全清：只清局内（掉落时已清 SyncList），stash 不动。
- `isDead` 复用现有 SyncVar；`OnDeathChanged` 从「启 ragdoll+显尸体」改为「显死亡 UI + 解锁鼠标」。
- Game 场景放 `SpawnPoint` 列表，重生时取一个。
- 对局结束时已死未复活者：直接结算随场景切回；已复活在场者：局内搜集物品回收。两者走同一切回路径。

## UI

### 复用

仓库/背包拖拽全套现成：`UI_InventoryPanel`/`UI_InventorySlot`/`UI_EquipmentSlot`/`UI_DragManager`/`UI_SlotHover`/`UI_Tooltip`/`UI_PlaceholderIcons`。仓库面板 = 再开一个 `UI_InventoryPanel` 实例绑 stash SyncList。结算 `UI_MatchEndScreen`、计时器、撤离 HUD、血条、弹药环保留。

### 新增/改造

| UI | 场景 | 作用 |
|---|---|---|
| `UI_StartScreen` | Lobby | 替换 demo OnGUI HUD；启动服务器/主机登录/客机登录(+IP) |
| `UI_Login` | Lobby | 输入 ID（从 Game 场迁来） |
| `UI_StashPanel` | Lobby | 仓库+配装栏+配装背包，拖拽 |
| `UI_RoomPanel` | Lobby | 创建/加入/成员/开始 |
| `UI_DeathScreen` | Game | 死亡复活 |

### 启动入口流

```
[客户端启动] → Lobby → UI_StartScreen
  ├─ 启动服务器 → ServerManager.StartConnection()
  ├─ 主机登录  → StartConnection(server+client) → 鉴权 → 仓库
  └─ 客机登录  → 输入IP → StartConnection(client) → 鉴权 → 仓库
[鉴权成功] UI_Login 隐藏 → UI_StashPanel + UI_RoomPanel 显示
```

移除 demo `NetworkHudCanvas`，用 `UI_StartScreen` 替代，解决「无启动入口」「无 IP 输入」缺口。

### 仓库/配装布局（Lobby）

左：仓库网格（绑 stash）。右：配装装备栏+配装背包（绑 loadout）。拖拽左→右=提交配装（`CmdMoveStashToLoadout`）；右→左=放回。底部房间面板。

### 房间面板

- 无房：「创建房间」。
- Gathering：房主见「开始」+成员列表；非房主成员见「等待」+「退出」；未加入见「加入」。
- InMatch：隐藏。
- 满 4 人「加入」置灰。

## 分阶段实施

| 阶段 | 内容 | 验收 |
|---|---|---|
| **0 场景地基** | 建 Lobby 场景；NetworkManager+ServerDataManager 迁入+DontDestroyOnLoad；Scene_multi 拆纯 Game；移除 demo HUD；新增 UI_StartScreen；生成逻辑按场景分 | Lobby 起服、登录、生成大厅玩家体 |
| **1 仓库数据层** | PlayerSaveData 扩 stash+loadout；默认配装表 SO；大厅玩家体+PlayerStash；UI_StashPanel；SavePlayerState 写 stash；死亡不全清 | 登录见仓库、拖拽配装、断线仓库在 |
| **2 房间状态机** | RoomManager+CmdCreate/Join/Leave/Start；UI_RoomPanel；房主转让 | 多端建房、加入(≤4)、转让、退出 |
| **3 场景切换** | CmdStartMatch→FishNet SceneManager.LoadGame；loadout→局内 MOVE；删 RestartMatchRoutine 改调 RoomManager；结束回收局内→stash→LoadLobby；对局中插入=默认近战 | 开始→进对局→撤离/超时→结算→回大厅仓库更新 |
| **4 死亡重生** | Die 改造(不掉权/不清stash/loot容器)；CmdRespawn；UI_DeathScreen；SpawnPoint 列表 | 死亡掉全→点复活→同局重生带近战→继续打 |
| **5 收尾** | 结算排名验证；全员断线自结束；默认配装表调优；边界测试 | 完整循环跑通 |

### 依赖

```
0 → 1 → 2 → 3 → 4 → 5
```

阶段 1/2 可部分并行（耦合低），建议串行验证。阶段 3 为最大风险点（FishNet SceneManager 跨场景 + 物品搬运）。

## 风险

- **FishNet DontDestroyOnLoad 场景对象迁移**：NetworkManager 从场景内改常驻易踩配置坑，需编辑器验证。
- **FishNet SceneManager 跨场景切换**：当前代码刻意绕过它（硬重载），改回正规流程需验证客户端场景同步、对象归属。
- **物品搬运守恒**：stash↔loadout↔局内 三次 MOVE 任一出错会刷/丢物品，需在 RoomManager 集中处理并加守恒断言。
- **尸体 loot 容器**：不再 RemoveOwnership 后，尸体如何常驻可搜刮需验证（改用场景内 NetworkObject loot 容器）。

## 不做（本阶段范围外）

- 重连（客户端关闭后恢复原局原状态）。
- 多房间并发（单局制）。
- 友伤/计分规则调整（保留现有）。
- 语音/聊天。
