using System.Collections.Generic;
using System.Linq;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>DifficultyTable.Resolve 权威数值测试（spec §5.2/§5.3 表逐格照抄 + 自定义档与回退规则）。
/// 数值断言即规格冻结：上游表若变，改实现必须连这里的数字一起改（评审可见）。</summary>
public sealed class DifficultyTableTests
{
    private sealed class WarnSpy
    {
        public readonly List<string> Messages = new();
        public void OnWarn(string message) => Messages.Add(message);
    }

    // —— 预设档 1~5：全字段逐格对照 spec §5.2 权威表 ——

    [Theory]
    [InlineData(1, 0.75, 0.20, 0.05, new[] { 50d, 28d, 15d, 7d }, new[] { 95d, 5d, 0d, 0d }, 2.0, 0.5, 2, 0)]   // 幸运儿
    [InlineData(2, 0.55, 0.25, 0.20, new[] { 58d, 26d, 12d, 4d }, new[] { 80d, 17d, 3d, 0d }, 1.5, 0.75, 2, 1)]  // 顺风局
    [InlineData(3, 0.45, 0.25, 0.30, new[] { 70d, 22d, 7d, 1d }, new[] { 70d, 25d, 4.5d, 0.5d }, 1.0, 1.0, 3, 1)] // 日复一日
    [InlineData(4, 0.30, 0.25, 0.45, new[] { 78d, 17d, 4.5d, 0.5d }, new[] { 55d, 30d, 12d, 3d }, 0.75, 1.75, 5, 2)] // 命运多舛
    [InlineData(5, 0.15, 0.25, 0.60, new[] { 84d, 13d, 2.7d, 0.3d }, new[] { 45d, 30d, 19d, 6d }, 0.5, 2.5, 0, 4)]  // 厄运缠身
    public void Resolve_Presets_MatchSpecPerCell(int tier, double good, double plain, double bad,
        double[] goodRarity, double[] badRarity, double reward, double loss, int pity, int cap)
    {
        var spy = new WarnSpy();
        var p = DifficultyTable.Resolve(tier, null, spy.OnWarn);

        Assert.Equal(tier, p.Tier);
        Assert.Equal(good, p.Good);
        Assert.Equal(plain, p.Plain);
        Assert.Equal(bad, p.Bad);
        Assert.Equal(1d, p.Good + p.Plain + p.Bad, 5);   // 三轴比例和=1.0
        Assert.Equal(goodRarity, p.GoodRarity);
        Assert.Equal(badRarity, p.BadRarity);
        Assert.Equal(100d, p.GoodRarity.Sum(), 5);       // 稀有度权重和=100
        Assert.Equal(100d, p.BadRarity.Sum(), 5);
        Assert.Equal(reward, p.RewardMult);
        Assert.Equal(loss, p.LossMult);
        Assert.Equal(pity, p.PityDays);
        Assert.Equal(cap, p.PermNegativeCap);
        Assert.Empty(spy.Messages);                      // 预设档不告警
    }

    // —— 自定义档（tier 6，spec §5.3）——

    [Fact]
    public void Resolve_CustomSumInvalid_WarnsOnceAndFallsBackToFullTier3()
    {
        var spy = new WarnSpy();
        var custom = new CustomSettings
        {
            GoodPercent = 40, PlainPercent = 25, BadPercent = 30,   // 和=95≠100
            RewardMultiplier = 2.2, LossMultiplier = 0.4,
        };
        var p = DifficultyTable.Resolve(6, custom, spy.OnWarn);

        // 完整档 3 参数（比例/倍率一并回退，不归一化自定义值）
        Assert.Equal(3, p.Tier);
        Assert.Equal(0.45, p.Good);
        Assert.Equal(0.25, p.Plain);
        Assert.Equal(0.30, p.Bad);
        Assert.Equal(new[] { 70d, 22d, 7d, 1d }, p.GoodRarity);
        Assert.Equal(new[] { 70d, 25d, 4.5d, 0.5d }, p.BadRarity);
        Assert.Equal(1.0, p.RewardMult);
        Assert.Equal(1.0, p.LossMult);
        Assert.Equal(3, p.PityDays);
        Assert.Equal(1, p.PermNegativeCap);

        var message = Assert.Single(spy.Messages);       // warn 恰好一次
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void Resolve_CustomValid_UsesCustomAxesWithTier3InnerStructure()
    {
        var spy = new WarnSpy();
        var custom = new CustomSettings
        {
            GoodPercent = 50, PlainPercent = 25, BadPercent = 25,
            RewardMultiplier = 1.3, LossMultiplier = 1.6,
        };
        var p = DifficultyTable.Resolve(6, custom, spy.OnWarn);

        Assert.Equal(6, p.Tier);
        Assert.Equal(0.50, p.Good);
        Assert.Equal(0.25, p.Plain);
        Assert.Equal(0.25, p.Bad);
        Assert.Equal(1.3, p.RewardMult);
        Assert.Equal(1.6, p.LossMult);
        // 内层稀有度/保底/永久负面上限：跟档 3（spec §5.3）
        Assert.Equal(new[] { 70d, 22d, 7d, 1d }, p.GoodRarity);
        Assert.Equal(new[] { 70d, 25d, 4.5d, 0.5d }, p.BadRarity);
        Assert.Equal(3, p.PityDays);
        Assert.Equal(1, p.PermNegativeCap);
        Assert.Empty(spy.Messages);
    }

    [Fact]
    public void Resolve_CustomBadZero_PureLotteryIsValid()
    {
        var spy = new WarnSpy();
        var custom = new CustomSettings { GoodPercent = 50, PlainPercent = 50, BadPercent = 0 };
        var p = DifficultyTable.Resolve(6, custom, spy.OnWarn);
        Assert.Equal(6, p.Tier);
        Assert.Equal(0.0, p.Bad);
        Assert.Empty(spy.Messages);
    }

    [Fact]
    public void Resolve_CustomGoodZero_IsValid()
    {
        var spy = new WarnSpy();
        var custom = new CustomSettings { GoodPercent = 0, PlainPercent = 40, BadPercent = 60 };
        var p = DifficultyTable.Resolve(6, custom, spy.OnWarn);
        Assert.Equal(6, p.Tier);
        Assert.Equal(0.0, p.Good);
        Assert.Empty(spy.Messages);
    }

    [Fact]
    public void Resolve_CustomNegativeComponent_FallsBackToTier3()
    {
        // 和=100 但含负分量：负概率无意义，按非法处理（防御性规则）
        var spy = new WarnSpy();
        var custom = new CustomSettings { GoodPercent = -5, PlainPercent = 55, BadPercent = 50 };
        var p = DifficultyTable.Resolve(6, custom, spy.OnWarn);
        Assert.Equal(3, p.Tier);
        Assert.Equal(0.45, p.Good);
        Assert.Single(spy.Messages);
    }

    [Fact]
    public void Resolve_CustomNull_FallsBackToTier3()
    {
        var spy = new WarnSpy();
        var p = DifficultyTable.Resolve(6, null, spy.OnWarn);
        Assert.Equal(3, p.Tier);
        Assert.Single(spy.Messages);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(99)]
    public void Resolve_UnknownTier_WarnsAndFallsBackToTier3(int tier)
    {
        var spy = new WarnSpy();
        var p = DifficultyTable.Resolve(tier, null, spy.OnWarn);
        Assert.Equal(3, p.Tier);
        Assert.Equal(0.45, p.Good);
        Assert.Equal(1.0, p.RewardMult);
        Assert.Single(spy.Messages);
    }

    [Fact]
    public void Resolve_ReturnsFreshArraysPerCall()
    {
        // 每次调用返回全新数组：调用方（Task 5+）改写字段不得污染表
        var a = DifficultyTable.Resolve(1, null, null);
        var b = DifficultyTable.Resolve(1, null, null);
        Assert.NotSame(a.GoodRarity, b.GoodRarity);
        Assert.NotSame(a.BadRarity, b.BadRarity);
        a.GoodRarity[0] = 0;
        var c = DifficultyTable.Resolve(1, null, null);
        Assert.NotEqual(0d, c.GoodRarity[0]);
    }
}
