namespace SurvivalLog.DailySurprise;

/// <summary>触发展示形态（spec §8.1 平淡静音）：平淡档 ShowPlainPopup=false 时不拉事件卡，
/// 改为主角头顶气泡（台词照播、记账照常）；好运/霉运永远走事件卡。</summary>
public enum PresentationKind
{
    EventCard,
    HeadTalk,
}

/// <summary>每日触发链的纯逻辑半边（Task 7）。本文件**零 BepInEx/il2cpp 依赖**、由 Tests 源链接
/// 钉死判定规则；接线半边（OnNewDay/IsPostDisaster/展示分发）在同 partial 类的
/// Game/Trigger/DailyFlow.Runtime.cs（仅主工程编译）。</summary>
internal static partial class DailyFlow
{
    /// <summary>是否应触发今天的掷骰（spec §5.1 前置门）：
    /// 非灾后（灾前序章/读不到世界态）→ false 完全静默；
    /// day==LastTriggeredDay → false（同日幂等：读档回昨天重睡同一天/补丁与兜底双触发都吃这道闸）。</summary>
    internal static bool ShouldTrigger(RollState s, int day, bool postDisaster) =>
        postDisaster && day != s.LastTriggeredDay;

    /// <summary>展示形态（spec §8.1）：平淡档且 ShowPlainPopup=false → 头顶气泡；其余一律事件卡。</summary>
    internal static PresentationKind Presentation(LuckKind kind, bool showPlainPopup) =>
        kind == LuckKind.Plain && !showPlainPopup ? PresentationKind.HeadTalk : PresentationKind.EventCard;

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
