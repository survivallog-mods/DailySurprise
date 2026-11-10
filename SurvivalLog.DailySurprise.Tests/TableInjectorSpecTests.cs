using System.Collections.Generic;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>RewardSet 编码语义测试（Task 2 首验取证；Task 4 起 spike 专用类型已删——
/// SpikeEvent/SpikeRowTemplate 随 EventCatalog 接管数据而移除，Game/TableInjector 改读
/// EventCatalog G1，其样式常量内联进 Game 层（测试上下文无 interop DLL，不可见）。
/// G1 数据本身已由 EventCatalogTests 钉死；本文件只锁 RewardEncoding 的表编码契约，
/// 原生行 6420 的展开语义由本套件钉死——上游表若变，改实现必须连测试一起改（评审可见）。</summary>
public sealed class TableInjectorSpecTests
{
    [Fact]
    public void RewardEncoding_ItemChangeRepeatsItemIdPerCount()
    {
        // gamekb Config_RewardSet 行 6420 实证语义：itemId 逐次重复=件数（非 [id,count] 对）
        var spec = new RewardSpec
        {
            Items = new() { new ItemGain { ItemId = 2101, Count = 4 } },   // G1 首行奖励同款
        };
        Assert.Equal(new[] { 2101, 2101, 2101, 2101 }, RewardEncoding.ItemChangeOf(spec));
    }

    [Fact]
    public void RewardEncoding_NullSafeAndSkipsNonPositiveCounts()
    {
        Assert.Empty(RewardEncoding.ItemChangeOf(null));
        Assert.Empty(RewardEncoding.ItemChangeOf(new RewardSpec { Items = new() }));
        var mixed = new RewardSpec
        {
            Items = new()
            {
                new ItemGain { ItemId = 2101, Count = 2 },
                new ItemGain { ItemId = 2209, Count = 0 },
                new ItemGain { ItemId = 20106, Count = 1 },
            },
        };
        Assert.Equal(new[] { 2101, 2101, 20106 }, RewardEncoding.ItemChangeOf(mixed));
    }

    [Fact]
    public void RewardEncoding_MatchesNativeRow6420Semantics()
    {
        // 原生行 6420 ItemChange=[2107,2107,2107,2209,2209,...] 的展开规律反推：同 id 连续重复 N 次=N 件
        var spec = new RewardSpec
        {
            Items = new() { new ItemGain { ItemId = 2107, Count = 3 }, new ItemGain { ItemId = 2209, Count = 2 } },
        };
        Assert.Equal(new[] { 2107, 2107, 2107, 2209, 2209 }, RewardEncoding.ItemChangeOf(spec));
    }

    [Fact]
    public void RewardEncoding_G1Reward_ExpandsToBiscuitsAndCookies()
    {
        // TableInjector（Game 层）注入的 RewardSet 99250 数据源=EventCatalog G1：90压缩饼干×4+饼干×6
        var g1 = new List<EventDef>();
        foreach (var e in EventCatalog.Events)
            if (e.Id == EventCatalog.Ids.G1)
                g1.Add(e);
        var g1Event = Assert.Single(g1);
        Assert.Equal(
            new[] { 2101, 2101, 2101, 2101, 1, 1, 1, 1, 1, 1 },
            RewardEncoding.ItemChangeOf(g1Event.Reward));
    }
}
