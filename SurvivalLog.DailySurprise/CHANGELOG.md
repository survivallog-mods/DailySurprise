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
