using System;

namespace SurvivalLog.DailySurprise;

/// <summary>自定义档（tier 6）设置（spec §5.3）：三百分比须各≥0 且和=100，非法→warn 一次并回退档 3。
/// RewardMultiplier/LossMultiplier 直接进 DifficultyParams（倍率轴完全由自定义承担）。</summary>
public sealed class CustomSettings
{
    public int GoodPercent;
    public int PlainPercent;
    public int BadPercent;
    public double RewardMultiplier;
    public double LossMultiplier;
}

/// <summary>难度参数（spec §5.2/§5.3 权威表的运行时形态，LuckEngine 消费）。
/// Good/Plain/Bad=运势比例（和=1.0）；GoodRarity=[普,稀,史,传]、BadRarity=[轻,中,重,灾厄] 权重
/// （和=100，数组即权重、和不必为 1）；PityDays=0 表示无保底。</summary>
public sealed class DifficultyParams
{
    public int Tier;
    public double Good, Plain, Bad;
    public double[] GoodRarity;
    public double[] BadRarity;
    public double RewardMult, LossMult;
    public int PityDays;
    public int PermNegativeCap;
}

/// <summary>难度表（纯逻辑，spec §5.2/§5.3）：tier→DifficultyParams。
/// 预设档数值逐格照抄 spec 权威表；tier=6 自定义档只取三轴比例与两倍率，
/// 内层稀有度分布/保底/永久负面上限跟档 3。每次调用返回全新数组（调用方可安全改写）。</summary>
public static class DifficultyTable
{
    /// <summary>解析难度参数。tier=6 且自定义三概率非法（含 null）→ warn 恰好一次并返回档 3 完整参数
    /// （含档 3 的比例/倍率，不归一化自定义值）；tier 出界（非 1~6）→ 同样 warn+档 3（防御性兜底）。warn 允许 null。</summary>
    public static DifficultyParams Resolve(int tier, CustomSettings custom, Action<string> warn)
    {
        switch (tier)
        {
            case 1:
                return Lucky();
            case 2:
                return Favorable();
            case 3:
                return Daily();
            case 4:
                return Troubled();
            case 5:
                return Cursed();
            case 6:
                if (custom != null
                    && custom.GoodPercent >= 0 && custom.PlainPercent >= 0 && custom.BadPercent >= 0
                    && custom.GoodPercent + custom.PlainPercent + custom.BadPercent == 100)
                {
                    return new DifficultyParams
                    {
                        Tier = 6,
                        Good = custom.GoodPercent / 100.0,
                        Plain = custom.PlainPercent / 100.0,
                        Bad = custom.BadPercent / 100.0,
                        GoodRarity = Tier3GoodRarity(),
                        BadRarity = Tier3BadRarity(),
                        RewardMult = custom.RewardMultiplier,
                        LossMult = custom.LossMultiplier,
                        PityDays = 3,
                        PermNegativeCap = 1,
                    };
                }
                warn?.Invoke(custom == null
                    ? "DailySurprise: 自定义难度缺少 CustomSettings（tier=6），回退档 3"
                    : $"DailySurprise: 自定义难度三概率 {custom.GoodPercent}/{custom.PlainPercent}/{custom.BadPercent} 非法（须各≥0 且和=100），回退档 3");
                return Daily();
            default:
                warn?.Invoke($"DailySurprise: 未知难度档 {tier}（有效 1~6），回退档 3");
                return Daily();
        }
    }

    // —— 预设档 1~5（spec §5.2 权威表逐格照抄）——

    /// <summary>档 1 幸运儿：75/20/5，灾厄层权重 0，永久负面不出现（cap=0）。</summary>
    private static DifficultyParams Lucky() => new()
    {
        Tier = 1,
        Good = 0.75,
        Plain = 0.20,
        Bad = 0.05,
        GoodRarity = new[] { 50d, 28d, 15d, 7d },
        BadRarity = new[] { 95d, 5d, 0d, 0d },
        RewardMult = 2.0,
        LossMult = 0.5,
        PityDays = 2,
        PermNegativeCap = 0,
    };

    /// <summary>档 2 顺风局。</summary>
    private static DifficultyParams Favorable() => new()
    {
        Tier = 2,
        Good = 0.55,
        Plain = 0.25,
        Bad = 0.20,
        GoodRarity = new[] { 58d, 26d, 12d, 4d },
        BadRarity = new[] { 80d, 17d, 3d, 0d },
        RewardMult = 1.5,
        LossMult = 0.75,
        PityDays = 2,
        PermNegativeCap = 1,
    };

    /// <summary>档 3 日复一日（基准档；自定义档与非法回退的内层结构来源）。</summary>
    private static DifficultyParams Daily() => new()
    {
        Tier = 3,
        Good = 0.45,
        Plain = 0.25,
        Bad = 0.30,
        GoodRarity = Tier3GoodRarity(),
        BadRarity = Tier3BadRarity(),
        RewardMult = 1.0,
        LossMult = 1.0,
        PityDays = 3,
        PermNegativeCap = 1,
    };

    /// <summary>档 4 命运多舛。</summary>
    private static DifficultyParams Troubled() => new()
    {
        Tier = 4,
        Good = 0.30,
        Plain = 0.25,
        Bad = 0.45,
        GoodRarity = new[] { 78d, 17d, 4.5d, 0.5d },
        BadRarity = new[] { 55d, 30d, 12d, 3d },
        RewardMult = 0.75,
        LossMult = 1.75,
        PityDays = 5,
        PermNegativeCap = 2,
    };

    /// <summary>档 5 厄运缠身：无保底（PityDays=0）。</summary>
    private static DifficultyParams Cursed() => new()
    {
        Tier = 5,
        Good = 0.15,
        Plain = 0.25,
        Bad = 0.60,
        GoodRarity = new[] { 84d, 13d, 2.7d, 0.3d },
        BadRarity = new[] { 45d, 30d, 19d, 6d },
        RewardMult = 0.5,
        LossMult = 2.5,
        PityDays = 0,
        PermNegativeCap = 4,
    };

    private static double[] Tier3GoodRarity() => new[] { 70d, 22d, 7d, 1d };
    private static double[] Tier3BadRarity() => new[] { 70d, 25d, 4.5d, 0.5d };
}
