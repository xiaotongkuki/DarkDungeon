# 项目说明与开发规范（AGENTS.md）

> 供 AI 编码助手与人工开发者阅读，统一本项目的技术上下文与代码规范。
> 约定优先级：**本文件 > 全局用户配置**。

---

## 一、项目概述

| 项目 | 说明 |
| --- | --- |
| 项目名 | `My project` |
| 类型 | **2D 俯视角动作游戏**，像素风 |
| 引擎 | **团结引擎（Tuanjie）1.10.3**，基于 **Unity 2022.3.62t15** |
| 编辑器平台 | Windows Editor |
| 美术风格 | 16×16 像素素材（0x72 DungeonTileset II、Ninja Adventure、P_P_FREE_RPG_TILESET 等） |
| 输入方案 | **新版 Input System**（`com.unity.inputsystem`） |

### 已实现的功能模块

- **开始界面** → 点击「开始游戏」跳转 `Game` 场景
- **输入与移动**：新 Input System、加减速平滑移动、输入缓冲、阻尼相机跟随
- **敌人**：视野外加权随机生成、空间网格最近查询、直线追踪 + 停止环
- **战斗**：周期索敌开火、子弹命中、`IDamageable`、接触伤害、玩家无敌帧与死亡
- **数据驱动**：数值全在 `Assets/Data/`，加怪/加武器零代码（见「十一」）
- **事件总线**：静态发布/订阅；生产者只广播事实，消费方 `OnEnable` 订阅、`OnDisable` 解绑
- **计分与 HUD / Game Over**：`EnemyKilled` → `ScoreChanged` → 显示「击杀: N」；`PlayerDied` → 结算面板 + 重开
- **经验与升级**：击杀掉宝石（值 = `EnemyData.xpValue`）→ 磁吸拾取 → `XpTracker` 折算等级 → `LevelUp` 触发三选一（`timeScale = 0` 暂停 + `PlayerStatModifiers` 生效并广播 `StatsChanged` + 恢复）
- **对象池**：子弹/怪物/宝石走 `ComponentPool<T>`；**无池的对象回退 `Destroy`**，裸对象行为不变（见「十二」）

---

## 二、环境与技术栈

关键依赖（`Packages/manifest.json`）：`com.unity.inputsystem` 1.14.4-t4（团结定制版）、`com.unity.feature.2d` 2.0.1（Physics2D / Tilemap / SpriteShape）、`com.unity.textmeshpro` 3.0.9、`com.unity.test-framework` 1.1.33、`com.coplaydev.unity-mcp` 10.2.0（AI 通过 MCP 操作 Unity）。

- **未安装 Cinemachine**：相机跟随用项目自写的 `CameraFollow2D`，不要引入该依赖（除非明确要求）
- `ProjectSettings.asset` 的 `activeInputHandler: 2`（新旧输入系统均启用）；**新代码一律用 Input System，禁止 `Input.GetKey` 等旧 API**

---

## 三、目录结构约定

```
Assets/
├─ Art/                  美术资源（第三方素材包 + 程序化占位图）
├─ Data/                 ScriptableObject 资产：Enemies/ Weapons/ Player/ Levels/ Upgrades/
├─ Prefabs/              Enemy、Enemy_Fast、Bullet、Gem
├─ Scenes/               StartMenu.scene（index 0）、Game.unity（index 1）
├─ Settings/InputSystem/ PlayerControls.inputactions（唯一按键来源）
│                        PlayerControls.cs（导入器生成，**禁止手改**）
├─ Screenshots/          截图输出（工具自动生成）
└─ Scripts/              按模块分目录：
      StartMenuController.cs（根）
      Camera/        CameraFollow2D
      Combat/        IDamageable、IntervalTimer、Projectile
      Common/        GameLayers、EventBus、ComponentPool、IPooledComponent
      Data/          EnemyData、WeaponData、PlayerData、LevelCurveData、
                     UpgradeData、UpgradePoolData、UpgradeStat
      Diagnostics/   PerfStressHarness / Environment / Report（见「十三」）
      Enemy/         Enemy、EnemyAI、EnemyContactDamage、EnemyManager、
                     EnemySpawner、SpatialGrid、ChaseSteering、SpawnArea
      Game/          ScoreTracker、XpTracker
      Input/         InputBuffer、PlayerInputReader、PlayerControls（生成物）
      Pickup/        Gem、GemSpawner
      Player/        PlayerMovement、PlayerAutoAttack、PlayerHealth、
                     PlayerLocator、PlayerStatModifiers
      UI/            HudController、GameOverController、UpgradeChoiceController
      Tests/         EditMode/ 与 PlayMode/
```

**新增脚本**：按功能模块建子目录（`Player/`、`Enemy/`、`UI/`…），不要堆在 `Scripts` 根；跨模块通用工具放 `Common/`。

---

## 四、代码规范（强制）

### 4.1 函数说明注释（必须）

**每个函数/方法前都要有说明注释**（含 `private`/`internal`/`protected`、Unity 生命周期回调、含非平凡逻辑的属性访问器；lambda 仅逻辑复杂时注释），推荐 XML 文档注释：

```csharp
/// <summary>根据输入意图与当前速度选择加速度，反向时触发急停。</summary>
/// <param name="intent">归一化后的移动意图向量</param>
/// <returns>本物理步的速度变化率（单位/秒）</returns>
private float ChooseRate(Vector2 intent) { ... }
```

**要求**：说明「做什么」与「为什么」，不复述代码（禁止 `// 设置速度`）；参数/返回值/边界条件要体现；语言中英皆可但**同一文件内一致**（现有代码用英文，建议沿用）。

### 4.2 命名与格式

| 对象 | 规则 | 示例 |
| --- | --- | --- |
| 类 / 结构体 / 枚举 | PascalCase | `PlayerController` |
| 方法 / 属性 | PascalCase | `ReadDirection()` |
| 私有字段 | `_camelCase` | `_rb`、`_buffer` |
| 局部变量 / 参数 | camelCase | `targetSpeed` |
| 常量 | PascalCase | `MaxBufferCapacity` |
| 序列化字段 | `_camelCase` + `[SerializeField] private` | `[SerializeField] private float maxSpeed = 8f;` |

- **禁止 `public` 字段**：需要 Inspector 暴露一律用 `[SerializeField] private`
- 文件名 = 类名（一个文件一个 MonoBehaviour）；**不要创建命名空间**（项目为 `Assembly-CSharp` 根命名空间）
- 缩进 **4 空格**（不用 Tab）；每行 ≤ **120 字符**；文件末尾留一个空行；大括号独占一行（Allman）；用 `// ---- Section ----` 或 `[Header("...")]` 组织长类
- **禁止魔法数字**：可调数值必须是 `[SerializeField]` 字段或 `const`，注释写明单位与含义（如 `// units per second`）
- 单文件建议 ≤ 300 行；**禁止在 `Update` 中** `GetComponent`/`Find`/字符串拼接/`new` 分配（在 `Awake` 缓存）
- 事件解绑、资源释放必须在 `OnDisable`/`OnDestroy` 中对称处理

---

## 五、输入系统规范

1. **按键一律定义在 `PlayerControls.inputactions`**，禁止硬编码按键字符串（如 `Keyboard.current.dKey`）；通过自动生成的包装类访问（`_controls.Gameplay.Move`）。`PlayerControls.cs` **禁止手改**，改 `.inputactions` 后需重新导入
2. **采样与驱动分离**：`Update` 采样 → 写入 `InputBuffer`；`FixedUpdate` 消费意图 → 驱动 `Rigidbody2D.velocity`。目的是消除 `Update`（帧率）与 `FixedUpdate`（50Hz）的节拍差
3. 移动端触摸绑定已预留（`TouchMove`），虚拟摇杆 UI 待后续 Sprint

---

## 六、移动与物理规范

- 玩家移动用 **`Rigidbody2D`（Dynamic）+ 代码驱动 `velocity`**，不用 `transform.position` 直接位移
- 加减速用 `Vector2.MoveTowards` / `SmoothDamp`，**参数必须可调**（`maxSpeed`/`acceleration`/`deceleration`/`turnBoost`，现位于 `PlayerData`）
- `Rigidbody2D` 配置：`gravityScale = 0`、`freezeRotation = true`、`collisionDetectionMode = Continuous`、`interpolation = Interpolate`
- 2D 物理一律用 `Physics2D` / `Collider2D` / `Rigidbody2D`，**禁止混用 3D 物理组件**
- 输入缓冲窗口默认 **120ms**（`_bufferWindow`），改需说明理由
- 敌人是 **Kinematic + isTrigger**：只触发 `OnTriggerStay2D` 而不推挤玩家；`useFullKinematicContacts = 0`

---

## 七、场景规范

- 新场景必须含 **Camera** 与必要光照/背景；2D 相机用**正交投影**（`orthographicSize = 5`）、`z = -10`
- 新场景必须登记进 **Build Settings**，索引顺序：`StartMenu` = 0、`Game` = 1

### 7.1 UI 约定（uGUI）

- 与 `StartMenu.scene` 一致：`Canvas` + `CanvasScaler` + `GraphicRaycaster` + `EventSystem`（`StandaloneInputModule`）
- 文本用 legacy **`UnityEngine.UI.Text`** + 内置 **`LegacyRuntime`** 字体（实测中文正常）；不要混用 UI Toolkit
- 画布用 **Screen Space - Overlay**（`m_RenderMode = 0`）、`CanvasScaler` 用 Scale With Screen Size / 1920×1080
- 运行时若用 `UnityEngine.UI`，**必须在 `MyProject.Runtime.asmdef` 的 references 加上 `UnityEngine.UI`**（asmdef 不自动引用包程序集；测试程序集同理）
- 面板类 UI 在 `Awake` 里隐藏，而不是在场景里禁用：面板在编辑器中始终可见可调，且控制器仍能收到 `OnEnable`
- **UI 布对齐只由人工完成（强制）**：AI 只负责建好结构骨架（节点层级、组件、代码接线、逻辑功能），**不要自行设置/调整 RectTransform 的具体对齐**（anchor/position/sizeDelta、字号、配色等视觉数值）。用户已在编辑器中手调过的 UI 参数视为只读；修 bug 时**禁止**「整树重建预制体 / 删实例重放」这类覆盖式操作，只能用 `SerializedObject` 精确定性改动用户点名的字段。若确需调整某个具体的 UI 数值，先列出「改哪个节点的哪个字段、从什么值到什么值」获得用户确认

---

## 八、AI 助手工作流（重要）

本项目通过 **MCP for Unity** 让 AI 直接操作编辑器。

### 8.1 验证流程

1. **改脚本后必须确认编译真的通过**——不能只读 Console（可能被清空或滞后），要**解析程序集类型**：`Type.GetType("X, MyProject.Runtime")`。编译失败会锁死编辑器标志位（见 8.2）
2. 功能验证优先在 **Play Mode** 中做，读真实运行数据（velocity、位置、相机偏移）而非肉眼判断；截图用 `manage_camera(action="screenshot")`

### 8.2 已知陷阱

| 陷阱 | 现象 | 规避 |
| --- | --- | --- |
| **编译失败锁死 play-mode 标志位** | `isCompiling` 与 `isPlayingOrWillChangePlaymode` 恒为 true，域重载卡住：测试报 `did not start within timeout`、MCP `sequence` 不推进、`stop` 回 "Already stopped"。**编辑器在空转**（实测 12 秒仅 0.44s CPU） | ① **先修编译错误**（根因）② `EditorApplication.ExitPlaymode()` 清标志（安全检查会误判为 `EditorApplication.Exit`，需 `safety_checks=false`）③ `run_tests(clear_stuck=true)` 清孤儿作业 ④ 重新 `run_tests` 踢通管线。用两次 CPU 采样区分「真编译」与「空转」 |
| **编辑器失焦冻结 PlayerLoop** | 游戏不 tick，采样恒为 0 | 先设 `Application.runInBackground = true`（运行时不持久化）；手动测试时保持 Game 视图有焦点 |
| **池化放大「字段被当一次性初始化」** | `Instantiate` 下正常；**复用后 `Gem._magnetRadius` 4.5 → 6.0 → 7.5 无限增长** | 池化前逐个检查实例字段：**赋值而非累加**；per-use 状态在 `OnEnable`/arming 里复位。回归测试要**对同一实例调用两次 arming** |
| **`ObjectPool.collectionCheck` 只查重复归还** | 归还未发出的实例被静默接受 → 同一对象可能发给两个调用方 | `ComponentPool<T>` 自维护 `_handedOut` 校验并抛异常；别假设内置检查覆盖外来实例 |
| **`Time.timeScale` 不随场景重载重置** | 升级置 0 后重开场景仍停在 0，游戏卡死 | 恢复路径上必须还原（`GameOverController.Restart()` 已防御性置 1）；测试 TearDown 也要还原 |
| **`SerializedObject` 写入不落盘资产** | 内存读到新值，`.asset` 仍是旧值（如 `fileID: 0`） | 反射直写字段 + `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets()`，并**用文本核对磁盘**，不要只信内存回读 |
| **项目设置类资产写入被回写** | 往 `TagManager.asset` 写层名后 30~50 秒被编辑器内存副本覆盖 | 只写文件**不算成功**：必须核对运行时读数（`LayerMask.NameToLayer`）；不一致就请用户手动改 |
| **MCP 建出的 UI 对象两个坑** | ① `Canvas` 的 `m_RenderMode` 默认 2（World Space），UI 完全看不到，`GameObject.Find` 也找不到 ② 子对象 `localScale` 被写成 `1/画布缩放`（实测 3.27），字号巨大、标题被裁 | ① 建完把 `m_RenderMode` 改成 `0`（Overlay）再保存，核对 `Canvas.rect` 是否等于参考分辨率 ② 把**子对象**重置为 `(1,1,1)`（画布自身不动，由 `CanvasScaler` 维护），核对 `GetWorldCorners` 是否落在屏内 |
| **测试夹具实例会被复用** | runner 对同一测试类的所有 `[Test]` 复用同一 fixture 实例，字段不在测试间重置，计数漏到下一个测试 | `[SetUp]` 里显式清零所有字段；**静态状态**（如 `EventBus` 订阅）不受影响，必须在 `[TearDown]` 解绑 |
| **PlayMode 测试掩盖集成缺陷** | 自建资产使测试全绿，真实场景资产为 null 也不报错 | 数据驱动改动后**必须补真实场景冒烟验证**（生成 / 开火击杀 / 玩家受伤） |
| **Tuanjie `.meta` 用 base64 GUID** | 与场景/预制体 YAML 的十六进制 GUID 格式不同，grep 文本会误判为「未找到」 | 引用核对一律走 Unity API（`AssetDatabase`、MCP 资源查询），不要 grep 文本 |
| **`.inputactions` 的 id 必须是合法 GUID** | id 被截断时（末段只有 11 位）Actions 编辑器抛 `FormatException`、窗口无法渲染；**运行时不受影响**，很隐蔽 | 校验全部 id 格式（8-4-4-4-12）与唯一性；修好后**重新导入**生成 `PlayerControls.cs`（生成类内嵌 JSON 副本，两处必须同步） |
| **`execute_code` 的 CodeDom 限制** | 仅 C# 6、部分程序集未引用；往返 10~30 秒游戏时间，无法短窗口采样；`QueueStateEvent` 注入的键盘状态下一帧即失效 | 用完全限定名（如 `UnityEngine.UI.Text`）、避免 `using`；精细采样改用「探针 + `EditorPrefs`」跨调用传数据；输入注入需**逐帧**执行并配合 `InputSystem.Update()`；`KeyboardState` 在 `UnityEngine.InputSystem.LowLevel` |
| **场景资产操作** | 传 `Game.scene` 给 `manage_scene create` 会生成 `Game.scene/Game.unity`；移动**当前打开**的场景时报 "not a valid path" | 建完用 `MoveAsset` 修正路径并清理空目录；移动场景前先 `OpenScene` 切走 |
| **安全检查误拦** | `AssetDatabase.DeleteAsset`、`EditorApplication.ExitPlaymode` 等被 blocked pattern 拦截 | 确认操作安全后用 `safety_checks=false` |
| **贴图 tint 无法改色相** | 红色贴图（蓝通道为 0）乘品红 tint 仍是红色，两种怪看起来一样 | tint 只能缩放已有通道；换颜色必须换贴图 |

### 8.3 沟通约定

- 涉及取舍或多个方案时**先列方案与依据，等用户确认再动手**
- 无法验证的内容（读不到的截图、确认不了的渲染效果）**如实说明，不得编造**
- 报告验证结论要附**实测数据**（如每物理步速度增量），而非「看起来正常」
- **写入不生效必须主动报告**，固定五项：① 什么设置/资产（完整路径）② 期望值 ③ 我做了什么、结果如何 ④ 请用户手动做什么（具体菜单路径）⑤ 影响范围（影响运行还是仅编辑器显示）

---

## 九、提交规范

- 提交信息用英文、遵循 Conventional Commits（`feat:` / `fix:` / `refactor:` / `docs:` / `test:` / `chore:`）
- 未经用户明确要求**不要 commit / push**
- 禁止提交密钥、Token、密码；禁止提交 `Library/`、`Temp/`、`Logs/`、`UserSettings/`

---

## 十、待办与后续规划

| 模块 | 状态 | 说明 |
| --- | --- | --- |
| 开始界面 / 输入与移动 / 敌人生成与 AI / 自动攻击与战斗 | ✅ | Sprint 1 + 2-1 |
| 数据驱动 / 事件总线 / 计分 HUD / Game Over | ✅ | 资产化；静态发布订阅；Game Over 订阅 `PlayerDied` |
| 经验掉落与升级三选一 | ✅ | `GemSpawner` + `XpTracker` + `LevelCurveData` + `UpgradePoolData` |
| 对象池 | ✅ | `ComponentPool<T>` 覆盖子弹/怪物/宝石；无池回退 `Destroy`（见「十二」） |
| 场景级冒烟测试 | 🟡 | `PerfStressTests` 已加载真实 `Game.unity` 并断言接线/存活数，但 `[Explicit]` **默认不跑**；仍缺默认执行的轻量冒烟 |
| 死亡时暂停 | ⏳ | 玩家死亡后敌人仍在后台跑（面板只是遮罩）；要暂停需 `timeScale = 0` 并注意重开还原 |
| 移动端虚拟摇杆 / 离散输入预输入 | ⏳ | `TouchMove` 已预留；`InputBuffer.Enqueue/TryDequeue` 已实现但无使用者 |
| 设置按钮 / 多武器 / 数值平衡 | ⏳ | `BtnSettings` 未绑定；`WeaponData` 已就绪但当前单武器槽；站桩玩家约 5 秒接触即死（10 HP / 0.5s 无敌）待调 |
| **运行时存档序列化** | ⏳ | **完全没有**：无 `PlayerPrefs`/`JsonUtility`/文件写入，也没有需要存档的数据；做局外成长时需单独设计 |
| 输入键位重绑定 | ⏳ | 无重绑定 UI；实现后需保存键位覆盖（属存档范畴） |
| 编辑器设置 | ⏳ | 建议关闭「Enter Playmode with Reload Scene disabled」：会残留测试场景导致假象 |

---

## 十一、数据驱动（ScriptableObject）

数值不写在组件里，全在 `Assets/Data/` 资产中；**加新怪或新武器不需要改代码**。本节只涉及**配置数据**（随包发布、运行时只读）；**运行时存档尚未实现**（见「十」）。

| 数据类（`Scripts/Data/`） | 资产 | 覆盖字段 |
| --- | --- | --- |
| `EnemyData` | `Data/Enemies/*.asset` | 血量、移速、速度浮动、接触伤害、掉落经验值 |
| `WeaponData` | `Data/Weapons/*.asset` | 子弹预制体、开火间隔、索敌半径、伤害、子弹速度/寿命/命中半径 |
| `PlayerData` | `Data/Player/Player.asset` | 血量、无敌帧、移速、加减速、转向加成 |
| `LevelCurveData` | `Data/Levels/LevelCurve.asset` | 每级所需经验（末项即等级上限） |
| `UpgradeData` | `Data/Upgrades/Upgrade_*.asset` | 强化名称、描述、目标属性、数值 |
| `UpgradePoolData` | `Data/Upgrades/UpgradePool.asset` | 升级三选一候选池（需 ≥ 3 项） |

菜单：`Create → My project/Data/{Enemy|Weapon|Player|Level Curve|Upgrade|Upgrade Pool}`。

**接线规则**

- **一个预制体只填一个数据槽**：`Enemy` 持有 `EnemyData`，`EnemyAI`/`EnemyContactDamage` 通过 `Enemy.Data` 读
- `PlayerHealth` 与 `PlayerMovement` 各有一个 `PlayerData` 槽
- `EnemySpawner._spawnEntries` 是 `{预制体, 权重}` 加权表（按总和归一，非正整数）
- `EnemyAI._stopDistance` 留在组件上：它是**几何量**（必须小于双方碰撞体半径之和，否则接触伤害永不触发），与预制体强耦合
- **资产运行时只读**：随实例变化的状态（速度浮动、当前血量）只能放组件上；**升级加成也不进资产**（`PlayerStatModifiers` 保存本局累计，各组件读「资产基础值 + 加成」）
- **缺数据的行为**：`Awake` 报 `Debug.LogError` 并**禁用该组件**（不走 `OnEnable`，既不注册也不行动）；**不做静默回退默认值**——配置错误必须立刻可见

**新增一个怪物（零代码）**：复制 `Enemy.prefab` → `Create → My project/Data/Enemy` 新建资产填数值 → 拖到预制体 `Enemy` 的 `_data` 槽 → 加进 `Game.unity` 中 `Enemies` 的 `_spawnEntries` 并给权重。（可选）换贴图区分外观——注意「贴图 tint 无法改色相」，改色必须换贴图。

**测试写法**：用 `Tests/PlayMode/TestData.cs` 的 `CreateEnemyData`/`CreateWeaponData`/`CreatePlayerData` 造临时资产（`CreateInstance`，不落盘）；组件在 `Awake` 校验数据，所以必须**先建 inactive → 挂组件并赋数据 → 再 `SetActive(true)`**（对活跃对象 `AddComponent` 会立刻触发 `Awake` 并报错）；`Tests/EditMode/DataAssetTests.cs` 校验资产与预制体的接线。

---

## 十二、对象池（`ComponentPool<T>`）

子弹、怪物、宝石创建/销毁极频繁，池化把 `Instantiate`/`Destroy` 换成状态复位。

### 12.1 契约

```csharp
public interface IPooledComponent<T> where T : Component, IPooledComponent<T>
{
    ComponentPool<T> Pool { set; }   // 由池在取出时注入
}
```

契约**故意只有一条**：实例只需知道自己的池以便自归还；**状态复位放在组件自己的 `OnEnable`**（Unity 本就会调用它）。自引用约束是必需的——池的约束要求类型参数实现该接口，接口不重复这一条就无法命名一个与自身兼容的池（曾触发 CS0311 并锁死编辑器，见 8.2）。

### 12.2 `ComponentPool<T>` 的行为

包装内置 `UnityEngine.Pool.ObjectPool<T>`（`CoreModule` 自带，无需额外包），补齐 Unity 那一半：

- **懒创建**：不预分配，按峰值需求增长；`capacity` 只是栈容量提示
- **`maxSize`**：停放栈到上限后再归还就**销毁**，防无界增长
- **取出**：先注入池引用，再 `SetActive(true)`（激活触发 `OnEnable`，即复位时机）
- **归还**：`SetActive(false)` 并挂回宿主容器下，随场景/测试一起回收
- **拒绝外来实例**：自维护 `_handedOut`，归还非本池发出或重复归还**抛 `InvalidOperationException`**（内置 `collectionCheck` 只查重复归还）
- **`Clear()`**：只销毁**停放中**的实例，不影响在场的

### 12.3 接入四步与无池回退

1. 类型实现 `IPooledComponent<T>`，加 `_pool` 与 `Pool { set => _pool = value; }`
2. `Destroy(gameObject)` → `Retire()`：`if (_pool != null) { _pool.Release(this); return; } Destroy(gameObject);`
3. **逐个检查实例字段**：能累加的一律改成赋值，per-use 状态在 `OnEnable`/arming 里复位
4. 由生成方建池（懒创建 + 宿主容器 + `maxSize`）

**裸对象（测试里 `new GameObject` + `AddComponent`、编辑器里手放的）走 `Destroy` 分支，行为与池化前完全一致**——这就是现有断言 `shot == null` / `_gemObject == null` 不用改的原因。

### 12.4 池的划分与边界

- **一个池服务一个预制体**：`EnemySpawner` 每个 `_spawnEntries` 条目一个池（`SpawnEntry.PoolUnder`）；`PlayerAutoAttack` 持其武器 `ProjectilePrefab` 的池；`GemSpawner` 持宝石池
- 池**懒创建**（测试会在 `Awake` 之后才填配置，预制体也可在 Inspector 改）；**容器是宿主组件的子物体**（`Pool_<预制体名>`），`TearDown` 销毁宿主即连带回收
- **运行中换预制体不受支持**：会 `LogError` 拒绝，需要换就重开一局
- 已知边界：`_handedOut` 对**被外部 `Destroy` 的实例**留一个陈旧引用（可忽略）；`PerfStressHarness.Drain` 直接 `Destroy` 会让池的 `CountActive` 漂移（不影响正确性）；池化后**不能靠「对象是否存在」判断生死**（停放实例是 inactive，`FindObjectsOfType` 默认找不到）

---

## 十三、性能基准（`Diagnostics/`）

`Diagnostics/` 下 `PerfStressHarness`（编排：分档生成 / 预热收敛 / 变体开关 / 采样窗口）、`PerfStressEnvironment`（环境接管还原、玩家无敌、Profiler 日志、反射读写）、`PerfStressReport`（采样行 + 表格），入口是 `Tests/PlayMode/PerfStressTests.cs` 的 `[Explicit]` 测试——**默认套件不跑**，按名触发：

```
run_tests(mode="PlayMode", test_names=["PerfStressTests.Baseline_SmokeRunReportsOneTier"])
```

**两条铁律**：① 生成与预热不计入测量窗口（否则 `Instantiate` 尖峰被当成稳态成本）② 变体开关在收敛**之后**才应用（否则各变体敌人分布不同，差量就不是成本差量）。

**三个会让测量作废的坑**：vSync 开着（Ultra 档 `vSyncCount = 1`）→ 帧耗时被钳在 16.7ms，每档都是 60 FPS；帧率不设上限 + Profiler 在录制 → 几百 FPS 下 Profiler 逐帧存数据，**小内存机器会 OOM**（实测触发过），故预热限帧 60 + 采样设帧数预算（`_maxSampleFrames`）；编辑器失焦 → PlayerLoop 冻结，`window` 与 `frames × avg` 对不上（实测 25 秒的用例跑了 111 秒）。

**读数口径**：`avg/min/max/sd` 是玩家循环帧间隔，`fps = frames / 窗口实际秒数`，两者**不可混用**；`gc` 取自 `GC.GetAllocatedBytesForCurrentThread()`（不依赖 Profiler 录制）；`batches/setPass/draw` 取自 `UnityEditor.UnityStats`（**仅编辑器**）；`mono/total` 是**编辑器自身**的堆，报内存必须用 Development Build。

**基线**（编辑器内，200 只怪，`Full` 变体，20 秒窗口）：`avg=1.92ms sd=0.40ms fps=519.7 gc=0.0B/f batches=6 draw=6 active=200/200`。已知未修：`Drain` 用 `Destroy` 销毁活跃的池化敌人（见 12.4）；池化收益尚未用 churn 变体量化（`Full` 变体关掉了自动攻击，没有子弹/击杀 churn）。
