using System;
using System.Collections.Generic;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>惩罚侧纯函数契约测试（Task 9，先写后实现）：
/// ScaleLoss（损失量缩放=×lossMult 后四舍五入到 0.1，AwayFromZero，spec §5.5 损失轴）、
/// PlanPermAttrLoss（D1 代价分流：AllowPermanentNegative=false → 行运低潮 PickSlump 变体；
/// true → 五维上限属性池（AttrName 101~105，dump.cs:57466）种子化随机 + 扣量 −round(10×mult)）。
/// StealParts 数量口径=既有 ScaleCount（复验钉 10×0.75=8）；耐久区间解码往返已有
/// EventCatalogTests.DurabilityRangeCodec_RoundTrips 在役（复验不重写）。
/// 变体 Id 权威=BuffBlueprints.SlumpIds[2..5]=99290~99293（§6.3）。</summary>
public sealed class PunishmentMathTests
{
    // ———————————————— ScaleLoss：×lossMult 后四舍五入到 0.1（AwayFromZero） ————————————————

    [Theory]
    [InlineData(15, 1.0, 15)]     // M1 基准 15% × 档 3 → 整数
    [InlineData(15, 1.75, 26.3)]  // 26.25 → AwayFromZero → 26.3（非银行家舍入 26.2）
    [InlineData(5, 2.5, 12.5)]    // M3 基准 5% × 档 5 → 恰一位小数不动
    [InlineData(25, 1.75, 43.8)]  // H2 基准 25% × 档 4：43.75 → 43.8
    [InlineData(40, 0.75, 30)]    // D2 基准 40% × 档 2 → 整数
    [InlineData(5, 0.5, 2.5)]     // 档 1 损失减半
    [InlineData(15, 0.1, 1.5)]    // 自定义下限 0.1
    [InlineData(10, 1.3, 13)]     // 13.0 归整
    public void ScaleLoss_MultipliesAndRoundsToTenth(double baseValue, double lossMult, double want)
    {
        Assert.Equal(want, RewardMath.ScaleLoss(baseValue, lossMult));
    }

    [Theory]
    [InlineData(0, 1.0)]     // 基准 0=数据错误 → 0（不产生负损失）
    [InlineData(-5, 1.0)]    // 负基准防御
    [InlineData(15, 0)]      // 倍率 0（PendingFlusher 的补发档 LossMult 缺省）防御
    [InlineData(15, -1.0)]   // 负倍率=方向反转，安全向 0
    public void ScaleLoss_NonPositiveInputs_DefendToZero(double baseValue, double lossMult)
    {
        Assert.Equal(0d, RewardMath.ScaleLoss(baseValue, lossMult));
    }

    // ———————————————— PlanPermAttrLoss：AllowPermanentNegative 分流（spec §8.9 D1） ————————————————

    [Theory]
    [InlineData(2.0, 99293)]   // ≥1.75 → SlumpIds[5]=Ⅴ
    [InlineData(1.75, 99293)]
    [InlineData(1.3, 99292)]   // ≥1.25 → [4]=Ⅳ
    [InlineData(1.25, 99292)]
    [InlineData(0.8, 99291)]   // ≥0.75 → [3]=Ⅲ
    [InlineData(0.75, 99291)]
    [InlineData(0.5, 99290)]   // 否则 → [2]=Ⅱ（最轻）
    public void PlanPermAttrLoss_Disabled_RoutesToSlumpBuff(double lossMult, int slumpId)
    {
        var plan = RewardMath.PlanPermAttrLoss(allowPermanentNegative: false, lossMult: lossMult, rand: new Random(1));

        Assert.False(plan.UseAttrLoss);          // 不扣属性
        Assert.Equal(0, plan.AttrId);            // 属性字段不携带
        Assert.Equal(0, plan.Loss);
        Assert.Equal(slumpId, plan.SlumpBuffId); // 行运低潮变体=PickSlump 同源
        Assert.Equal(slumpId, RewardMath.PickSlump(lossMult));
    }

    /// <summary>自然触发链里 AllowPermanentNegative=false 由 LuckEngine 替补（D1→D2）先行兜住；
    /// 本分流兜的是 DebugForceEventId 强制 D1 直达执行器的路径（实机清单最后一项）。
    /// 禁用档 rand 不被消费（可数随机）：同种子下 enabled 的随机序不受 disabled 判定影响。</summary>
    [Fact]
    public void PlanPermAttrLoss_Disabled_DoesNotConsumeRandom()
    {
        var rand = new Random(42);
        rand.Next(1000);   // 两个 rand 同步消耗一次，站在同一位置
        var rand2 = new Random(42);
        rand2.Next(1000);

        RewardMath.PlanPermAttrLoss(false, 1.0, rand2);   // 分流判定不吃随机数

        Assert.Equal(rand.Next(1000), rand2.Next(1000));  // 未消费 → 后续序列仍一致
        Assert.Equal(rand.Next(1000), rand2.Next(1000));
    }

    [Fact]
    public void PlanPermAttrLoss_Enabled_SeededRandHitsPoolMemberReproducibly()
    {
        var pool = new HashSet<int>(RewardMath.PermAttrPool);
        foreach (var seed in new[] { 0, 1, 7, 42, 2026 })
        {
            var plan = RewardMath.PlanPermAttrLoss(true, 1.0, new Random(seed));
            Assert.True(plan.UseAttrLoss);       // 扣属性路径
            Assert.Contains(plan.AttrId, pool);  // 命中五维上限之一
            Assert.Equal(0, plan.SlumpBuffId);   // 替补字段不携带
            // 同种子可复现（rand 注入的可测性）
            Assert.Equal(plan.AttrId, RewardMath.PlanPermAttrLoss(true, 1.0, new Random(seed)).AttrId);
        }
    }

    [Theory]
    [InlineData(1.0, -10)]    // 档 3 基准
    [InlineData(1.75, -18)]   // 17.5 AwayFromZero → 18（档 4）
    [InlineData(2.5, -25)]    // 档 5
    [InlineData(0.75, -8)]    // 7.5 → 8（档 2）
    [InlineData(0.5, -5)]     // 档 1
    [InlineData(0.1, -1)]     // 自定义下限：至少扣 1
    public void PlanPermAttrLoss_Enabled_LossIsNegativeRoundedAwayFromZero(double lossMult, int want)
    {
        var plan = RewardMath.PlanPermAttrLoss(true, lossMult, new Random(7));
        Assert.Equal(want, plan.Loss);
    }

    /// <summary>属性池=AttrName 上限系五维（dump.cs:57466：MaxSatiety=101…MaxVitality=105），
    /// 逐字钉死——池扩员/错位即刻红。</summary>
    [Fact]
    public void PermAttrPool_ExactlyFiveMaxAttrs()
    {
        Assert.Equal(new[] { 101, 102, 103, 104, 105 }, RewardMath.PermAttrPool);
    }

    // ———————————————— StealParts 数量口径：既有 ScaleCount 复验（H3 基准 10×lossMult） ————————————————

    [Theory]
    [InlineData(0.75, 8)]    // 7.5 AwayFromZero=8（档 2）
    [InlineData(1.0, 10)]    // 档 3 基准
    [InlineData(1.75, 18)]   // 17.5 → 18（档 4）
    [InlineData(2.5, 25)]    // 档 5
    [InlineData(0.1, 1)]     // 下限 1：贼不走空
    public void StealParts_Base10_ScaleCountRoundsAwayFromZero(double lossMult, int want)
    {
        Assert.Equal(want, RewardMath.ScaleCount(10, lossMult));
    }
}
