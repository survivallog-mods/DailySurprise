using System.Collections.Generic;
using System.Linq;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>RegistrarPlan 纯函数契约测试（Task 17 Step 1，先写后实现）。
/// 注册决策层=50 行 Id 清单/去重决策/DynamicEventConfig 字段映射（镜像原生 StartBattle_AfterAgent
/// 构建循环——gamekb 2026-11-12_26615062 反汇编定案，见 ConfigRegistrar.cs 头注）/随机池免疫不变量。
/// IL2CPP 写入层（Game/Trigger/ConfigRegistrar.cs，测试上下文不可见）只做"shape→对象属性"搬运。</summary>
public sealed class ConfigRegistrarTests
{
    // ———————————————— Id 清单 ————————————————

    [Fact]
    public void EventIds_FiftyContiguousUniqueIds()
    {
        var ids = RegistrarPlan.EventIds();

        Assert.Equal(50, ids.Count);
        Assert.Equal(45 + 5, EventCatalog.Events.Count + EventCatalog.SonEvents.Count);
        Assert.Equal(EventCatalog.FirstEventId, ids.Min());
        Assert.Equal(EventCatalog.LastSonEventId, ids.Max());
        Assert.Equal(50, ids.Distinct().Count());
        Assert.Empty(Enumerable.Range(99200, 50).Except(ids));   // 99200~99249 连续无缝
    }

    // ———————————————— 去重/缺行决策 ————————————————

    public static TheoryData<string, HashSet<int>, HashSet<int>, int, int, int> DecideCases => new()
    {
        // name, inConfigs 集, rowInTable 集, 期望 toAdd, already, rowMissing
        { "全已在快照：全部跳过（换档/模式切换幂等）", new HashSet<int>(Enumerable.Range(99200, 50)), new HashSet<int>(Enumerable.Range(99200, 50)), 0, 50, 0 },
        { "全不在快照且行在表：全部待注册（修复后的常态）", new HashSet<int>(), new HashSet<int>(Enumerable.Range(99200, 50)), 50, 0, 0 },
        { "行缺（时序：表未注入）：记 rowMissing 不注册", new HashSet<int>(), new HashSet<int>(), 0, 0, 50 },
        { "混合：部分已在部分缺行", new HashSet<int> { 99200 }, new HashSet<int>(Enumerable.Range(99200, 50).Where(i => i != 99205)), 48, 1, 1 },
    };

    [Theory]
    [MemberData(nameof(DecideCases))]
    public void Decide_TableDriven(string name, HashSet<int> inConfigs, HashSet<int> rowInTable,
        int wantAdd, int wantAlready, int wantMissing)
    {
        var d = RegistrarPlan.Decide(RegistrarPlan.EventIds(),
            id => inConfigs.Contains(id), id => rowInTable.Contains(id));

        Assert.Equal(wantAdd, d.ToAdd.Count);
        Assert.Equal(wantAlready, d.AlreadyPresent.Count);
        Assert.Equal(wantMissing, d.RowMissing.Count);
        // 决策互斥且覆盖全集
        Assert.Equal(50, d.ToAdd.Count + d.AlreadyPresent.Count + d.RowMissing.Count);
        if (wantMissing > 0) Assert.Contains(99205, d.RowMissing);
    }

    // ———————————————— DynamicEventConfig 字段映射（镜像原生构建循环） ————————————————

    private static RegistrarPlan.RowShape ShapeOf(Dictionary<string, object> row) => new()
    {
        Id = (int)row["ID"],
        Type = (int)row["Type"],
        TimeMin = (int)row["TimeMin"],
        TimeMax = (int)row["TimeMax"],
        IsSon = (bool)row["IsSon"],
        ChoseNameCount = ((List<string>)row["ChoseName"]).Count,
        TaskTime = (int)row["TaskTime"],
        TaskConditionSetId = (int)row["TaskConditionSetId"],
        GroupKey = (string)row["GroupKey"],
        GroupDayCd = (int)row["GroupDayCD"],
        DayRate = (float)row["DayRate"],
    };

    private static EventDef ById(int id) => Assert.Single(EventCatalog.Events, e => e.Id == id);

    [Fact]
    public void MapConfig_DirectEvent_MirrorsNativeLoop()
    {
        var shape = RegistrarPlan.MapConfig(ShapeOf(InjectorMapping.EventRow(ById(EventCatalog.Ids.G1))));

        Assert.Equal(99205, shape.EventId);
        Assert.Equal(2, shape.EventType);            // Type=2 → DynamicEventConfigType.Result
        Assert.Equal(1, shape.TimeMin);
        Assert.Equal(24, shape.TimeMax);
        Assert.False(shape.IsChild);
        Assert.False(shape.IsChose);                 // ChoseName 空 → IsChose=false（原生按 ChoseName 计数）
        Assert.Equal(0, shape.TaskTime);
        Assert.Equal(0, shape.TaskConditionSetId);
        Assert.Equal("", shape.GroupKey);
        Assert.Equal(0, shape.GroupDayCd);
    }

    [Fact]
    public void MapConfig_ChoiceEvent_IsChoseFromChoseNameCount()
    {
        var shape = RegistrarPlan.MapConfig(ShapeOf(InjectorMapping.EventRow(ById(EventCatalog.Ids.L1))));

        Assert.Equal(1, shape.EventType);            // Type=1 → DynamicEventConfigType.Choice
        Assert.True(shape.IsChose);                  // L1 三选项：ChoseName.Count=3
        Assert.False(shape.IsChild);
    }

    [Fact]
    public void MapConfig_SonEvent_IsChildTrue()
    {
        var son = Assert.Single(EventCatalog.SonEvents, s => s.Id == 99245);
        var shape = RegistrarPlan.MapConfig(ShapeOf(InjectorMapping.SonEventRow(son)));

        Assert.Equal(99245, shape.EventId);
        Assert.Equal(2, shape.EventType);
        Assert.True(shape.IsChild);                  // IsSon=true → IsChild
        Assert.False(shape.IsChose);                 // 子事件行 ChoseName 空
    }

    [Fact]
    public void MapConfig_AllCatalogRows_ConsistentWithRowStyle()
    {
        foreach (var e in EventCatalog.Events)
        {
            var row = InjectorMapping.EventRow(e);
            var shape = RegistrarPlan.MapConfig(ShapeOf(row));
            var choice = e.Reward is { Choices.Count: > 0 };
            Assert.Equal(choice ? 1 : 2, shape.EventType);
            Assert.Equal(choice, shape.IsChose);
            Assert.False(shape.IsChild);
        }
        foreach (var s in EventCatalog.SonEvents)
        {
            var shape = RegistrarPlan.MapConfig(ShapeOf(InjectorMapping.SonEventRow(s)));
            Assert.Equal(2, shape.EventType);
            Assert.True(shape.IsChild);
            Assert.False(shape.IsChose);
        }
    }

    // ———————————————— 原生随机池免疫不变量 ————————————————

    [Fact]
    public void AllRows_DayRateZero_NativeRollerImmune()
    {
        // 注册进 Configs 后原生 TriggerRandomEvent 逐时扫描全部非子事件；唯一免疫源=
        // ProbabilityTestCondition(DayRate)：p==0 时幸运加成显式跳过、0>RandomFloat 恒 false
        //（反汇编 0x27360E0）。任何行 DayRate≠0 都会让原生随机池开始自发 roll（无奖励弹卡）。
        foreach (var e in EventCatalog.Events)
            Assert.True(RegistrarPlan.IsRollerImmune((float)InjectorMapping.EventRow(e)["DayRate"]),
                "事件 " + e.Id + " DayRate≠0：进入原生随机池");
        foreach (var s in EventCatalog.SonEvents)
            Assert.True(RegistrarPlan.IsRollerImmune((float)InjectorMapping.SonEventRow(s)["DayRate"]),
                "子事件 " + s.Id + " DayRate≠0");
    }

    // ———————————————— 缺席原因诊断 ————————————————

    public static TheoryData<bool, bool, bool, string> AbsenceCases => new()
    {
        // rowInTable, blockedDemoTwo, blockedEndless, 期望关键词
        { false, false, false, "时序：行未注入" },
        { true, true, false, "过滤：DemoTwoMode" },
        { true, false, true, "过滤：EndlessMode" },
        { true, true, true, "过滤：DemoTwoMode+EndlessMode" },
        { true, false, false, "时序：快照构建先于注入" },
    };

    [Theory]
    [MemberData(nameof(AbsenceCases))]
    public void AbsenceReason_TableDriven(bool rowInTable, bool blockedDemoTwo, bool blockedEndless, string wantPart)
    {
        var reason = RegistrarPlan.AbsenceReason(rowInTable, blockedDemoTwo, blockedEndless);
        Assert.Contains(wantPart, reason);
    }
}
