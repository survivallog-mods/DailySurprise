using System.Linq;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>首验 spike 数据装配测试（Task 2 brief Step 1"测试断言字段装配正确"）。
/// Game 层（TableInjector/SpikeForce/DsTicker）依赖 BepInEx/interop DLL，测试上下文没有——
/// 这里锁的是喂给注入器的全部纯数据：EventDef 装配 + 注入行样式常量（逐字照抄原生行的取证值）
/// + RewardSet 编码语义。样式常量与 gamekb Config_Event.json 原生行（10011/103）的逐字一致性
/// 由本套件把抄录值钉死——上游表若变，改常量必须连测试一起改（评审可见）。</summary>
public sealed class TableInjectorSpecTests
{
    [Fact]
    public void SpikeEvent_AssemblyMatchesBrief()
    {
        var e = SpikeEvent.Instance;
        Assert.Equal(99200, e.Id);
        Assert.Equal("spike", e.Key);
        Assert.Equal("这是一次测试：天上掉下来一箱压缩饼干。", e.Dialogue);
        Assert.NotNull(e.Title);
        Assert.Equal(LuckKind.Good, e.Kind);
        Assert.Equal(Eligibility.None, e.Elig);
        Assert.True(e.Weight > 0);
    }

    [Fact]
    public void SpikeEvent_RewardIsFourBiscuits_WithCleanDefaults()
    {
        var r = SpikeEvent.Instance.Reward;
        Assert.NotNull(r);
        var item = Assert.Single(r.Items);
        Assert.Equal(2101, item.ItemId);      // 90压缩饼干（gamekb Config_Item 2101）
        Assert.Equal(4, item.Count);
        Assert.Empty(r.Buffs);
        Assert.Empty(r.Attrs);
        Assert.Empty(r.Choices);
        Assert.Equal(SpecialOp.None, r.Op);
        Assert.Equal(0d, r.OpParam);
        Assert.False(r.PermBuffByTier);
    }

    [Fact]
    public void SpikeEvent_IconIsNativePath()
    {
        // 任务口径：Icon 抄原生值（原生资源必在），不用 eg_arrow_tower.png（依赖 EndlessGuard 已装）
        var icon = SpikeEvent.Instance.Icon;
        Assert.StartsWith("Assets/RuntimeAssets/Texture/", icon);
        Assert.Equal(SpikeRowTemplate.IconA, icon);
        Assert.DoesNotContain("eg_arrow_tower", icon);
        Assert.DoesNotContain("../../Res", icon);
    }

    [Fact]
    public void StyleA_CopiedVerbatimFromNativeRow10011()
    {
        // Config_Event.json 原生行 10011「暴雨侵袭-用毛巾堵漏」：Type=2/ShowType=1/PopUpType=1/KeepTime=0
        Assert.Equal(10011, SpikeRowTemplate.StyleSourceRowA);
        Assert.Equal(2, SpikeRowTemplate.TypeA);
        Assert.Equal(1, SpikeRowTemplate.ShowTypeA);
        Assert.Equal(1, SpikeRowTemplate.PopUpTypeA);
        Assert.Equal(0, SpikeRowTemplate.KeepTimeA);
        Assert.Equal("Assets/RuntimeAssets/Texture/EventNew/022", SpikeRowTemplate.IconA);
        Assert.Equal("我知道了", SpikeRowTemplate.OkDesA);
        Assert.Equal(2, SpikeRowTemplate.DemoTwoModeA);
    }

    [Fact]
    public void StyleB_CopiedVerbatimFromNativeRow103()
    {
        // Config_Event.json 原生行 103「事件3」：Type=1/PopUpType=3/Countdown=60，Default=末选项
        Assert.Equal(103, SpikeRowTemplate.StyleSourceRowB);
        Assert.Equal(1, SpikeRowTemplate.TypeB);
        Assert.Equal(1, SpikeRowTemplate.ShowTypeB);
        Assert.Equal(3, SpikeRowTemplate.PopUpTypeB);
        Assert.Equal(0, SpikeRowTemplate.KeepTimeB);
        Assert.Equal(60, SpikeRowTemplate.CountdownB);
        Assert.Equal(3, SpikeRowTemplate.DemoTwoModeB);
        Assert.Equal("Assets/RuntimeAssets/Texture/Literature/UI_Item_Icon_Liter_M_1490200101", SpikeRowTemplate.IconB);
    }

    [Fact]
    public void StyleB_ChoiceFieldsAreTwoLabelsPointingAtNativeChildren()
    {
        // brief Step 4 原文：ChoseName_Local=["拿走","算了"] 两选项
        Assert.Equal(new[] { "拿走", "算了" }, SpikeRowTemplate.ChoseLabels);
        Assert.Equal(2, SpikeRowTemplate.ChoseIds.Length);
        Assert.Equal(SpikeRowTemplate.ChoseLabels.Length, SpikeRowTemplate.ChoseIds.Length);  // 按钮/子事件一一对应
        Assert.All(SpikeRowTemplate.ChoseIds, id => Assert.InRange(id, 1, 90001));            // 原生行段
        Assert.Equal(SpikeRowTemplate.DefaultChoseId, SpikeRowTemplate.ChoseIds[^1]);        // 103 同构：Default=末选项
    }

    [Fact]
    public void Ids_AreInPlannedSegmentAndDistinct()
    {
        // spec §7.3：自注入段 99200~99399（Event 99200+、RewardSet 99250+）
        Assert.InRange(SpikeRowTemplate.EventId, 99200, 99399);
        Assert.InRange(SpikeRowTemplate.RewardSetId, 99250, 99399);
        Assert.Equal(99200, SpikeRowTemplate.EventId);
        Assert.Equal(99250, SpikeRowTemplate.RewardSetId);
        Assert.NotEqual(SpikeRowTemplate.EventId, SpikeRowTemplate.RewardSetId);
    }

    [Fact]
    public void RewardEncoding_ItemChangeRepeatsItemIdPerCount()
    {
        // gamekb Config_RewardSet 行 6420 实证语义：itemId 逐次重复=件数（非 [id,count] 对）
        var flat = RewardEncoding.ItemChangeOf(SpikeEvent.Instance.Reward);
        Assert.Equal(new[] { 2101, 2101, 2101, 2101 }, flat);
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
}
