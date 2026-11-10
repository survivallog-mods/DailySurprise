using BepInEx.Configuration;
using BepInEx.Logging;

namespace SurvivalLog.DailySurprise;

/// <summary>cfg 静态包装（spec §7.4 最终暴露面 8 项 + 调试 DebugForceEventId）。
/// 只做 Bind 与取值转发：所有键读取时实时取 <see cref="ConfigEntry{T}.Value"/>
/// （BepInEx cfg 文件改动即生效=热更，无需 SettingChanged 订阅缓存）；
/// 数值合法性（Difficulty 越界/自定义三概率和≠100）由 DifficultyTable.Resolve 回退档 3
/// （T3 已实现并测试），本层不重复校验。Bind 集中在 <see cref="Init"/>（插件 Load 调一次）。</summary>
internal static class DsConfig
{
    internal static ConfigEntry<int> Difficulty;
    internal static ConfigEntry<int> CustomGoodPercent;
    internal static ConfigEntry<int> CustomPlainPercent;
    internal static ConfigEntry<int> CustomBadPercent;
    internal static ConfigEntry<double> CustomRewardMultiplier;
    internal static ConfigEntry<double> CustomLossMultiplier;
    internal static ConfigEntry<bool> ShowPlainPopup;
    internal static ConfigEntry<bool> AllowPermanentNegative;

    /// <summary>Task 2 起的调试强制触发（原在插件类，Task 7 起随 cfg 面收拢到本类；
    /// <see cref="SpikeForce"/> 消费）。0=关闭。</summary>
    internal static ConfigEntry<int> DebugForceEventId;

    internal static void Init(ConfigFile config)
    {
        Difficulty = config.Bind("General", "Difficulty", 3,
            "难度档：1=幸运儿 2=顺风局 3=日复一日 4=命运多舛 5=厄运缠身 6=自定义。越界值回退档 3。");
        CustomGoodPercent = config.Bind("General", "CustomGoodPercent", 45,
            "档 6 自定义：好运天数百分比。三概率各≥0 且和=100，非法回退档 3。");
        CustomPlainPercent = config.Bind("General", "CustomPlainPercent", 25,
            "档 6 自定义：平淡天数百分比（与 Good/Bad 求和须=100）。");
        CustomBadPercent = config.Bind("General", "CustomBadPercent", 30,
            "档 6 自定义：霉运天数百分比（与 Good/Plain 求和须=100）。允许 0=纯抽奖。");
        CustomRewardMultiplier = config.Bind("General", "CustomRewardMultiplier", 1.0,
            "档 6 自定义：奖励倍率（0.1~5.0），缩放物品数量。");
        CustomLossMultiplier = config.Bind("General", "CustomLossMultiplier", 1.0,
            "档 6 自定义：损失倍率（0.1~5.0），缩放耐久/偷窃/属性损失。");
        ShowPlainPopup = config.Bind("General", "ShowPlainPopup", true,
            "平淡档弹窗开关：false=平淡事件不拉事件卡，改为主角头顶气泡播台词（记账照常）。");
        AllowPermanentNegative = config.Bind("General", "AllowPermanentNegative", true,
            "永久负面事件（H1 心结/D1 代价/D3 低语）开关：false=全部走替补（H4 重病/D2 雷击/H2 管道爆裂）。");
        DebugForceEventId = config.Bind("General", "DebugForceEventId", 0,
            "首验调试：>0=进档且表就绪后对注入事件行强制触发一次（事件行 ID 99200~99244，如 99205=G1；99250 是 RewardSet ID 无效）。0=关闭。每进档一次只触发一次，结果写 BepInEx\\plugins-data\\DailySurprise\\trace\\force.txt。");
    }

    // —— 取值面（实时读 .Value；Init 前调用=默认值兜底，不抛） ——

    internal static int Tier => Difficulty?.Value ?? 3;

    internal static CustomSettings CustomOrDefault() => new()
    {
        GoodPercent = CustomGoodPercent?.Value ?? 45,
        PlainPercent = CustomPlainPercent?.Value ?? 25,
        BadPercent = CustomBadPercent?.Value ?? 30,
        RewardMultiplier = CustomRewardMultiplier?.Value ?? 1.0,
        LossMultiplier = CustomLossMultiplier?.Value ?? 1.0,
    };

    internal static bool PlainPopupEnabled => ShowPlainPopup?.Value ?? true;

    /// <summary>永久负面开关：false 时 DailyFlow 把 PermNegativeCap 置 0（LuckEngine 对每个
    /// isPermNegative 命中立即走替补，等价 spec §5.4「关=走替补」）。</summary>
    internal static bool PermNegativeAllowed => AllowPermanentNegative?.Value ?? true;

    /// <summary>回退提示通道（Resolve 的 warn 回调）：节流写 trace\cfg.txt——cfg 文件不改就安静。</summary>
    internal static void Warn(string message) => DsTrace.WriteThrottled("cfg", message);
}
