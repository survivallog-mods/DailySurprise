using BepInEx.Configuration;
using BepInEx.Logging;

namespace SurvivalLog.DailySurprise;

/// <summary>cfg 静态包装（spec §7.4 最终暴露面 7 项 + TriggerHour（Task 14 定时触发）；调试配置
/// 模块已移除，ShowPlainPopup 已随 A2 取消——平淡事件同样弹卡）。
/// 只做 Bind 与取值转发：所有键读取时实时取 <see cref="ConfigEntry{T}.Value"/>
/// （BepInEx cfg 文件改动即生效=热更，无需 SettingChanged 订阅缓存）；
/// 数值合法性（Difficulty 越界/自定义三概率和≠100）由 DifficultyTable.Resolve 回退档 3
/// （T3 已实现并测试），本层不重复校验。Bind 集中在 <see cref="Init"/>（插件 Load 调一次）。</summary>
internal static class DsConfig
{
    /// <summary>General.Enabled 总开关（ModCenter 启停位，套件惯例）：false=立即停止全部
    /// 天降行为（不掷骰/不弹卡/不发奖/不记账/不注册），注入表行与记账数据保留，true 恢复。
    /// 消费点：Patch_CaptureDaySnapshot / ConfigRegistrar / DsTicker（三入口一夫当关）。</summary>
    internal static ConfigEntry<bool> Enabled;

    internal static ConfigEntry<string> Difficulty;
    internal static ConfigEntry<int> CustomGoodPercent;
    internal static ConfigEntry<int> CustomPlainPercent;
    internal static ConfigEntry<int> CustomBadPercent;
    internal static ConfigEntry<double> CustomRewardMultiplier;
    internal static ConfigEntry<double> CustomLossMultiplier;
    internal static ConfigEntry<bool> AllowPermanentNegative;
    internal static ConfigEntry<int> TriggerHour;

    internal static void Init(ConfigFile config)
    {
        // General.Enabled 放最前 Bind（套件惯例：保证 cfg 键已生成后才可能短路）
        Enabled = config.Bind("General", "Enabled", true,
            "总开关：false=立即停止全部天降事件（不掷骰/不弹卡/不发奖/不记账）；true 恢复。");
        Difficulty = config.Bind("General", "Difficulty", "日复一日",
            new ConfigDescription("难度档：幸运儿/顺风局/日复一日/命运多舛/厄运缠身/自定义（面板下拉用档名）。旧数字值 1~6 仍可读，其他值回退档 3。",
                new AcceptableValueList<string>("幸运儿", "顺风局", "日复一日", "命运多舛", "厄运缠身", "自定义")));
        CustomGoodPercent = config.Bind("General", "CustomGoodPercent", 45,
            new ConfigDescription("档 6 自定义：好运天数百分比。三概率各≥0 且和=100，非法回退档 3。",
                new AcceptableValueRange<int>(0, 100)));
        CustomPlainPercent = config.Bind("General", "CustomPlainPercent", 25,
            new ConfigDescription("档 6 自定义：平淡天数百分比（与 Good/Bad 求和须=100）。",
                new AcceptableValueRange<int>(0, 100)));
        CustomBadPercent = config.Bind("General", "CustomBadPercent", 30,
            new ConfigDescription("档 6 自定义：霉运天数百分比（与 Good/Plain 求和须=100）。允许 0=纯抽奖。",
                new AcceptableValueRange<int>(0, 100)));
        CustomRewardMultiplier = config.Bind("General", "CustomRewardMultiplier", 1.0,
            new ConfigDescription("档 6 自定义：奖励倍率（0.1~5.0），缩放物品数量。",
                new AcceptableValueRange<double>(0.1, 5.0)));
        CustomLossMultiplier = config.Bind("General", "CustomLossMultiplier", 1.0,
            new ConfigDescription("档 6 自定义：损失倍率（0.1~5.0），缩放耐久/偷窃/属性损失。",
                new AcceptableValueRange<double>(0.1, 5.0)));
        TriggerHour = config.Bind("General", "TriggerHour", 1,
            new ConfigDescription("每天游戏内几点触发（1=凌晨1点睡醒后），0=日结算时立即触发。范围 0~23，越界值自动夹回并提示。",
                new AcceptableValueRange<int>(0, 23)));
        AllowPermanentNegative = config.Bind("General", "AllowPermanentNegative", true,
            "永久负面事件（H1 心结/D1 代价/D3 低语）开关：false=全部走替补（H4 重病/D2 雷击/H2 管道爆裂）。");
    }

    // —— 取值面（实时读 .Value；Init 前调用=默认值兜底，不抛） ——

    /// <summary>总开关现值（Init 前=true 兜底，不抛）。cfg 热更——false 时三入口
    /// （结算 hook / 快照注册 / ticker）全部短路，下拍即停。</summary>
    internal static bool MasterEnabled => Enabled?.Value ?? true;

    internal static int Tier => ParseTier(Difficulty?.Value);

    /// <summary>档名→档号（2026-11-14 作者要求：面板下拉显示档名，cfg 值即档名）：
    /// 六档名映射 1~6；旧数字串（"1"~"6"，存量 cfg/手改）兼容读；空/未知回 3
    ///（与原 int 键"越界回退档 3"同语义，DifficultyTable.Resolve 侧不重复校验）。</summary>
    private static int ParseTier(string s)
    {
        switch (s)
        {
            case "幸运儿": return 1;
            case "顺风局": return 2;
            case "日复一日": return 3;
            case "命运多舛": return 4;
            case "厄运缠身": return 5;
            case "自定义": return 6;
            default: return int.TryParse(s, out var n) && n >= 1 && n <= 6 ? n : 3;
        }
    }

    internal static CustomSettings CustomOrDefault() => new()
    {
        GoodPercent = CustomGoodPercent?.Value ?? 45,
        PlainPercent = CustomPlainPercent?.Value ?? 25,
        BadPercent = CustomBadPercent?.Value ?? 30,
        RewardMultiplier = CustomRewardMultiplier?.Value ?? 1.0,
        LossMultiplier = CustomLossMultiplier?.Value ?? 1.0,
    };

    /// <summary>定时触发小时（Task 14）：0=旧行为（结算即触发）；1~23=每天游戏内该小时后触发。
    /// 越界值夹回 [0,23] 并经 Warn 留痕（节流）——非法值不得让"结算通道退位+小时门失灵"同时发生。</summary>
    internal static int TriggerHourValue
    {
        get
        {
            var v = TriggerHour?.Value ?? 1;
            if (v >= 0 && v <= 23) return v;
            Warn("TriggerHour=" + v + " 越界，已夹回 " + (v < 0 ? 0 : 23));
            return v < 0 ? 0 : 23;
        }
    }

    /// <summary>永久负面开关：false 时 DailyFlow 把 PermNegativeCap 置 0（LuckEngine 对每个
    /// isPermNegative 命中立即走替补，等价 spec §5.4「关=走替补」）。</summary>
    internal static bool PermNegativeAllowed => AllowPermanentNegative?.Value ?? true;

    /// <summary>回退提示通道（Resolve 的 warn 回调）：节流写 trace\cfg.txt——cfg 文件不改就安静。</summary>
    internal static void Warn(string message) => DsTrace.WriteThrottled("cfg", message);
}
