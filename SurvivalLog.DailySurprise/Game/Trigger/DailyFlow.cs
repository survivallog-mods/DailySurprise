using System;

namespace SurvivalLog.DailySurprise;

/// <summary>事件卡展示通道（Task 14 回退链的胜利通道，CardChannel.None=全失败走头顶气泡兜底）。
/// 通道序：C1 TryForce(id,false) → Heal 自愈拍零后重试 C1 → C2 TryForce(id,true) →
/// C3 ForceTrigger(id,true)（void，无异常=视为成功）→ C4 TriggerEventUI（void，同前）。
/// public=与 VoidCallOutcome/ChannelDecision 同族通道决策枚举（T18 fix#1 起 NeedsBubbleGrant
/// 消费、表驱动测试签名引用）。</summary>
public enum CardChannel
{
    None,
    C1,
    Heal,
    C2,
    C3,
    C4,
}

/// <summary>void 通道调用结果（Task 15）：void 方法无返回值可判，"无异常"与"真成功"之间
/// 存在静默 no-op（实机 daily.txt 2026-11-12：c3 ForceTriggerEvent 无异常被当成功停链
/// card=C3，玩家无弹窗）——以 HasPendingUIEvent 探针（dump.cs:420326）复核后归为三态。</summary>
public enum VoidCallOutcome
{
    /// <summary>调用抛异常。</summary>
    Threw,

    /// <summary>void 无异常但探针 false（静默 no-op：事件未入待处理 UI 队列）。</summary>
    ProbeFalse,

    /// <summary>void 无异常且探针 true（事件已入待处理 UI 队列，弹窗可期）。</summary>
    ProbeTrue,
}

/// <summary>void 通道去留决策（Task 15）：Win=真成功停链；Continue=试下一通道（c3）；
/// Fallback=链尾（c4）落 HeadTalk 兜底。</summary>
public enum ChannelDecision
{
    Continue,
    Win,
    Fallback,
}

/// <summary>Config_Event 可疑门控字段快照（Task 14 会话内自愈的纯侧数据；
/// 字段名与 dump.cs:35896~35937 公开属性一一对应）。</summary>
internal readonly struct GateFields
{
    internal int ConditionSetId { get; init; }
    internal int NeedConditionSetId { get; init; }
    internal int TaskConditionSetId { get; init; }
    internal int DayCd { get; init; }
    internal bool CanRepeat { get; init; }
    internal int MaxTimes { get; init; }
    internal int TimeMin { get; init; }
    internal int TimeMax { get; init; }
    internal int DataMin { get; init; }
    internal int DataMax { get; init; }
}

/// <summary>每日触发链的纯逻辑半边（Task 7）。本文件**零 BepInEx/il2cpp 依赖**、由 Tests 源链接
/// 钉死判定规则；接线半边（OnNewDay/IsPostDisaster/展示分发）在同 partial 类的
/// Game/Trigger/DailyFlow.Runtime.cs（仅主工程编译）。
/// Task 14 增：HourGateReached（TriggerHour&gt;0 定时触发门）、NeedsGateHeal/GateHealPlan
/// （通道 1 连败的自愈决策）、FallbackHeadTalkText（全通道失败兜底文案）。
/// Task 15 增：DecideVoidChannel（void 通道 C3/C4 的探针去留——no-op 不停链、链尾落兜底）。
/// T18 fix#1 增：NeedsBubbleGrant（气泡路径代码发奖判定——卡未显示则声明式无载体，代码照发）。</summary>
internal static partial class DailyFlow
{
    /// <summary>是否应触发今天的掷骰（spec §5.1 前置门）：
    /// 非灾后（灾前序章/读不到世界态）→ false 完全静默；
    /// day==LastTriggeredDay → false（同日幂等：读档回昨天重睡同一天/补丁与兜底双触发都吃这道闸）。</summary>
    internal static bool ShouldTrigger(RollState s, int day, bool postDisaster) =>
        postDisaster && day != s.LastTriggeredDay;

    /// <summary>气泡路径代码发奖判定（T18 fix#1 Controller 裁定：显示走气泡、奖励照发）：事件卡全通道
    /// 失败（channel=None）转气泡兜底——声明式奖励没有载体（卡不显示=原生 FSM 不结算 RewardSetId），
    /// 由 RewardExecutor 保留代码路径照发（Items/Buffs/Attrs/PermBuffByTier/RandomPickOne 全形状，
    /// Op 系经 Grant 内 RunsTopLevelOp 分流照跑）。卡通道成功（含选项卡）→ 原生声明式结算，
    /// 代码停发（T18 防双发口径不变）。A2 起平淡事件同样走事件卡——原"平淡静音气泡"路径取消
    /// （PresentationKind/Presentation 随之移除）。落空（def=null）：Grant 空操作，判定值无副作用。</summary>
    internal static bool NeedsBubbleGrant(CardChannel channel) => channel == CardChannel.None;

    /// <summary>定时小时门（Task 14，TriggerHour&gt;0 时）：新一天（day 越过已触发日）且当前
    /// 游戏内小时到点（hour &gt;= TriggerHour）→ true。TriggerHour&lt;=0（旧行为）恒 false——
    /// 结算 hook 负责即时触发，小时门不参与。lastTriggeredDay 口径=同日幂等闸（读档重进同日不重掷）。</summary>
    internal static bool HourGateReached(int day, int hour, int triggerHour, int lastTriggeredDay) =>
        triggerHour > 0 && day > lastTriggeredDay && hour >= triggerHour;

    /// <summary>会话内自愈武装（Task 14）：通道 1（TryForce 默认参）连续失败 ≥2 天且本会话未自愈过
    /// → true（注入行可疑门控拍零后重试一次）。首日失败不自愈——当天回退链自身还有 C2~C4 兜底。</summary>
    internal static bool NeedsGateHeal(int channel1FailStreak, bool alreadyHealed) =>
        channel1FailStreak >= 2 && !alreadyHealed;

    /// <summary>可疑门控清单（Task 14 纯决策）：非零条件/CD/时窗/数据窗字段或 CanRepeat=true 逐项
    /// 列出"字段:现值→0"（顺序固定=brief 字段序）；全零=空串（原生 0 值行常态，无可自愈）。
    /// 依据 Config_Event 原生 2101 行统计：ConditionSetId 0 值 68%、MaxTimes 0 值 99.8%、
    /// TimeMin/TimeMax/DayCD 0 值约半数、DataMin/DataMax 全零行 275 行——全零是合法原生形态。</summary>
    internal static string GateHealPlan(GateFields f)
    {
        var plan = "";
        Append(ref plan, "ConditionSetId", f.ConditionSetId);
        Append(ref plan, "NeedConditionSetId", f.NeedConditionSetId);
        Append(ref plan, "TaskConditionSetId", f.TaskConditionSetId);
        Append(ref plan, "DayCD", f.DayCd);
        if (f.CanRepeat) plan += (plan.Length > 0 ? " " : "") + "CanRepeat:True→False";
        Append(ref plan, "MaxTimes", f.MaxTimes);
        Append(ref plan, "TimeMin", f.TimeMin);
        Append(ref plan, "TimeMax", f.TimeMax);
        Append(ref plan, "DataMin", f.DataMin);
        Append(ref plan, "DataMax", f.DataMax);
        return plan;

        static void Append(ref string s, string name, int v)
        {
            if (v != 0) s += (s.Length > 0 ? " " : "") + name + ":" + v + "→0";
        }
    }

    /// <summary>全通道失败兜底文案（Task 14）：HeadTalk 至少让玩家有感知。</summary>
    internal static string FallbackHeadTalkText(string title) => "喜从天降：" + title;

    /// <summary>void 通道（C3 ForceTriggerEvent / C4 TriggerEventUI，Task 15）去留决策：
    /// 探针 true 才算真成功（Win 停链）；探针 false（静默 no-op）或抛异常——c3（非链尾）
    /// Continue 走 c4、c4（链尾）Fallback 落 HeadTalk 兜底。实机教训：c3 void 无异常≠弹窗。</summary>
    internal static ChannelDecision DecideVoidChannel(bool isLastChannel, VoidCallOutcome outcome) =>
        outcome == VoidCallOutcome.ProbeTrue
            ? ChannelDecision.Win
            : isLastChannel ? ChannelDecision.Fallback : ChannelDecision.Continue;

    /// <summary>掷骰后记账（spec §5.1/§5.4；纯函数——返回新 RollState，不改写入参）：
    /// - BadStreak：按 Kind——霉运 +1（含落空：按 Kind 记，口径=今天没有好事），好运/平淡清 0
    ///   （保底强制的好运同样是好运，自然清 0）；
    /// - PermNegativeUsed：permGranted（isPermNegative 命中且未被替补）才 +1；替补结果不占名额；
    /// - YesterdayEventId=EventId（落空=0，目录禁用 0=无对照）；
    /// - LastTriggeredDay=day（落空也记——当天已消费，同日幂等靠它）；
    /// - DisasterCount：选中事件（替补后的最终 EventId）在目录中 Rarity==3 且 Kind==Bad 时 +1
    ///   （仅统计字段，引擎不消费；D1→D2 替补后仍是灾厄照计，D3→H2 替补后降为重度不计）。</summary>
    internal static RollState NextState(RollState s, RollOutcome o, int day, bool permGranted)
    {
        return new RollState
        {
            LastTriggeredDay = day,
            YesterdayEventId = o.EventId,
            BadStreak = o.Kind == LuckKind.Bad ? s.BadStreak + 1 : 0,
            PermNegativeUsed = s.PermNegativeUsed + (permGranted ? 1 : 0),
            DisasterCount = s.DisasterCount + (IsDisaster(o.EventId) ? 1 : 0),
            PendingRewards = s.PendingRewards,   // 挂账列表原引用移交（Task 8 起在执行器追加后由调用方 Save）
        };
    }

    /// <summary>灾厄命中判定：按最终 EventId 查目录（霉运 Rarity==3 层=灾厄 D1~D3；替补后以实际
    /// 展示的事件为准）。目录外 id / 落空 0 / 非霉运 → false。</summary>
    private static bool IsDisaster(int eventId)
    {
        foreach (var e in EventCatalog.Events)
            if (e.Id == eventId)
                return e.Kind == LuckKind.Bad && e.Rarity == 3;
        return false;
    }
}
