# 喜从天降 DailySurprise — 设计记录

> 暂为占位（Task 1 起）。机制与数值权威 = spec：`../../docs/superpowers/specs/2026-11-10-daily-surprise-design.md`（已冻结，计划以 spec 为准）。
> 本文件累积各任务实证结论（首验 spike、实机核对、冒烟频率）；Task 13 终稿 = spec 摘要 + 全部首验/冒烟结论。
> 版本：跟随本仓 csproj `<Version>`（占位 0.1.0；发版号由作者定，见 Task 13）。

## 首验记录区（各任务回填）

### Task 1：工程脚手架与插件骨架（2026-11-10）

- 状态：代码侧门通过（`dotnet.exe build` 退出码 0；`dotnet.exe test` 退出码 0，1 passed）。
- 实机清单（`trace\_DONE.txt` 自证 + 主角头顶署名气泡）移交作者执行；dll 已按计划拷至
  `BepInEx\plugins\SurvivalLog.DailySurprise\`。
- 结构：`Core/` 纯逻辑层（可单测，禁 BepInEx/il2cpp 引用）与 `Game/` il2cpp 层隔离；
  `Ds` 身份常量在 Core/Ds.cs（Tests 源链接断言），`DsTrace` 按计划与插件同文件（BaseDir 可注入备测）。

### Task 2：首验 spike——单事件注入与强制触发（2026-11-10）

- 状态：代码侧门通过（`dotnet.exe build` 退出码 0；`dotnet.exe test` 退出码 0，11 passed）。
  实机验证 A/B（下两节）移交作者执行，**结论待回填**。

#### 2.0 查证记录（静态核对，gamekb 2026-11-05_26454866）

- 触发 API：`DynamicEventManager.TryForceTriggerEvent(int eventId, bool ignorePlayerSelect = False)`，
  返回 `bool`（dump.cs:415573）；同类 `ForceTriggerEvent`（void，dump.cs:415570）。取 bool 版，失败可观测。
- 管理器获取：`GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.Instance
  ._DynamicEventManager`（internal 属性 dump.cs:385844；`IsInstanceCreated` 防护；同字段族 `_AgentManager`
  已被 EndlessGuard 在役访问，interop 代理公开 internal 成员）。DynamicEventManager 命名空间=
  `GameCore.HotUpdate.Battle.Logic`（dump.cs:415004 行上注）。
- 表消费路径：`ConfigManager.Get_Config_Event(key)`（dump.cs:54861）/`Get_Config_RewardSet(key)`
  （dump.cs:54987）按需查字典 → 注入行注入 `ConfigManager._Config_Event_Dict`（dump.cs:54517）/
  `_Config_RewardSet_Dict`（dump.cs:54559）即可达；发放执行器 `RewardSetManager.ExecuteRewardSet(int
  rewardSetId, …)`（dump.cs:431723）同样按 id 取行。**是否在触发时快照/缓存**（`DynamicEventManager.Configs`，
  dump.cs:415039）→ 验证A 的 trace/实机观察回答。
- 样式字段取值（Config_Event.json 逐字抄录）：验证A 模板=原生行 10011「暴雨侵袭-用毛巾堵漏」
  （Type=2 Result / ShowType=1 / PopUpType=1 / KeepTime=0 / Icon=`Assets/RuntimeAssets/Texture/EventNew/022` /
  OKDes_Local=我知道了 / DemoTwoMode=2）；验证B 模板=原生行 103「事件3」（Type=1 Choice / PopUpType=3 /
  Countdown=60 / Default=末选项子事件 / Icon=Literature/…1490200101 / DemoTwoMode=3）。
  共有：EndlessMode=1 / LogType=1 / DataMin=1 / DataMax=999 / TimeMin=1 / TimeMax=24 / TasteRequire=-1。
- 物品与发放编码：2101=「90压缩饼干」（Config_Item.json）；`Config_RewardSet.ItemChange` 语义=itemId
  逐次重复出现表件数（行 6420 实证：[2107,2107,2107,2209,…]=3×2107+…），4 件=[2101,2101,2101,2101]。
- 刻意偏离模板（保证强制触发不被表内条件挡）：ConditionSetId/NeedConditionSetId/TaskConditionSetId/
  TaskTime 清零（模板 10011 带 NeedConditionSetId=13、103 带 ConditionSetId=3=自然触发资格门）；
  RewardSetId 置 0 只留 StartRewardSetId=99250（归因单一：验证A 专测 StartRewardSet 是否自动发放）。
- 字段写入模板（IL2CPP，EndlessGuard ArrowTower 同款；**List<string>（ChoseName_Local 等）与
  List<int> 同构，全新实例逐元素填，禁 null/共享实例**——官方消费方直读 .Count）：

  ```csharp
  var cm = SurvivalLog.ModShared.BagTable.Cm();            // IsInstanceCreated 防护，未就绪=null
  var evDict = cm._Config_Event_Dict;                      // null=装表中，ticker 下轮再试
  if (!evDict.TryGetValue(99200, out var ev) || ev == null)
  { ev = new GameCore.HotUpdate.Config_Event(); evDict[99200] = ev; }   // 行级幂等：在则复用
  ev.Type = 2; ev.PopUpType = 1; ev.KeepTime = 0;           // int/string 属性直写
  ev.ChoseName_Local = new Il2CppSystem.Collections.Generic.List<string>();   // List<T> 全新实例
  ev.ChoseName_Local.Add("拿走");
  ```

#### 2.1 验证A：弹窗 + StartRewardSet 自动发放（待实机）

- cfg：`DebugForceEventId=99200`，`DebugSpikeChose=false`（默认）。
- 预期：进档后 ≤2s+弹窗排队弹原生事件卡（标题「喜从天降·首验」、台词「这是一次测试：天上掉下来一箱
  压缩饼干。」、图标=原生 022 图、按钮「我知道了」）；关闭后背包出现 4×「90压缩饼干」。
- 逐项观察（结论待回填）：
  - 弹窗是否出现 / 台词是否正确 / 图标是否显示：___
  - `trace\force.txt` 的 `TryForceTriggerEvent(99200) -> ?`：___
  - 关窗后背包 4×压缩饼干：___（**否** → 结论=StartRewardSet 不自动发放，v1 RewardSet 走代码路径）
- 结论：___

#### 2.2 验证B：ChoseName_Local 选项 UI（待实机）

- cfg：`DebugForceEventId=99200`，`DebugSpikeChose=true`（改后重进档；行形态哨兵自动重拍为选项行）。
- 预期：弹选项卡（PopUpType=3 样式），两按钮「拿走 / 算了」可点；点击分别触发原生子事件 10011/10012
  （各弹自己的结果卡）。Countdown=60s 归零自动走「算了」（Default=10012，原生 103 同构）。
- 逐项观察（结论待回填）：
  - 选项卡是否出现 / 两按钮文案是否正确：___
  - 点击是否生效（触发对应子事件）：___（**否** → 降级方案启用：所有【选项】事件改直接效果）
  - 倒计时自动走 Default 是否生效（可选观察）：___
- 结论：___

#### 2.3 Icon 相对路径（待实机，Task 11 前置）

- 本任务 Icon 用原生绝对路径（原生资源必在）。自绘 `../../Res/Icon/ds_*.png` 相对路径是否被事件卡
  UI 支持 → Task 11 IconDrop 落地时实测（EndlessGuard wave8 已证 WebUI/ToolTable.html 通道支持相对路径，
  事件卡 UI 是否同源待验）。
- 结论：___

### Task 7：每日触发链（补丁+模式门控+幂等+兜底）（2026-11-10）

- 状态：代码侧门通过（`dotnet.exe build` 退出码 0 零警告；`dotnet.exe test` 124/124 通过，
  新增 DailyFlowLogicTests 20 例）。实机验证清单（见 task-7-report.md）移交作者执行，**结论待回填**。

#### 7.0 查证记录（静态核对，gamekb 2026-11-05_26454866）

- **模式门控**：`WorldStateType` 枚举仅二值——`PreDisaster=1 / PostDisaster=2`（dump.cs:58256~58262）。
  读点=`GameTimeManager.WorldStateType`（public getter，类 dump.cs:421227；经
  `BattleLogicWorld._GameTimeManager` internal 属性 dump.cs:385813，interop 代理公开）。
  判定口径=**值≠1（PreDisaster）即放行**；读不到世界态=不放行（安全向，宁漏勿误）。
- **无尽档映射（静态结论，实机复核）**：`Config_Chapter.json` 全 9 行——仅章节 1（灾前倒计时）`WorldStateType=1`；
  章节 0/2/1000~1005（灾后主章+全部探索章，无尽期在此）均=2。无尽标识旁证：`EndlessKind`
  {None=0,Pure=1,Story=2,Casual=3}（dump.cs:70236）、`HistoryChild.EndlessStartDay`（dump.cs:~80861）、
  `TowerWaveManager.IsActive`（EndlessGuard 在役无尽判定）。**预期无尽档 WorldStateType=2→放行**；
  每日触发记录行尾带 `WorldStateType=N` 观测值，实机回报后固化。
- **资格查询**：电器=`Config_Furniture.IsElectrical`（bool，dump.cs:36922；表数据 137 行电器）；
  家具列表=`AgentManager.GetAllFurnitures()`（dump.cs:379587）；耐久组件=
  `FurnitureDurabilityComponent`（dump.cs:374068）经 `AgentTools.GetAgentComponent<T>`（EndlessGuard 在役）；
  生鲜=`Config_Item.Life>0`（dump.cs:40276）且 `It_Rot_Product_Id` 非空（dump.cs:40280，表数据 2700 行），
  未腐坏=`ItemData.TimeLeft>0`（dump.cs:424047），背包 owner=`ReduxUISystem.reduxStoreLayer.dataTree
  [State_Data_Player].PlayerId`（CabinetEverywhere 校准点 T14 在役）；铁制零件=20362（表数据「铁制零件」），
  计数=`ItemManager.GetLeadingRoleItemCount(int)`（游戏原生主角持有口径）。**查询失败=无资格**（安全向）。
- **结算锚点**：`GameSaveManager.CaptureDaySnapshot(int day)`（public，dump.cs:88810）；SaveMgr 同点在役。
- **存档名/日翻**：`GetLastPlayFileName`/`GetHistoryList`/`EnsureHistoryV2Migrated`（dump.cs:88156/88135/88210）；
  局内读名 NRE→菜单期缓存回落（SaveMgr 2026-10-31 实证）。

#### 7.1 触发链形态

- `Patch_CaptureDaySnapshot`（Harmony postfix，TargetMethod 反射+参数类型钉死）→ `DailyFlow.OnNewDay(day)`；
  `DayWatcher`（2s ticker 轮询存档 meta 日增量，hook 60s 内让路）兜底；同日幂等（`LastTriggeredDay`）
  吸收双触发与读档回昨天重睡。
- 状态槽：`plugins-data\DailySurprise\saves\<存档基名>\state.json`（StateStore 原子写）。
- trace 口径：`trace\daily.txt` 每天至多一行触发记录（day+kind+event+标题+pity/subst/perm+badStreak/
  permUsed/disasters+card/talk 返回值+WorldStateType）；灾前静默记 `trace\gate.txt`（节流）。
- cfg 面落地（DsConfig）：`Difficulty/CustomGoodPercent/CustomPlainPercent/CustomBadPercent/
  CustomRewardMultiplier/CustomLossMultiplier/ShowPlainPopup/AllowPermanentNegative/DebugForceEventId`，
  读取实时取值=热更；非法值由 DifficultyTable 回退档 3（T3 已测）。
