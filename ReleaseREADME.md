# SurvivalLog.DailySurprise — 喜从天降

## 这是干什么的
每个游戏日睡醒时，随机降临一个"天降事件"：好运（物资、buff）、霉运（debuff、建筑受损）或平平无奇，原生事件卡弹窗（配图+台词，部分带选项），45 个事件全量首发。仅灾后生效（普通档+无尽档），灾前序章不触发；每天至多一次。

| 机制 | 说明 |
|---|---|
| 5+1 难度档 | 1 幸运儿 / 2 顺风局 / 3 日复一日（默认）/ 4 命运多舛 / 5 厄运缠身 / 6 自定义（手动调三档概率与倍率） |
| 防劝退 | 霉运保底（连霉 N 天强制好运）、永久负面上限（达上限自动换非永久替补）、灾厄在档 1/2 不出现、同事件防连抽 |
| buff 规则 | 相同 buff 重复获得=时长叠加、强度不变；永久 buff 重复获得无变化 |
| 存档安全 | 不碰游戏存档结构；mod 记账存本地 JSON（`BepInEx\plugins-data\DailySurprise\`） |

## 安装
- **Full 完整包（新玩家）**：解压到游戏根目录（Steam 库 `steamapps\common\Survival Log`），自带 BepInEx 6 基座，装完即用。
- **PluginOnly 纯插件包**：已装 BepInEx 6 的玩家，把 `BepInEx\plugins\SurvivalLog.DailySurprise.dll` 放进游戏 `BepInEx\plugins\`，重启游戏即生效。

## 难度怎么选
配置文件 `BepInEx\config\com.local.survivallog.dailysurprise.cfg` 改 `Difficulty`（1~5 预设，6=自定义），保存即生效、无需重启。新玩家保持默认 3 即可；想天天开箱爽选 1/2；想给长期档加压力选 4/5。详细难度表、45 事件一览、自定义档示例见仓库 README。

## 怎么知道生效了
- 睡觉进入新一天后弹原生事件卡（配图+台词），关卡即发奖 / 惩罚。
- 事件触发记录在 `BepInEx\plugins-data\DailySurprise\trace\daily.txt`。
- 想立刻验证：cfg 把 `DebugForceEventId` 设为 99205（救灾空投）重进档，必弹一次好运卡。

## 常见问题
- **今天没弹？** 灾前序章不触发（需过灾变）；平淡档同样弹卡，所有事件一律走事件卡。
- **霉运一次没来过？** 部分霉运事件需要资产（家具/电器/生鲜/铁制零件），开局空屋会自动换抽别的——属预期。
- **永久负面太狠？** cfg `AllowPermanentNegative=false`，心结/代价/低语全部换成非永久替补。
- **与其他 mod 冲突吗？** 本套件各 mod 可同装；注入全走进程内存（ID 段 99200~99399 独占），不改游戏文件。

## 卸载
删除 `BepInEx\plugins\SurvivalLog.DailySurprise.dll` 即可（Full 包玩家再删 `BepInEx\` 目录、`winhttp.dll`、`dotnet\` 目录）。已获得的物品/buff/属性变化是游戏自己的数据，按剩余时长自然消退；mod 记账留在 `BepInEx\plugins-data\DailySurprise\`（无害，可整目录删）。
