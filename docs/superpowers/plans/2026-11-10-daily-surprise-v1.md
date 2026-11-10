# 喜从天降（Daily Surprise）v1 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 每个游戏日睡醒触发一个随机天降事件（好运/平淡/霉运 45 选 1），原生事件卡弹窗 + 配图台词 + 奖惩发放，5 预设难度 + 自定义难度。

**Architecture:** 纯逻辑层（难度/掷骰/目录/存取，可单测）与 il2cpp 层（表注入/发放/触发补丁）隔离；事件全量以代码常量定义，运行时注入 `Config_Event`/`Config_RewardSet`/`Config_Buff`/`Config_Effect` 自定义行，`TryForceTriggerEvent` 拉起原生弹窗；耐久/永久属性等 RewardSet 覆盖不了的效果走代码双通道。

**Tech Stack:** C# / net6.0 / BepInEx 6 (IL2CPP) / HarmonyX / Il2CppInterop / System.Text.Json / xUnit（或 MSTest，随 EndlessGuard.Tests 惯例）

**Spec:** `E:\Game\Mod\survivallog-mods\DailySurprise\docs\superpowers\specs\2026-11-10-daily-surprise-design.md`（已冻结，计划以 spec 为准，执行者两份都读）

## Global Constraints

- 构建只用 `E:\Game\Mod\survivallog-mods\sdk\dotnet\dotnet.exe`，判退出码，禁管道截断输出（禁 `| tail`）
- csproj 引用游戏侧 DLL（`<Private>false</Private>`），路径镜像 `EndlessGuard\SurvivalLog.EndlessGuard\SurvivalLog.EndlessGuard.csproj`
- 自注入 ID 段：Event 99200+，RewardSet 99250+，Buff 99280+，Effect 99290+（已核对无占用）
- 所有注入 buff 一律 `Overlay=0`（时长叠加不加层）
- cfg 暴露面固定 8 项 + 1 个开发用调试项：`Difficulty`(1~6,默认3)、`CustomGoodPercent`(45)、`CustomPlainPercent`(25)、`CustomBadPercent`(30)、`CustomRewardMultiplier`(1.0)、`CustomLossMultiplier`(1.0)、`ShowPlainPopup`(true)、`AllowPermanentNegative`(true)、`DebugForceEventId`(0，开发用)。不暴露单事件概率
- 属性官方名：饱腹/心态/精力/健康/生命；比例字段 0.5=50%；`BuffInterval`/`BuffDuring` 单位=秒（3600=1h，−1=永久）
- 触发点：`GameSaveManager.CaptureDaySnapshot(int day)` postfix；**禁挂 Harmony 到编译器生成的 async 状态机 MoveNext**；换任何挂钩点须先反汇编论证并同步 `suite\tools\gamekb\moddeps.json`
- 仅灾后生效（普通档+无尽档），灾前序章完全静默；永久类按存档槽独立记账
- 数据目录 `BepInEx\plugins-data\DailySurprise\`（按存档槽分文件）；不碰游戏存档结构
- 插件自证：写 `plugins-data\DailySurprise\trace\` 下自证文件，不依赖 LogOutput.log
- 必须接 `WelcomeTip.Install(Log)`（ModShared 镜像，唯一改动源在 `suite\ModShared\`，本仓只放镜像副本）
- git：禁 `add -A`，外科手术式 add 明确文件；主分支 main
- `.ps1` 一律 ASCII-only（PowerShell 5.1）；`gamekb\`、`thirdparty\`、`发版物料\` 永不入库
- 版本号只有作者能定：csproj `<Version>` 的最终值在 Task 13 由作者确认，此前占位 `0.1.0`
- 显示名：喜从天降；GUID：`com.local.survivallog.dailysurprise`；图片命名 `ds_<小写事件号>.png`（如 `ds_g1.png`）
- 游戏单实例红线：作者玩时不等不杀；杀游戏后确认进程死透再碰文件

## Review Focus

（spec 未显式覆盖、最可能咬人的五类输入；每条已锚定到归属任务的测试步骤）

1. **自定义档三概率和≠100**（如 40/25/30）→ 必须回退档 3 并写警告日志，不得崩溃或归一化。→ Task 3 测试 `Resolve_CustomSumInvalid_FallsBackToTier3`
2. **多存档槽串数据**（槽 A 玩 3 天换槽 B）→ 保底计数/昨日事件/永久上限不得带入槽 B。→ Task 5 测试 `SaveLoad_SlotIsolation`
3. **同日重复结算**（读档回昨天再睡过结算点）→ 同一 (slot, day) 只触发一次。→ Task 7 逻辑测试 + 实机验证幂等
4. **背包满发奖失败**（`TryAddItemToBag` 拒收）→ 不得静默吞奖，入 `PendingRewards` 次日优先补发并气泡提示。→ Task 8 测试 `DecideQueue_BagFull_QueuesPending` + 实机验证
5. **霉运层整层无资格**（开局空屋抽到重度）→ 同层重掷→降层，轻度层（B1~B6 全以自身为对象）恒有资格，不得死循环/落空白挨。→ Task 3 测试 `Roll_BadTierAllIneligible_DowngradesToLight`

---

### Task 1: 工程脚手架与插件骨架

**Files:**
- Create: `SurvivalLog.DailySurprise/SurvivalLog.DailySurprise.csproj`、`SurvivalLog.DailySurprise/Game/DailySurprisePlugin.cs`、`SurvivalLog.DailySurprise/Properties/AssemblyInfo.cs`（若 EndlessGuard 有则镜像）
- Create: `SurvivalLog.DailySurprise.Tests/SurvivalLog.DailySurprise.Tests.csproj`、`SurvivalLog.DailySurprise.Tests/SmokeTest.cs`
- Create: `ModShared/BagTableRw.cs`、`ModShared/WelcomeTip.cs`（从 `suite\ModShared\` 复制镜像，不改动）
- Create: `.gitignore`（bin/obj/release/plugins-data）
- Create: `SurvivalLog.DailySurprise/DESIGN.md`（暂为指向 spec 的占位+首验记录区）、`CHANGELOG.md`（空壳）

**Interfaces:**
- Consumes: `suite\ModShared\WelcomeTip.cs` 的 `WelcomeTip.Install(BepInEx.Logging.ManualLogSource)`
- Produces: 程序集 `SurvivalLog.DailySurprise.dll`；常量 `Ds.Guid="com.local.survivallog.dailysurprise"`、`Ds.Name="喜从天降"`；静态 `DsTrace.Write(string category, string message)`（写 `plugins-data\DailySurprise\trace\`，UTF-8，追加）

- [ ] **Step 1: 建 csproj**（镜像 EndlessGuard 的引用路径与 `<Private>false</Private>`；`<Version>0.1.0</Version>` 占位；EmbeddedResource 预留 `Art/**`）。Tests 工程镜像 EndlessGuard.Tests。
- [ ] **Step 2: 写插件骨架**：`[BepInPlugin(Ds.Guid, Ds.Name, "0.1.0")]`，`Load()` 里写自证文件 `trace\_DONE.txt`（内容=GUID+时间戳）并 `WelcomeTip.Install(Log)`。`DsTrace` 同文件实现。
- [ ] **Step 3: Tests 冒烟**：`SmokeTest` 断言 `Ds.Guid` 非空且 `Ds.Name=="喜从天降"`。
- [ ] **Step 4: 构建并跑测试**：`E:\Game\Mod\survivallog-mods\sdk\dotnet\dotnet.exe build`（退出码 0）→ `dotnet.exe test`（退出码 0，1 passed）。
- [ ] **Step 5: 部署实机**：拷 dll+cfg 到 `E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\SurvivalLog.DailySurprise\`，启动游戏进档，确认 `trace\_DONE.txt` 出现、主角头顶出现署名气泡。
- [ ] **Step 6: Commit**：`git add` 上述明确文件清单，`git commit -m "feat: 工程脚手架与插件骨架（自证+WelcomeTip）"`

### Task 2: 首验 spike——单事件注入与强制触发

> 目的：§7.7 风险项 1/3 提前实证，决定后续任务走法。结论回填 DESIGN.md。

**Files:**
- Create: `SurvivalLog.DailySurprise/Core/EventDef.cs`（数据类型，见 Interfaces）
- Create: `SurvivalLog.DailySurprise/Game/TableInjector.cs`（本任务只注入 1 行）
- Modify: `SurvivalLog.DailySurprise/Game/DailySurprisePlugin.cs`（cfg 加 `DebugForceEventId`；ticker 轮询 `ConfigManager` 就绪后 `EnsureInjected()`，若 DebugForceEventId>0 且今日未强制过 → `TryForceTriggerEvent`）
- Modify: `SurvivalLog.DailySurprise/DESIGN.md`（记录首验结论）
- Test: `SurvivalLog.DailySurprise.Tests/TableInjectorSpecTests.cs`

**Interfaces:**
- Consumes: `BagTableRw.Cm()`（ConfigManager 单例获取）
- Produces: `EventDef { int Id; string Key; LuckKind Kind; int Rarity; string Title; string Dialogue; string Icon; RewardSpec Reward; Eligibility Elig; int Weight; }`；`enum LuckKind { Plain, Good, Bad }`；`enum Eligibility { None, HasAppliance, HasFurnitureAtLeast1, HasFreshFood, HasParts10 }`；`RewardSpec { List<ItemGain> Items; List<int> Buffs; List<AttrChange> Attrs; SpecialOp Op; double OpParam; List<Choice> Choices; bool PermBuffByTier; }`（`PermBuffByTier=true` 表示 Buffs 不固定、按档位/倍率在运行时选变体，见 Task 8 `VariantMath`）、`ItemGain{int ItemId;int Count}`、`AttrChange{int AttrId;double Delta}`、`enum SpecialOp { None, DurabilityPercentRandom, DurabilityPercentAll, DurabilityPercentRange, AttrPermRandom, RotFood, StealParts }`、`Choice{string Label; RewardSpec Reward}`；`TableInjector.EnsureInjected() -> InjectionReport { int Events, RewardSets, BuffRows, EffectRows; }`

- [ ] **Step 1: 定义测试事件数据**：`EventDef` 单例 `SpikeEvent`：Id=99200，Key="spike"，台词"这是一次测试：天上掉下来一箱压缩饼干。"，Reward.Items=[{2101,4}]，Icon 暂用任一原生相对路径（如 EndlessGuard 落盘过的 `../../Res/Icon/eg_arrow_tower.png`）。测试断言字段装配正确。
- [ ] **Step 2: 实现最小 `TableInjector.EnsureInjected()`**：向 `Config_Event` 字典写 1 行（含 `EventDes_Local` 裸中文、PopUpType/KeepTime 抄一条原生事件的值、`StartRewardSetId` 指向注入的 RewardSet 99250），向 `Config_RewardSet` 写 1 行（4×压缩饼干）。哨兵=行 99200 是否已在字典（`ConfigManager.Initialize` 重建后由 ticker 复查重注入）。
- [ ] **Step 3: 构建部署，实机验证 A（自动发放）**：cfg `DebugForceEventId=99250` 无关——直接用事件行强制：进档后 ticker 触发 `DynamicEventManager.TryForceTriggerEvent(99200)`。预期：弹原生事件卡、台词正确、图标显示、**关闭弹窗后背包出现 4×压缩饼干**（若 StartRewardSet 不自动发放→记录，RewardSet 走代码路径，写进 DESIGN.md 结论）。
- [ ] **Step 4: 实机验证 B（选项字段类型）**：临时给该行加 `ChoseName_Local=["拿走","算了"]` 两选项，确认 UI_DynamicEventChose 弹出且可点（IL2CPP 数组字段写入方式记入 DESIGN.md；失败→选项事件降级方案启用：所有【选项】事件改为直接效果，记入 DESIGN.md）。
- [ ] **Step 5: 回填 DESIGN.md 首验记录**（发放链路结论/选项可行性/Icon 相对路径结论/字段写入模板代码段）。
- [ ] **Step 6: Commit**：`git commit -m "feat: 首验spike——单事件注入与强制触发链路实证"`

### Task 3: DifficultyTable + LuckEngine（纯逻辑 TDD）

**Files:**
- Create: `SurvivalLog.DailySurprise/Core/DifficultyTable.cs`、`SurvivalLog.DailySurprise/Core/LuckEngine.cs`
- Test: `SurvivalLog.DailySurprise.Tests/DifficultyTableTests.cs`、`SurvivalLog.DailySurprise.Tests/LuckEngineTests.cs`

**Interfaces:**
- Consumes: 无（纯逻辑，禁引用 BepInEx/il2cpp）
- Produces: `CustomSettings { int GoodPercent, PlainPercent, BadPercent; double RewardMultiplier, LossMultiplier; }`；`DifficultyParams { int Tier; double Good, Plain, Bad; double[] GoodRarity; double[] BadRarity; double RewardMult, LossMult; int PityDays; int PermNegativeCap; }`；`DifficultyTable.Resolve(int tier, CustomSettings custom, Action<string> warn) -> DifficultyParams`（tier=6 且三概率和≠100 → warn+返回档 3 参数；数值=spec §5.2/§5.3 表，逐格照抄）；`RollState { int LastTriggeredDay; int BadStreak; int PermNegativeUsed; int YesterdayEventId; int DisasterCount; }`；`LuckEngine.Roll(DifficultyParams p, RollState s, Func<int,bool> eligible, Func<int,bool> isPermNegative, Func<int,int> substitute, Random rand) -> RollOutcome { LuckKind Kind; int RarityIndex; int EventId; bool PityUsed; bool Substituted; }`（语义=spec §5.1/§5.4：保底→两段掷骰→层内等权选（N 层用 N1=35/N2=20/N3=N4=N5=15）→防连抽重掷一次→资格过滤同层重掷→霉运整层无资格降一层（重度→中度→轻度，轻度恒有资格）→永久负面达上限/开关关闭时用 substitute 换替补事件）

- [ ] **Step 1: 写 DifficultyTable 失败测试**（先跑失败）：档 1~5 全字段断言（如档1 `Good==0.75 && RewardMult==2.0 && PityDays==2 && PermNegativeCap==0 && GoodRarity` 四值和=100）；`Resolve_CustomSumInvalid_FallsBackToTier3`（40/25/30→档3 值+warn 被调）；`Resolve_CustomValid_UsesCustomAxes`（50/25/25→Good==0.50，其余跟档3）；`Resolve_BadZero_PureLottery`（50/50/0 合法）。
- [ ] **Step 2: 跑测试确认失败**（类型不存在）。
- [ ] **Step 3: 实现 `DifficultyTable`**（数据硬编码 spec 表）。
- [ ] **Step 4: 跑测试通过。**
- [ ] **Step 5: 写 LuckEngine 失败测试**：种子化 `Random(42)` 断言确定性结果；`Roll_PityForceGood`（BadStreak=PityDays→Kind==Good 且 PityUsed）；`Roll_AntiRepeat_RerollsOnce`（昨日事件被避开，除非层内仅 1 个事件）；`Roll_BadTierAllIneligible_DowngradesToLight`（eligible 对重/中度全 false→Kind==Bad 且 RarityIndex==轻度）；`Roll_PermNegativeCapped_UsesSubstitute`（PermNegativeUsed==Cap→EventId==substitute(原)）；`Roll_NoPity_Tier5`（档5 PityDays=0 不强制）。
- [ ] **Step 6: 跑测试失败→实现 `LuckEngine`→跑通过。**
- [ ] **Step 7: Commit**：`git commit -m "feat: 难度表与掷骰引擎（保底/降层/防连抽/上限替补）"`

### Task 4: EventCatalog 与 BuffBlueprints 数据

**Files:**
- Create: `SurvivalLog.DailySurprise/Core/EventCatalog.cs`、`SurvivalLog.DailySurprise/Core/BuffBlueprints.cs`
- Test: `SurvivalLog.DailySurprise.Tests/EventCatalogTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `EventDef/RewardSpec` 类型
- Produces: `EventCatalog.Events -> IReadOnlyList<EventDef>`（45 条，Id/台词/奖励=spec §8 全表，N 层权重 35/20/15/15/15，其余等权；E2/L3 的 Reward 置 `PermBuffByTier=true`；H1/D1/D3 的替补映射表 `EventCatalog.Substitutes: int→int`（H1→H4、D1→D2、D3→H2）；`EventCatalog.PermNegativeIds = {H1,D1,D3}`）；`BuffBlueprints.All -> IReadOnlyList<BuffBlueprint>`（§6.2 五条克隆 + §6.3 九条自注入（命运眷顾Ⅰ~Ⅴ、行运低潮Ⅱ~Ⅴ，变体 Id 记为 `BuffBlueprints.FortuneIds[1..5]` 与 `SlumpIds[2..5]`）：`BuffBlueprint { int NewId; int CloneSourceId; string Name; string Des; Dictionary<string,object> FieldOverrides; }`，NewId 从 99280 顺排）

- [ ] **Step 1: 写校验测试（失败）**：`Events_CountIs45`；`Events_IdsUniqueAndInSegment`（99200~99249）；`Events_TierCounts`（Plain 5/Good 22/Bad 18，各稀有度层数 N5 G8 R6 E5 L3 B6 M5 H4 D3）；`Events_AllPlainWeightsSum100`；`Events_ReferencedItemsExist`（Items 里的 ItemId ∈ 一份内嵌的合法 ID 集合——从 gamekb Config_Item 摘录用到的那 30 余个 ID 常量）；`Substitutes_PointToExistingNonPerm`；`Blueprints_IdsUniqueAndCloneSourcesValid`。
- [ ] **Step 2: 跑失败→按 spec §8/§6 逐条填数据（45+14 条）→跑通过。**
- [ ] **Step 3: Commit**：`git commit -m "feat: 45事件目录与14条buff蓝图数据"`

### Task 5: StateStore（TDD）

**Files:**
- Create: `SurvivalLog.DailySurprise/Core/StateStore.cs`
- Test: `SurvivalLog.DailySurprise.Tests/StateStoreTests.cs`

**Interfaces:**
- Consumes: Task 3 `RollState`
- Produces: `StateStore`（扩展 `RollState`：加 `List<PendingReward> PendingRewards`，`PendingReward{ RewardSpec Spec; string Reason; }`）；`StateStore.Load(string slotDir) -> RollState`（无文件→默认值；JSON 损坏→默认值+调用方可检测的 `HadCorruption` 标记）；`StateStore.Save(RollState, string slotDir)`（tmp+`File.Move(overwrite:true)` 原子写，UTF-8 JSON）

- [ ] **Step 1: 写失败测试**：`SaveLoad_RoundTrip`（BadStreak/PermNegativeUsed/PendingRewards 往返）；`SaveLoad_SlotIsolation`（槽 A 存数据后读槽 B→默认值）；`Load_CorruptFile_ReturnsDefault`（写乱码 JSON）；`Save_IsAtomicAndOverwrites`（连存两次读回第二次）。
- [ ] **Step 2: 跑失败→实现→跑通过**（临时目录 `Path.Combine(Path.GetTempPath(), …)` 做槽）。
- [ ] **Step 3: Commit**：`git commit -m "feat: 分槽存取与原子写的状态存储"`

### Task 6: TableInjector 全量注入

**Files:**
- Modify: `SurvivalLog.DailySurprise/Game/TableInjector.cs`（从 1 行扩到全量）
- Test: `SurvivalLog.DailySurprise.Tests/InjectorMappingTests.cs`（纯逻辑映射部分：EventDef→表行字段字典）

**Interfaces:**
- Consumes: `EventCatalog`、`BuffBlueprints`、Task 2 首验记录的字段写入模板
- Produces: `TableInjector.EnsureInjected() -> InjectionReport`（45 Event 行、RewardSet 行（普通奖励 1 行/事件，含选项的每选项 1 行）、14 Buff 行、对应 Effect 行；哨兵复查幂等；`ConfigManager.Initialize` 重建窗口由既有 ticker 轮询重入）；`InjectorMapping.EventRow(EventDef) -> Dictionary<string, object>`（纯函数，可单测）

- [ ] **Step 1: 写映射测试（失败）**：G1 行含 `StartRewardSetId=99250+序号`、台词原文、Icon=`../../Res/Icon/ds_g1.png`；L1 生成 3 个选项 RewardSet；H1 的 buff 引用=克隆行 NewId（99280 段）而非源 24112。
- [ ] **Step 2: 实现 `InjectorMapping` 纯函数→测试通过。**
- [ ] **Step 3: 扩展 `EnsureInjected` 写四表**（事件/RewardSet/buff/effect；克隆行=读源行字段→套 FieldOverrides→新 Id 写入）。
- [ ] **Step 4: 构建+部署实机**：进档后自证 `trace\inject.txt` 报告四表行数（45/N/14/M），重进档一次验证哨兵重注入幂等（无重复行、无异常）。
- [ ] **Step 5: Commit**：`git commit -m "feat: 四表全量注入（45事件+奖励集+克隆/自注入buff）"`

### Task 7: 每日触发链（补丁+模式门控+幂等+兜底）

**Files:**
- Create: `SurvivalLog.DailySurprise/Game/Trigger/Patch_CaptureDaySnapshot.cs`、`SurvivalLog.DailySurprise/Game/Trigger/DayWatcher.cs`、`SurvivalLog.DailySurprise/Game/DailyFlow.cs`
- Modify: `SurvivalLog.DailySurprise/Game/DailySurprisePlugin.cs`（挂补丁+ticker）
- Test: `SurvivalLog.DailySurprise.Tests/DailyFlowLogicTests.cs`（把 OnNewDay 的判定逻辑抽纯函数 `DailyFlow.ShouldTrigger(RollState, int day, bool postDisaster) -> bool`）

**Interfaces:**
- Consumes: `LuckEngine`、`StateStore`、`DifficultyTable`、cfg（`DsConfig` 静态包装：各 cfg 键的当前值）
- Produces: `DailyFlow.OnNewDay(int day)`（门控：非灾后直接 return；`ShouldTrigger`：day==LastTriggeredDay→false；触发后 Save；**平淡档且 `ShowPlainPopup=false` 时不拉事件卡**，改为 `Unit_ShowHeadTalk(台词)` 头顶气泡，记账照常）；模式判定 `DailyFlow.IsPostDisaster() -> bool`（基于 `GameTimeManager.WorldStateType`，无尽档判定按实机核对结果固化）

- [ ] **Step 1: 写失败测试**：`ShouldTrigger_SameDayTwice_False`；`ShouldTrigger_NewDay_True`；`ShouldTrigger_PreDisaster_False`；`PlainPopup_Disabled_SkipsCardButRecords`（纯函数 `DailyFlow.Presentation(LuckKind, bool showPlain) -> enum { EventCard, HeadTalk }`）。
- [ ] **Step 2: 实现纯函数→通过；实现 Harmony postfix（TargetMethod 反射取 `CaptureDaySnapshot(int)`，Postfix 调 `DailyFlow.OnNewDay`，全程 try/catch 隔离）与 `DayWatcher` 兜底轮询（SaveMgr 模式）。**
- [ ] **Step 3: 实机验证**：灾后档连睡 3 天，`trace\daily.txt` 每天 1 行触发记录（day 号+事件 Id）；读档回昨天重睡同一天→无第二行；**灾前序章新开档睡 1 天→零触发记录**；无尽档睡 1 天→正常触发（WorldStateType 映射结论回填 DESIGN.md）。
- [ ] **Step 4: Commit**：`git commit -m "feat: 每日触发链（结算补丁+灾后门控+同日幂等+兜底轮询）"`

### Task 8: RewardExecutor——声明式发放与倍率

**Files:**
- Create: `SurvivalLog.DailySurprise/Game/Exec/RewardExecutor.cs`
- Modify: `SurvivalLog.DailySurprise/Game/DailyFlow.cs`（触发后调用 Grant；DailyFlow 启动时先补发 PendingRewards）
- Test: `SurvivalLog.DailySurprise.Tests/RewardScaleTests.cs`（纯逻辑：数量取整）

**Interfaces:**
- Consumes: `RewardSpec`、`ItemManager.TryAddItemToBag`、buff 注入行 Id、Task 2 首验的发放结论（声明式 vs 代码）
- Produces: `RewardExecutor.Grant(RewardSpec spec, double rewardMult) -> GrantResult { int GrantedItems; List<PendingReward> Queued; }`（数量=round(count×mult) 下限 1；背包拒收→Queued 进 PendingRewards；buff 经 `BuffComponent.AddBuff(主角, NewId)` 或 RewardSet 声明式，按首验结论择一）；`RewardMath.ScaleCount(int base, double mult) -> int`、`RewardMath.DecideQueue(bool bagAccepted, RewardSpec spec, string reason) -> PendingReward?`、`RewardMath.PickPermBuff(int tier, double rewardMult) -> int`（预设档 1~5→`FortuneIds[1..5]` 同号变体；仅 tier==6 按倍率 ≥1.75/1.25/0.875/0.625/否则→Ⅰ~Ⅴ；对应行运低潮 `PickSlump(double lossMult) -> int`，仅自定义档使用）（全纯函数）

- [ ] **Step 1: 写失败测试**：`ScaleCount_RoundsAndFloorsAt1`（4×2.0=8；4×0.5=2；1×0.5=1（下限）；3×0.3=1）；`DecideQueue_BagFull_QueuesPending`（bagAccepted=false→返回 PendingReward，true→null）；`PickPermBuff_TierN_MapsSameIndex`、`PickPermBuff_CustomByMultiplier`（6,2.0→Ⅰ；6,0.5→Ⅴ）。
- [ ] **Step 2: 实现 `RewardMath`→通过；实现 `Grant`（物品批量发放、拒收入队、buff 施加）。**
- [ ] **Step 3: 实机验证**：`DebugForceEventId` 分别强制 G1（档3 基准×4）与档1 cfg（×2→8 个）各一次，核对背包数量；强制 N4 核对**负面属性声明式生效**（心态/精力 −2，不生效则该 AttrChange 走代码路径并记 DESIGN.md）；塞满背包强制 G5→次日进档补发+`Unit_ShowHeadTalk` 气泡"昨天塞不下的东西送到了"。
- [ ] **Step 4: Commit**：`git commit -m "feat: 奖励发放（倍率取整+背包满补发队列）"`

### Task 9: PunishmentOps——耐久/永久属性/腐坏/偷窃

**Files:**
- Create: `SurvivalLog.DailySurprise/Game/Exec/PunishmentOps.cs`
- Test: （纯逻辑部分并入 `RewardScaleTests`：损失量缩放 `ScaleLoss(double base, double lossMult)`）

**Interfaces:**
- Consumes: `SpecialOp`、`RewardMath.PickSlump`（D1 在 `AllowPermanentNegative=false` 且自定义档时的行运低潮变体）、`FurnitureDurabilityComponent`、`AttributeComponent`、`AgentManager`（取家具/主角）、物品腐坏字段（`It_Rot_Product_Id`）
- Produces: `PunishmentOps.Apply(SpecialOp op, double opParam, double lossMult) -> string Summary`（`DurabilityPercentRandom`=随机 1 台电器 `AddCurrentDurability(-max*pct)`；`DurabilityPercentAll`=全屋家具；`DurabilityPercentRange`=随机 N 台（opParam 编码台数与百分比）；`AttrPermRandom`=随机 1 项基础属性 `SetBaseValue_Float(base-10×mult)`（五维池=Max 系属性，实机核对后固化）；`RotFood`=背包挑 `Life` 最短的 1~2 件生鲜替换为腐坏产物；`StealParts`=`CostItem` 铁制零件 round(10×mult)）；Summary 写入 `trace\punish.txt`

- [ ] **Step 1: 实现各 Op（每个 Op 一个私有方法，入口 switch）+ Summary 追加写 trace。**
- [ ] **Step 2: 实机验证**：`DebugForceEventId` 逐个强制 M1/M3/M4/H2/D2/H3(不追线)/D1：核对耐久条、属性面板、背包腐坏件、零件扣减、（D1 后）存读档再核对属性不回落。D1 完成后用替补开关（`AllowPermanentNegative=false`）再强制一次→走 D2 替补。
- [ ] **Step 3: Commit**：`git commit -m "feat: 惩罚执行器（耐久/永久属性/腐坏/偷窃+替补开关）"`

### Task 10: 选项事件接线（L1 三选一、H3 追/不追）

**Files:**
- Modify: `SurvivalLog.DailySurprise/Game/TableInjector.cs`（选项行）、`SurvivalLog.DailySurprise/Game/Exec/RewardExecutor.cs`（选项回调分发）
- Test: 映射已含于 Task 6 测试（L1 三选项 RewardSet）；本任务补 `InjectorMappingTests.ChoiceEvent_WiresThreeOptions`

**Interfaces:**
- Consumes: Task 2 首验 B 的选项结论
- Produces: 若原生选项链路可用：`Config_Event` 行带 `ChoseName_Local/ChoseID` → 各选项 RewardSet；若不可用（首验 B 失败）：降级为直接效果（L1 固定发三选一中最价值均衡的食物层；H3 固定走"不追"线），降级事实记 DESIGN.md 并在 README 标注

- [ ] **Step 1: 按 Task 2 结论接线或降级（二选一，代码路径已定）。**
- [ ] **Step 2: 实机验证**：强制 L1 三次，三个选项各点一次，核对三种奖励；强制 H3，"追"线连点 5 次（种子随机），确认追回/受伤流血两种结果都出现且 `受伤流血` buff 时长/数值正确。
- [ ] **Step 3: Commit**：`git commit -m "feat: 选项事件（神秘补给箱三选一+贼影追击）"`

### Task 11: IconDrop 与美术三件套

**Files:**
- Create: `SurvivalLog.DailySurprise/Game/Art/IconDrop.cs`
- Create: `SurvivalLog.DailySurprise/art-spec/prompt-template.md`、`SurvivalLog.DailySurprise/art-spec/compositions.md`、`SurvivalLog.DailySurprise/art-spec/acceptance-checklist.md`
- Create: `SurvivalLog.DailySurprise/Art/`（作者供图目录，先放 1 张占位 `ds_spike.png` 供链路验证）
- Test: `SurvivalLog.DailySurprise.Tests/IconNamingTests.cs`（45 个事件 Icon 字段与命名规则一致）

**Interfaces:**
- Consumes: csproj EmbeddedResource（`Art/**`）、EndlessGuard `EnsureIconFile` 模式
- Produces: `IconDrop.Install(string gameRoot)`（幂等：存在且哈希一致则跳过；写 `<游戏根>\SurvivalLog_Data\StreamingAssets\WebUI\Res\Icon\ds_*.png`）；三件套内容规范见 spec §7.8（构图清单=45 条：事件号/画面主体/场景要素/情绪基调）

- [ ] **Step 1: 写命名测试（失败→实现 `IconNaming` 校验→通过）。**
- [ ] **Step 2: 实现 `IconDrop`，用占位图实机验证落盘+Event.Icon 显示。**
- [ ] **Step 3: 撰写三件套**（prompt 模板锁风格关键词/色调/构图规则；compositions.md 45 条逐事件；acceptance-checklist.md：45 张齐、命名对、分辨率统一（建议 512×512 起）、风格一致性人工过目项）。
- [ ] **Step 4: Commit**：`git commit -m "feat: 图片落盘通道与美术生产三件套"`

### Task 12: 全链路冒烟与频率验收

**Files:**
- Modify: 按冒烟发现的问题修（不预先指定）
- Test: 无新增（验收型任务）

**Interfaces:**
- Consumes: 全部前序任务
- Produces: `trace\smoke-tier1.txt`、`trace\smoke-tier5.txt` 频率记录 + DESIGN.md 验收小节

- [ ] **Step 1: 档 1 冒烟**：cfg Difficulty=1，连续睡 10 天，记录每天的运势档/事件 Id；核对：好运天数 ≥7、无重度/灾厄、物品数量=基准×2。
- [ ] **Step 2: 档 5 冒烟**：Difficulty=5 连睡 10 天；核对：霉运天数 ≥4、保底未生效（无强制好运）、损失量=基准×2.5。
- [ ] **Step 3: 原生并存观察**：与游戏自带每日事件同屏出现时记录体感（是否轰炸）；若轰炸→事件卡弹窗延迟 3~5 秒（ticker 队列）后复测。
- [ ] **Step 4: 差异回填**：实测频率 vs spec §5.2 预期表，偏差>30% 的项在 DESIGN.md 记录并调内层权重（只调权重数字，不改结构）。
- [ ] **Step 5: Commit**：`git commit -m "test: 档1/档5 十日冒烟与频率验收"`

### Task 13: 文档、发版物料与整树评审

**Files:**
- Create: `README.md`、`ReleaseREADME.md`、`release.ps1`（ASCII-only，镜像 EndlessGuard 流水线）
- Modify: `SurvivalLog.DailySurprise/DESIGN.md`（终稿=spec 摘要+全部首验/冒烟结论）、`CHANGELOG.md`、`SurvivalLog.DailySurprise/SurvivalLog.DailySurprise.csproj`（版本号，待作者定）

**Interfaces:**
- Consumes: 全部
- Produces: 可发版仓库（除 45 张配图外齐备；配图到位后跑 release.ps1 即发）

- [ ] **Step 1: README（玩家文档）**：难度 5+1 档表、机制说明（保底/上限/防连抽/时长叠加语义）、自定义档示例配置、45 事件一览（分级表格）、单位说明（每 X 小时/百分比）。
- [ ] **Step 2: release.ps1 镜像 + CHANGELOG 首节草稿（版本号处留 `<待作者定>`）。**
- [ ] **Step 3: 45 张配图闸门脚本**（`release.ps1` 内校验 `Art/` 下 45 张齐备+命名合规，缺图即 fail——对应"发版硬闸门"）。
- [ ] **Step 4: 请作者确认版本号→填 csproj 与 CHANGELOG→跑一次 release.ps1 干跑（不 tag 不发）。**
- [ ] **Step 5: 整树终审**：fresh reviewer 过整个分支（代码+文档+套件规则符合性），问题清单修复。
- [ ] **Step 6: Commit**：`git commit -m "docs: 玩家文档与发版物料（版本闸门待作者定版）"`

---

## 执行顺序与依赖

Task 1→2（spike 定调）→3/4/5（纯逻辑，可并行）→6→7→8→9→10→11→12→13。
Task 2 的结论决定 Task 6/8/10 的实现分支（声明式 vs 代码、选项 vs 降级），必须最先完成。
