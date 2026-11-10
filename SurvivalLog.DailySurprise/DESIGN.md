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

### Task 2：首验 spike——单事件注入与强制触发

- （待回填：发放链路结论 / 选项字段类型 / Icon 相对路径 / 字段写入模板）
