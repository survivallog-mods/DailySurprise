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
