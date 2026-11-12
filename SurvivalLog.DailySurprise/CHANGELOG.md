# 更新日志（SurvivalLog.DailySurprise / 喜从天降）

> 本文件只记本 mod 的更新。版本号单源：本仓 `SurvivalLog.DailySurprise/SurvivalLog.DailySurprise.csproj`
> 的 `<Version>`（当前占位 0.1.0，发版号由作者定，Task 13 填）。

## 未定号（2026-11-10 起，待作者定版）

- 工程脚手架与插件骨架（Task 1）：BepInEx 6 IL2CPP 插件骨架 + 自证 trace（`trace\_DONE.txt`）+
  WelcomeTip 署名气泡接线 + 测试工程（冒烟 1 例）。
- 首验 spike（Task 2）：`Config_Event` 99200 + `Config_RewardSet` 99250（4×压缩饼干）内存注入
  （2s ticker 哨兵复查，官方重建表自动重放）+ cfg `DebugForceEventId` 强制触发
  （`TryForceTriggerEvent`，结果写 `trace\force.txt`）+ cfg `DebugSpikeChose` 选项行变体（验证B）+
  数据装配测试 10 例；实机验证清单见 DESIGN.md §2.1/§2.2（结论待回填）。
- 核心引擎（Task 3~5，补记）：难度表与掷骰引擎（保底/降层/防连抽/上限替补）、45 事件目录 +
  14 条 buff 蓝图、分槽存取与原子写的状态存储。
- 四表全量注入（Task 6）：`Config_Event` 45 行（99200~99244）+ `Config_RewardSet` 50 行
  （99250~99294 基础 + 99295~99299 选项）+ `Config_Buff` 14 行（99280~99293 克隆/自注入）+
  `Config_Effect` 14 行（99290~99303 独立编号）；映射纯函数 `InjectorMapping`（可单测，21 例）；
  cfg `DebugSpikeChose` 移除（选项行形态改由事件目录 Choices 驱动）；`trace\inject.txt` 报实际
  四表行数；ticker 重复异常 trace 节流（每 50 次一记）。
- 奖励发放（Task 8）：`RewardMath` 纯函数（数量四舍五入下限 1 / 拒收挂账 / 命运眷顾与行运低潮
  变体映射 / 随机×1，40 例测试）+ `RewardExecutor` 代码路径发放（物品按难度倍率缩放入包、
  可腐品满保鲜 Life×24h；buff 经 `BuffComponent.AddBuff`；属性经 `AddConditionValue` 可负；
  Op 系留 Task 9）+ 进档补发链 `PendingFlusher`（先于每日触发；入队存已缩放 spec、补发不二次
  缩放；补完主角气泡「昨天塞不下的东西送到了」）。**Controller 裁定**：事件行
  `StartRewardSetId` 改 0（防空双发），全部奖励统一代码路径，RewardSet 行保留备查；
  `DebugForceEventId` 强制直发事件时同链路即时发放（`trace\grants.txt`）。
- 选项事件接线（Task 10）：L1「神秘补给箱」/H3「贼影」接入原生选项链——父行 ChoseID/Default 指向
  5 个子事件行（99245~99249，形态抄原生 10010 父+10011/104 子），子事件 RewardSetId→选项
  RewardSet 行（99295~99299）声明式发奖（物品/加 buff/扣件三通道）；H3 顶层偷窃 Op 短路
  （RunsTopLevelOp）——零件损失改在「算了」子事件声明式扣（追=受伤流血但零件不变，防刷件）；
  强制调试链路同语义。50/50 不可声明式（RandomGroup 仅物品池随机），取舍与降级预案见
  DESIGN.md §10；实机验证并入冒烟（Task 12）。
