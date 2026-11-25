using System;
using System.Collections.Generic;
using System.Linq;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>RewardMath 纯函数契约测试（Task 8 Step 1，先写后实现）：
/// ScaleCount（倍率取整+下限 1，spec §5.5）、DecideQueue（背包拒收→挂账，
/// **入队 spec=已缩放形状**——补发原样发、不二次缩放，2026-11-10 定案记测试）、
/// PickPermBuff（预设档同号/自定义档按倍率映射 spec §5.3）、PickSlump（行运低潮按损失倍率）、
/// PickRandomOne（候选池均匀随机×1，注入 rand 可种子化）。
/// 变体 Id 权威=BuffBlueprints.FortuneIds/SlumpIds（99285~99289/99290~99293，§6.3）。</summary>
public sealed class RewardScaleTests
{
    // ———————————————— ScaleCount：四舍五入 + 下限 1 ————————————————

    [Theory]
    [InlineData(4, 2.0, 8)]    // 8.0
    [InlineData(4, 0.5, 2)]    // 2.0
    [InlineData(1, 0.5, 1)]    // 0.5 → 四舍五入 1（下限同向）
    [InlineData(3, 0.3, 1)]    // 0.9 → 1
    [InlineData(5, 0.5, 3)]    // 2.5 → 四舍五入（AwayFromZero）=3，非银行家舍入 2
    [InlineData(2, 0.25, 1)]   // 0.5 → 1（下限：四舍五入后为 1）
    [InlineData(1, 0.1, 1)]    // 0.1 → 0 → 下限 1
    public void ScaleCount_RoundsAndFloorsAt1(int baseCount, double mult, int want)
    {
        Assert.Equal(want, RewardMath.ScaleCount(baseCount, mult));
    }

    // ———————————————— DecideQueue：拒收挂账 / 接受为空 ————————————————

    [Fact]
    public void DecideQueue_BagFull_QueuesPending()
    {
        var spec = new RewardSpec { Items = new() { new ItemGain { ItemId = 2101, Count = 8 } } };

        var pending = RewardMath.DecideQueue(bagAccepted: false, spec: spec, reason: "day=12 event=99205 背包拒收");

        Assert.NotNull(pending);
        Assert.Equal("day=12 event=99205 背包拒收", pending.Reason);
        Assert.Same(spec, pending.Spec);
    }

    [Fact]
    public void DecideQueue_BagAccepted_ReturnsNull()
    {
        var spec = new RewardSpec { Items = new() { new ItemGain { ItemId = 2101, Count = 8 } } };

        Assert.Null(RewardMath.DecideQueue(bagAccepted: true, spec: spec, reason: "day=12 event=99205 背包拒收"));
    }

    /// <summary>2026-11-10 定案钉（补发不二次缩放）：入队的是**已缩放 spec**
    /// （Items 数量已在发放时 ×倍率），补发（PendingFlusher）原样 Grant、RewardMult 按 1.0 走。
    /// DecideQueue 只透传不定改——本测试钉死"透传"这一半；"补发不缩放"由补发侧 RewardMult=1 落实。</summary>
    [Fact]
    public void DecideQueue_StoresScaledSpecAsIs_NoRescalingOnFlush()
    {
        // 发放侧已按档1（×2.0）缩放：4→8；拒收入队的 spec 数量就是 8（不是基准 4）
        var scaled = new RewardSpec { Items = new() { new ItemGain { ItemId = 2101, Count = RewardMath.ScaleCount(4, 2.0) } } };
        Assert.Equal(8, scaled.Items[0].Count);

        var pending = RewardMath.DecideQueue(false, scaled, "背包拒收");
        Assert.Equal(8, pending.Spec.Items[0].Count);
        // 补发侧对已缩放 spec 的再缩放=恒等（RewardMult=1.0）
        Assert.Equal(8, RewardMath.ScaleCount(pending.Spec.Items[0].Count, 1.0));
    }

    // ———————————————— PickPermBuff：预设档同号 / 自定义档按倍率 ————————————————

    [Theory]
    [InlineData(1, 99285)]     // 档1 幸运儿 → 命运眷顾Ⅰ
    [InlineData(2, 99286)]
    [InlineData(3, 99287)]
    [InlineData(4, 99288)]
    [InlineData(5, 99289)]     // 档5 厄运缠身 → Ⅴ
    public void PickPermBuff_TierN_MapsSameIndex(int tier, int want)
    {
        Assert.Equal(want, RewardMath.PickPermBuff(tier, rewardMult: 1.0));
        // 预设档不看倍率：同档任意倍率同变体
        Assert.Equal(want, RewardMath.PickPermBuff(tier, rewardMult: 0.1));
        Assert.Equal(want, RewardMath.PickPermBuff(tier, rewardMult: 5.0));
    }

    [Theory]
    [InlineData(2.0, 99285)]   // ≥1.75 → Ⅰ
    [InlineData(1.75, 99285)]  // 边界含 =
    [InlineData(1.74, 99286)]
    [InlineData(1.3, 99286)]   // ≥1.25 → Ⅱ
    [InlineData(1.25, 99286)]
    [InlineData(1.24, 99287)]
    [InlineData(0.9, 99287)]   // ≥0.875 → Ⅲ
    [InlineData(0.875, 99287)]
    [InlineData(0.874, 99288)]
    [InlineData(0.7, 99288)]   // ≥0.625 → Ⅳ
    [InlineData(0.625, 99288)]
    [InlineData(0.624, 99289)]
    [InlineData(0.5, 99289)]   // 否则 → Ⅴ
    public void PickPermBuff_CustomByMultiplier(double mult, int want)
    {
        Assert.Equal(want, RewardMath.PickPermBuff(6, mult));
    }

    [Fact]
    public void PickPermBuff_PresetTiersMatchFortuneIdsArray()
    {
        for (var tier = 1; tier <= 5; tier++)
            Assert.Equal(BuffBlueprints.FortuneIds[tier], RewardMath.PickPermBuff(tier, 1.0));
    }

    // ———————————————— PickSlump：行运低潮按损失倍率（仅自定义档用） ————————————————

    [Theory]
    [InlineData(2.0, 99293)]   // ≥1.75 → SlumpIds[5]
    [InlineData(1.75, 99293)]
    [InlineData(1.3, 99292)]   // ≥1.25 → [4]
    [InlineData(1.25, 99292)]
    [InlineData(0.8, 99291)]   // ≥0.75 → [3]
    [InlineData(0.75, 99291)]
    [InlineData(0.5, 99290)]   // 否则 → [2]
    public void PickSlump_ByLossMultiplier(double lossMult, int want)
    {
        Assert.Equal(want, RewardMath.PickSlump(lossMult));
        Assert.Equal(BuffBlueprints.SlumpIds[2], RewardMath.PickSlump(0.1));   // 兜底=Ⅱ（最轻）
    }

    // ———————————————— 补发后合并：磁盘字段赢，只换 PendingRewards ————————————————

    /// <summary>fix round 1 钉（丢更新防回归）：ChannelReady 等待窗口（最长 ~60s）内其他写入者
    /// （DailyFlow 每日记账 / 气泡兜底挂账）落盘的新字段——合并只替换 PendingRewards，
    /// 其余字段一律磁盘为准；窗口期新增挂账条目随重发一同发放（发放列表=重载的最新列表，Game 层
    /// PendingFlusher 职责，本测试钉合并语义+输入不可变）。</summary>
    [Fact]
    public void ApplyFlushResult_ReplacesOnlyPendingRewards_DiskFieldsWin()
    {
        // 发现货点的陈旧快照（等待窗口前）——只作对照，绝不参与合并
        var stale = new RollState { LastTriggeredDay = 12, BadStreak = 3, DisasterCount = 1 };
        // 窗口期内磁盘被推进：日翻+清连霉+永久名额+昨日对照+灾厄计数，且新增一条窗口期挂账
        var windowAdded = new PendingReward { Spec = new RewardSpec(), Reason = "day=12 event=99209 通道不可达" };
        var latest = new RollState
        {
            LastTriggeredDay = 13,
            BadStreak = 0,
            PermNegativeUsed = 1,
            YesterdayEventId = EventCatalog.Ids.G1,
            DisasterCount = 2,
            PendingRewards = new List<PendingReward>
            {
                new() { Spec = new RewardSpec(), Reason = "day=12 event=99205 背包拒收" },
                windowAdded,
            },
        };
        var remaining = new List<PendingReward>();   // 重载列表（含窗口期新增）全部补发成功

        var merged = RewardMath.ApplyFlushResult(latest, remaining);

        // 其余字段=磁盘最新值（不被陈旧快照回滚：12/3/1 均不得出现）
        Assert.Equal(13, merged.LastTriggeredDay);
        Assert.Equal(0, merged.BadStreak);
        Assert.Equal(1, merged.PermNegativeUsed);
        Assert.Equal(EventCatalog.Ids.G1, merged.YesterdayEventId);
        Assert.Equal(2, merged.DisasterCount);
        // 唯一被替换的字段：挂账列表=补发结果
        Assert.Same(remaining, merged.PendingRewards);

        // 输入不可变：磁盘快照与补发结果未被污染（调用方重载对象不被就地改写）
        Assert.Equal(2, latest.PendingRewards.Count);
        Assert.Empty(remaining);
        Assert.Equal(12, stale.LastTriggeredDay);
    }

    [Fact]
    public void ApplyFlushResult_NullRemaining_NormalizedToEmptyList()
    {
        var merged = RewardMath.ApplyFlushResult(new RollState { LastTriggeredDay = 5 }, null);
        Assert.NotNull(merged.PendingRewards);
        Assert.Empty(merged.PendingRewards);
        Assert.Equal(5, merged.LastTriggeredDay);
    }

    // ———————————————— PickRandomOne：候选池均匀随机×1 ————————————————

    [Fact]
    public void PickRandomOne_SeededRandom_PicksWithinPool()
    {
        var pool = new List<ItemGain>
        {
            new() { ItemId = 14057, Count = 1 },
            new() { ItemId = 14059, Count = 1 },
            new() { ItemId = 14062, Count = 1 },
        };

        var pick = RewardMath.PickRandomOne(pool, new Random(42));

        Assert.NotNull(pick);
        Assert.Contains(pick, pool);   // 引用相等（池内原条目）
        // 同种子可复现（注入 rand 的可测性）
        Assert.Equal(pick.ItemId, RewardMath.PickRandomOne(pool, new Random(42)).ItemId);
    }

    [Fact]
    public void PickRandomOne_SingleCandidate_AlwaysThatOne()
    {
        var pool = new List<ItemGain> { new() { ItemId = 3034, Count = 1 } };
        for (var i = 0; i < 10; i++)
            Assert.Equal(3034, RewardMath.PickRandomOne(pool, new Random(i)).ItemId);
    }

    /// <summary>均匀性冒烟：3 候选 × 600 抽（200 个种子×3 次连续抽取），每候选都命中
    /// （弱断言防flaky：不钉分布比例，只钉全覆盖）。</summary>
    [Fact]
    public void PickRandomOne_AllCandidatesReachable()
    {
        var pool = new List<ItemGain>
        {
            new() { ItemId = 11033, Count = 1 },
            new() { ItemId = 11038, Count = 1 },
            new() { ItemId = 11032, Count = 1 },
            new() { ItemId = 11035, Count = 1 },
        };
        var hit = new HashSet<int>();
        for (var seed = 0; seed < 500; seed++)
        {
            var rand = new Random(seed);
            for (var i = 0; i < 4; i++)
                hit.Add(RewardMath.PickRandomOne(pool, rand).ItemId);
        }
        Assert.Equal(pool.Select(g => g.ItemId).ToHashSet(), hit);
    }
}
