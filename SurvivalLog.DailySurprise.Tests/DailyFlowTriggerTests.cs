using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>DailyFlow 触发半边纯函数规格测试（Task 14）：
/// HourGateReached（TriggerHour&gt;0 的定时触发门：day 越过已触发日且当前小时到点）、
/// NeedsGateHeal（通道 1 连续失败 ≥2 天且本会话未自愈过 → 拍零门控重试一次）、
/// GateHealPlan（可疑门控字段清单：非零/CanRepeat=true 列出并拍零，全零=空串）、
/// FallbackHeadTalkText（全通道失败时的头顶气泡兜底文案）。
/// Task 15 增：DecideVoidChannel（void 通道 C3/C4 的探针去留——实机证实 c3 存在
/// 静默 no-op：void 无异常≠弹窗，须以 HasPendingUIEvent 探针复核）。</summary>
public sealed class DailyFlowTriggerTests
{
    // —— HourGateReached：定时小时门 ——
    // 公式（brief T14）：TriggerHour>0 时 day > lastTriggeredDay && hour >= TriggerHour

    [Theory]
    [InlineData(50, 1, 1, 49, true)]    // 新一天的凌晨 1 点整 → 到点
    [InlineData(50, 7, 1, 49, true)]    // 新一天晚于 1 点（睡醒时段）→ 到点
    [InlineData(50, 0, 1, 49, false)]   // 0 点未到 1 点 → 等待
    [InlineData(50, 1, 23, 49, false)]  // 目标 23 点未到 → 等待
    [InlineData(50, 12, 0, 49, false)]  // TriggerHour=0=旧行为：小时门恒关（结算 hook 负责）
    [InlineData(50, 12, 1, 50, false)]  // 同日已触发（幂等）→ 关
    [InlineData(50, 12, 1, 51, false)]  // 读档回到更早天数（lastTriggeredDay 在前）→ 关
    [InlineData(49, 12, 1, 49, false)]  // day==lastTriggeredDay 边界 → 关
    public void HourGateReached_TableDriven(int day, int hour, int triggerHour, int lastDay, bool want)
    {
        Assert.Equal(want, DailyFlow.HourGateReached(day, hour, triggerHour, lastDay));
    }

    // —— NeedsGateHeal：会话内自愈武装条件 ——

    [Theory]
    [InlineData(0, false, false)]   // 首次失败：先观察（本日回退链自身还有 c2~c4）
    [InlineData(1, false, false)]   // 连续 1 天：仍不自愈
    [InlineData(2, false, true)]    // 连续 2 天：自愈武装
    [InlineData(9, false, true)]    // 连续多天：自愈武装
    [InlineData(9, true, false)]    // 本会话已自愈过：不再重复
    public void NeedsGateHeal_TableDriven(int failStreak, bool alreadyHealed, bool want)
    {
        Assert.Equal(want, DailyFlow.NeedsGateHeal(failStreak, alreadyHealed));
    }

    // —— GateHealPlan：可疑门控字段清单 ——

    [Fact]
    public void GateHealPlan_AllZero_Empty()
    {
        var f = new GateFields();   // 全零/false：原生"0 值行"常态，无可自愈

        Assert.Equal("", DailyFlow.GateHealPlan(f));
    }

    [Fact]
    public void GateHealPlan_SingleSuspiciousField_ListedWithZeroTarget()
    {
        var f = new GateFields { DayCd = 1 };

        Assert.Equal("DayCD:1→0", DailyFlow.GateHealPlan(f));
    }

    [Fact]
    public void GateHealPlan_CanRepeatTrue_IsSuspicious()
    {
        var f = new GateFields { CanRepeat = true };

        Assert.Equal("CanRepeat:True→False", DailyFlow.GateHealPlan(f));
    }

    [Fact]
    public void GateHealPlan_TypicalInjectedRow_ListsAllNonZeroInFixedOrder()
    {
        // T6 注入行的模板值（InjectorMapping.EventRowCore：DataMin=1/DataMax=999/
        // TimeMin=1/TimeMax=24/DayCD=1（直发）/CanRepeat=true）——全数可疑
        var f = new GateFields
        {
            DayCd = 1, CanRepeat = true, TimeMin = 1, TimeMax = 24, DataMin = 1, DataMax = 999,
        };

        Assert.Equal("DayCD:1→0 CanRepeat:True→False TimeMin:1→0 TimeMax:24→0 DataMin:1→0 DataMax:999→0",
            DailyFlow.GateHealPlan(f));
    }

    [Fact]
    public void GateHealPlan_ConditionGates_ListedFirst()
    {
        var f = new GateFields { ConditionSetId = 123, NeedConditionSetId = 45, TaskConditionSetId = 67, MaxTimes = 2 };

        Assert.Equal("ConditionSetId:123→0 NeedConditionSetId:45→0 TaskConditionSetId:67→0 MaxTimes:2→0",
            DailyFlow.GateHealPlan(f));
    }

    // —— FallbackHeadTalkText：全通道失败兜底 ——

    [Theory]
    [InlineData("过期药品回收站", "喜从天降：过期药品回收站")]
    [InlineData("", "喜从天降：")]
    public void FallbackHeadTalkText_PrefixesTitle(string title, string want)
    {
        Assert.Equal(want, DailyFlow.FallbackHeadTalkText(title));
    }

    // —— DecideVoidChannel：void 通道（C3/C4）探针去留（Task 15）——
    // 实机教训（daily.txt 2026-11-12 22:15）：c3 ForceTriggerEvent void 无异常→被当成功停链
    // （card=C3），但玩家无弹窗——静默 no-op。真成功=探针 true。

    [Theory]
    [InlineData(false, VoidCallOutcome.ProbeTrue, ChannelDecision.Win)]      // c3 探针 true：真成功停链
    [InlineData(false, VoidCallOutcome.ProbeFalse, ChannelDecision.Continue)] // c3 探针 false：静默 no-op 不停链，走 c4
    [InlineData(false, VoidCallOutcome.Threw, ChannelDecision.Continue)]      // c3 抛异常：继续下一通道
    [InlineData(true, VoidCallOutcome.ProbeTrue, ChannelDecision.Win)]       // c4 探针 true：真成功停链
    [InlineData(true, VoidCallOutcome.ProbeFalse, ChannelDecision.Fallback)] // c4 探针 false：链尾落 HeadTalk 兜底
    [InlineData(true, VoidCallOutcome.Threw, ChannelDecision.Fallback)]      // c4 抛异常：链尾落兜底
    public void DecideVoidChannel_TableDriven(bool isLastChannel, VoidCallOutcome outcome, ChannelDecision want)
    {
        Assert.Equal(want, DailyFlow.DecideVoidChannel(isLastChannel, outcome));
    }

    // —— ResolveCardType：void 通道 eventType 决策（Task 16 DebugCardType 诊断）——
    // 背景：实机 c4 TriggerEventUI(id,(DynamicEventType)2) 后探针 True（入队成功）但作者看不到
    // 弹窗——疑 Result(2) 只进事件日志侧栏不弹模态卡，cfg 强制 0/1/2 扫描哪种类型弹卡。

    [Theory]
    [InlineData(2, -1, 2)]  // -1 自动：直发行保持 Result
    [InlineData(1, -1, 1)]  // -1 自动：选项行保持 Task
    [InlineData(2, 0, 0)]   // 强制 Condition
    [InlineData(2, 1, 1)]   // 强制 Task（覆盖直发行的 Result）
    [InlineData(1, 2, 2)]   // 强制 Result（覆盖选项行的 Task）
    [InlineData(2, 3, 2)]   // 越界 3：保守拒绝=忽略覆盖，保持自动
    [InlineData(1, -2, 1)]  // 越界 -2：保守拒绝=忽略覆盖，保持自动
    public void ResolveCardType_TableDriven(int rowType, int typeOverride, int want)
    {
        Assert.Equal(want, DailyFlow.ResolveCardType(rowType, typeOverride, null));
    }

    [Fact]
    public void ResolveCardType_OutOfRangeWarns_ValidValuesSilent()
    {
        var warned = new System.Collections.Generic.List<string>();

        Assert.Equal(2, DailyFlow.ResolveCardType(2, 7, warned.Add));   // 越界拒绝仍回自动映射
        Assert.Single(warned);                                          // 且必须 Warn 留痕（cfg 写错可见）

        warned.Clear();
        foreach (var ok in new[] { -1, 0, 1, 2 })
            DailyFlow.ResolveCardType(2, ok, warned.Add);               // 合法值全部静默（不刷屏）
        Assert.Empty(warned);
    }
}
