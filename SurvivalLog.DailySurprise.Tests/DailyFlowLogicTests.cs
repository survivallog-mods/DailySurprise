using System.Collections.Generic;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>DailyFlow 纯函数规格测试（Task 7，spec §5.1 主流程 + §5.4 防劝退 + §8.1 平淡静音）：
/// ShouldTrigger（同日幂等/新日/灾前门控）、Presentation（平淡档 ShowPlainPopup=false 静音为头顶气泡）、
/// NextState（BadStreak 记账/保底清零/永久名额/替补不占/昨日对照/灾厄统计）。
/// 事件事实用真实 EventCatalog（已源链接）——DisasterCount 的 Rarity==3 判定不造重复数据。</summary>
public sealed class DailyFlowLogicTests
{
    private static RollState St(int lastDay = 0, int badStreak = 0, int permUsed = 0, int yesterday = 0, int disasters = 0) => new()
    {
        LastTriggeredDay = lastDay,
        BadStreak = badStreak,
        PermNegativeUsed = permUsed,
        YesterdayEventId = yesterday,
        DisasterCount = disasters,
    };

    // —— ShouldTrigger：同日幂等 + 灾后门控 ——

    [Fact]
    public void ShouldTrigger_SameDayTwice_False()
    {
        var s = St(lastDay: 12);

        Assert.False(DailyFlow.ShouldTrigger(s, day: 12, postDisaster: true));
    }

    [Fact]
    public void ShouldTrigger_NewDay_True()
    {
        var s = St(lastDay: 12);

        Assert.True(DailyFlow.ShouldTrigger(s, day: 13, postDisaster: true));
    }

    [Fact]
    public void ShouldTrigger_PreDisaster_False()
    {
        var s = St(lastDay: 0);   // 全新档：从未触发过

        Assert.False(DailyFlow.ShouldTrigger(s, day: 1, postDisaster: false));
    }

    // —— Presentation：平淡静音表 ——

    [Theory]
    [InlineData(LuckKind.Plain, false, PresentationKind.HeadTalk)]   // 平淡+静音 → 头顶气泡（记账照常）
    [InlineData(LuckKind.Plain, true, PresentationKind.EventCard)]   // 平淡+弹窗 → 事件卡
    [InlineData(LuckKind.Good, false, PresentationKind.EventCard)]   // 好运任意开关 → 事件卡
    [InlineData(LuckKind.Good, true, PresentationKind.EventCard)]
    [InlineData(LuckKind.Bad, false, PresentationKind.EventCard)]    // 霉运任意开关 → 事件卡
    [InlineData(LuckKind.Bad, true, PresentationKind.EventCard)]
    public void Presentation_TableDriven(LuckKind kind, bool showPlainPopup, PresentationKind want)
    {
        Assert.Equal(want, DailyFlow.Presentation(kind, showPlainPopup));
    }

    // —— NextState：记账规则 ——

    [Fact]
    public void NextState_GoodDay_ClearsBadStreak()
    {
        var s = St(badStreak: 3);
        var o = new RollOutcome { Kind = LuckKind.Good, EventId = EventCatalog.Ids.G1 };

        var n = DailyFlow.NextState(s, o, day: 20, permGranted: false);

        Assert.Equal(0, n.BadStreak);
    }

    [Fact]
    public void NextState_PlainDay_ClearsBadStreak()
    {
        var s = St(badStreak: 2);
        var o = new RollOutcome { Kind = LuckKind.Plain, EventId = EventCatalog.Ids.N1 };

        var n = DailyFlow.NextState(s, o, day: 20, permGranted: false);

        Assert.Equal(0, n.BadStreak);
    }

    [Fact]
    public void NextState_BadDay_IncrementsBadStreak()
    {
        var s = St(badStreak: 2);
        var o = new RollOutcome { Kind = LuckKind.Bad, EventId = EventCatalog.Ids.B1 };

        var n = DailyFlow.NextState(s, o, day: 20, permGranted: false);

        Assert.Equal(3, n.BadStreak);
    }

    [Fact]
    public void NextState_PityForcedGood_ClearsBadStreak()
    {
        var s = St(badStreak: 3);   // 达保底天数被强制好运
        var o = new RollOutcome { Kind = LuckKind.Good, EventId = EventCatalog.Ids.G1, PityUsed = true };

        var n = DailyFlow.NextState(s, o, day: 20, permGranted: false);

        Assert.Equal(0, n.BadStreak);
    }

    [Fact]
    public void NextState_PermGranted_IncrementsPermNegativeUsed()
    {
        var s = St(permUsed: 0);
        var o = new RollOutcome { Kind = LuckKind.Bad, EventId = EventCatalog.Ids.H1 };   // H1 占永久名额

        var n = DailyFlow.NextState(s, o, day: 20, permGranted: true);

        Assert.Equal(1, n.PermNegativeUsed);
    }

    [Fact]
    public void NextState_Substituted_DoesNotIncrementPermNegativeUsed()
    {
        var s = St(permUsed: 1);   // 已达上限 → D1 被替补为 D2（非永久）
        var o = new RollOutcome { Kind = LuckKind.Bad, EventId = EventCatalog.Ids.D2, Substituted = true };

        var n = DailyFlow.NextState(s, o, day: 20, permGranted: false);

        Assert.Equal(1, n.PermNegativeUsed);
    }

    [Fact]
    public void NextState_RecordsYesterdayEventAndTriggeredDay()
    {
        var s = St(lastDay: 19, yesterday: EventCatalog.Ids.B2);
        var o = new RollOutcome { Kind = LuckKind.Good, EventId = EventCatalog.Ids.R3 };

        var n = DailyFlow.NextState(s, o, day: 20, permGranted: false);

        Assert.Equal(EventCatalog.Ids.R3, n.YesterdayEventId);
        Assert.Equal(20, n.LastTriggeredDay);
    }

    [Fact]
    public void NextState_DisasterHit_IncrementsDisasterCount()
    {
        var s = St(disasters: 4);
        var o = new RollOutcome { Kind = LuckKind.Bad, EventId = EventCatalog.Ids.D2 };   // D2 雷击=霉运灾厄层（Rarity 3）

        var n = DailyFlow.NextState(s, o, day: 20, permGranted: false);

        Assert.Equal(5, n.DisasterCount);
    }

    [Fact]
    public void NextState_NonDisasterBad_DoesNotIncrementDisasterCount()
    {
        var s = St(disasters: 4);
        var o = new RollOutcome { Kind = LuckKind.Bad, EventId = EventCatalog.Ids.M2 };   // 中度霉运非灾厄

        var n = DailyFlow.NextState(s, o, day: 20, permGranted: false);

        Assert.Equal(4, n.DisasterCount);
    }

    [Fact]
    public void NextState_MissedRoll_StillRecordsTriggeredDay()
    {
        var s = St(lastDay: 19);   // EventId=0 落空：无事件可展示，但当天已消费（幂等记账）
        var o = new RollOutcome { Kind = LuckKind.Good, EventId = 0 };

        var n = DailyFlow.NextState(s, o, day: 20, permGranted: false);

        Assert.Equal(20, n.LastTriggeredDay);
        Assert.Equal(0, n.YesterdayEventId);   // 落空=无昨日对照（0=哨兵，目录禁用）
    }

    [Fact]
    public void NextState_DoesNotMutateSourceState()
    {
        var s = St(lastDay: 5, badStreak: 2, permUsed: 1, yesterday: 9, disasters: 3);
        var o = new RollOutcome { Kind = LuckKind.Bad, EventId = EventCatalog.Ids.D3 };

        var n = DailyFlow.NextState(s, o, day: 6, permGranted: true);

        Assert.Equal(5, s.LastTriggeredDay);      // 原状态不被改写（纯函数）
        Assert.Equal(2, s.BadStreak);
        Assert.Equal(1, s.PermNegativeUsed);
        Assert.Equal(9, s.YesterdayEventId);
        Assert.Equal(3, s.DisasterCount);
        Assert.Equal(6, n.LastTriggeredDay);
        Assert.Equal(3, n.BadStreak);
        Assert.Equal(2, n.PermNegativeUsed);
    }
}
