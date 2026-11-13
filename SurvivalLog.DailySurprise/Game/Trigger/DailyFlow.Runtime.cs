using System;
using System.IO;
using BepInEx;

namespace SurvivalLog.DailySurprise;

/// <summary>DailyFlow 的接线半边（仅主工程编译；纯判定半边在同 partial 的 DailyFlow.cs，Tests 源链接）。
/// OnNewDay 全 try/catch 隔离——本 mod 的任何异常绝不影响原日结算；异常写节流 trace\daily.txt。
/// 链路：灾后门控 → 分槽 Load → ShouldTrigger（同日幂等）→ 表就绪探针 → DifficultyTable.Resolve
///（tier/自定义来自 DsConfig；非法值由 Resolve 回退档 3）→ LuckEngine.Roll（资格=EligibilityChecker，
/// 永久负面/替补=EventCatalog）→ NextState 记账（Save 原子写）→ 按形态展示（事件卡 Task 14 回退链
/// C1→Heal→C2→C3→C4，全败走头顶气泡兜底 / 平淡静音 Unit_ShowHeadTalk）→ **RewardExecutor.Grant 发放**
///（Task 8：物品×倍率缩放入包、拒收入 PendingRewards 重存；事件行 StartRewardSetId=0 无原生直发，
/// 不存在双发）→ 每天恰好一行 trace\daily.txt（day+EventId+胜利通道+granted/queued+WorldStateType）；
/// 通道失败日额外产生逐通道诊断行。
/// 进档补发（PendingFlusher，DsTicker 驱动）先于本每日触发链执行。</summary>
internal static partial class DailyFlow
{
    /// <summary>灾前哨兵（WorldStateType.PreDisaster=1，dump.cs:58260；枚举仅二值 1/2，dump.cs:58256~58262）。</summary>
    private const int WsPreDisaster = 1;

    /// <summary>每日触发入口（TriggerHour&gt;0 时由 HourWatcher 小时门驱动；=0 时结算 hook 与
    /// DayWatcher 兜底共用；同日幂等天然防双触发）。day&lt;=0（异常参数）直接忽略。
    /// 返回值=本轮是否已定局（Task 14，HourWatcher 重试节流用）：true=当天已消费（已掷/同日已掷/
    /// 灾前静默）；false=瞬态放弃（存档名不可达/四表未就绪/异常）——调用方下轮可重试。</summary>
    internal static bool OnNewDay(int day)
    {
        try
        {
            if (day <= 0) return true;

            // —— 模式门控：灾前序章完全静默（spec §8 生效模式：仅灾后普通档+无尽档）——
            // 口径：WorldStateType 枚举仅 PreDisaster=1/PostDisaster=2（dump.cs:58256），"已知非灾后即放行"。
            // 章表佐证（Config_Chapter.json）：仅章节 1 WorldStateType=1（灾前倒计时），章节 0/2/1000+（含
            // 探索/无尽期全部章节）均=2；无尽档在灾后章节内 → 放行。实测值随每日记录行写 trace 供作者核对。
            if (!IsPostDisaster())
            {
                // 门静默记 gate.txt（节流）而非 daily.txt——daily.txt 保持"每天至多一行触发记录"的口径，
                // 灾前档实机验证（睡 1 天）要求 daily.txt 零记录
                DsTrace.WriteThrottled("gate", "day=" + day + " 非灾后（WorldStateType=" + LastWorldState
                    + "），按门控静默");
                return true;
            }

            // —— 分槽状态 ——
            var slotDir = SlotDir();
            if (slotDir == null)
            {
                DsTrace.WriteThrottled("daily", "day=" + day + " 存档名不可达，本轮放弃（未记账，不丢天）");
                return false;
            }
            var state = StateStore.Load(slotDir, out var hadCorruption);
            if (hadCorruption)
                DsTrace.Write("daily", "state.json 损坏已重置（slot=" + Path.GetFileName(slotDir) + "）");

            if (!ShouldTrigger(state, day, postDisaster: true)) return true;   // 同日幂等闸（双触发/读档重睡）

            // —— 表就绪探针：四表未全绿不掷骰（不记账——本天让位，表就绪后的下一天照常；trace 可见）——
            if (TableInjector.EnsureInjected() == null)
            {
                DsTrace.WriteThrottled("daily", "day=" + day + " 四表未就绪，本轮放弃（未记账）");
                return false;
            }

            // —— 难度与掷骰 ——
            var p = DifficultyTable.Resolve(DsConfig.Tier, DsConfig.CustomOrDefault(), DsConfig.Warn);
            if (!DsConfig.PermNegativeAllowed)
                p.PermNegativeCap = 0;   // 关=永久负面：每个 isPermNegative 命中立即走替补（spec §5.4）
            var rand = new Random();     // 掷骰与发放共用一个随机源（RandomPickOne 择一也吃它）
            var outcome = LuckEngine.Roll(
                p, state, EventCatalog.Events,
                id => EligibilityChecker.IsEligible(EligibilityOf(id)),
                IsPermNegative,
                SubstituteOf,
                rand);

            // —— 记账（先于展示：Save 原子写是 mod 自身一致性；展示失败=当天视为落空，trace 可见不丢账）——
            var permGranted = !outcome.Substituted && IsPermNegative(outcome.EventId);
            var next = NextState(state, outcome, day, permGranted);
            StateStore.Save(next, slotDir);

            // —— 展示分发（EventId=0 落空：无展示）——
            var def = FindEvent(outcome.EventId);
            string how;
            if (outcome.EventId == 0)
                how = "落空（层内无资格候选）";
            else if (Presentation(outcome.Kind, DsConfig.PlainPopupEnabled) == PresentationKind.EventCard)
            {
                // Task 14 回退链：胜利通道名入每日行；全失败转头顶气泡兜底（玩家至少有感知）
                var channel = TriggerEventCard(outcome.EventId, def);
                how = channel == CardChannel.None
                    ? "card=ALL_CHANNELS_FAILED talk="
                      + ShowHeadTalk(FallbackHeadTalkText(def?.Title ?? ""), 4f)
                    : "card=" + channel;
            }
            else
                how = "talk=" + ShowHeadTalk(def?.Dialogue ?? "");

            // —— 发放（Task 8；Controller 裁定：事件行 StartRewardSetId=0，奖励统一代码路径，
            //    展示后立即 Grant——物品按 p.RewardMult 缩放、拒收入队。落空/无载荷 spec=空操作）——
            var context = "day=" + day + " event=" + outcome.EventId
                + (def != null ? "「" + def.Title + "」" : "");
            var grant = RewardExecutor.Grant(def?.Reward, p, rand, context);
            if (grant.Queued.Count > 0)
            {
                // 挂账持久化：入队 spec=已缩放形状（补发 RewardMult=1 原样发），追加进 PendingRewards 重存
                next.PendingRewards.AddRange(grant.Queued);
                StateStore.Save(next, slotDir);
                how += " queued=" + grant.Queued.Count;
            }
            if (grant.GrantedItems > 0)
                how += " granted=" + grant.GrantedItems;

            DsTrace.Write("daily", "day=" + day + " kind=" + outcome.Kind + " event=" + outcome.EventId
                + (def != null ? "「" + def.Title + "」" : "")
                + " rarity=" + outcome.RarityIndex + " pity=" + outcome.PityUsed + " subst=" + outcome.Substituted
                + " perm=" + permGranted + " badStreak=" + next.BadStreak + " permUsed=" + next.PermNegativeUsed
                + " disasters=" + next.DisasterCount + " " + how + " WorldStateType=" + LastWorldState);
            return true;
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("daily", "OnNewDay(day=" + day + ") 异常: " + e.Message);
            return false;
        }
    }

    /// <summary>灾后判定：GameTimeManager.WorldStateType != PreDisaster（dump.cs:421227 类、
    /// public getter dump.cs:421376 附近；经 BattleLogicWorld._GameTimeManager（internal，dump.cs:385813，
    /// interop 代理公开））。读不到世界态（未进档/空窗）=false——宁可漏一天不误入灾前档（安全向）。</summary>
    internal static bool IsPostDisaster()
    {
        try
        {
            var gtm = EligibilityChecker.World()?._GameTimeManager;
            if (gtm == null) { LastWorldState = -1; return false; }
            var ws = (int)gtm.WorldStateType;
            LastWorldState = ws;   // 观测值落字段：每日 trace 行携带，供无尽档/异常值实机核对
            return ws != WsPreDisaster;
        }
        catch (Exception e)
        {
            LastWorldState = -1;
            DsTrace.WriteThrottled("gate", "IsPostDisaster 异常（按非灾后处理）: " + e.Message);
            return false;
        }
    }

    /// <summary>最近一次读到的 WorldStateType（-1=不可达；仅诊断用途）。</summary>
    internal static int LastWorldState { get; private set; } = -1;

    // —— 掷骰谓词（EventCatalog 数据；候选集恒为目录事件，未知 id 防御性取值）——

    private static Eligibility EligibilityOf(int eventId)
    {
        foreach (var e in EventCatalog.Events)
            if (e.Id == eventId) return e.Elig;
        return Eligibility.None;
    }

    private static bool IsPermNegative(int eventId)
    {
        foreach (var id in EventCatalog.PermNegativeIds)
            if (id == eventId) return true;
        return false;
    }

    private static int SubstituteOf(int eventId) =>
        EventCatalog.Substitutes.TryGetValue(eventId, out var sub) ? sub : eventId;

    internal static EventDef FindEvent(int eventId)
    {
        foreach (var e in EventCatalog.Events)
            if (e.Id == eventId) return e;
        return null;
    }

    // —— 展示通道 ——

    /// <summary>通道 1 连败计数与会话自愈标记（Task 14）：连败跨天累计（每天至多 +1），任一通道
    /// 成功即清零；自愈每会话（进程存活期）至多一次。</summary>
    private static int _ch1FailStreak;
    private static bool _gateHealed;

    /// <summary>事件卡展示通道（Task 14 回退链，修 card=False 弹窗失败；每日链 OnNewDay 与
    /// SpikeForce 调试强制共用）：依次尝试，每条成败/异常各写一行 trace\daily.txt（通道名+
    /// 返回值/探针/异常摘要），成功即停；全失败返回 None（每日链走 HeadTalk 兜底 + ALL_CHANNELS_FAILED
    /// 记行；调试链记 force.txt）。健康日（C1 直接成功）不产生额外行——daily.txt 保持"每天一行"口径，
    /// 失败日才有通道诊断行。
    /// - C1：TryForceTriggerEvent(id,false)（现状通道；dump.cs:420302）；
    /// - Heal：C1 连败≥2 天且本会话未自愈 → 注入行可疑门控拍零（原生 0 值行形态）重试 C1 一次；
    /// - C2：TryForceTriggerEvent(id,true)（ignorePlayerSelect，dump.cs:420302）；
    /// - C3：ForceTriggerEvent(id,true)（void，dump.cs:420299；Task 15：无异常≠成功——实机证实
    ///   存在静默 no-op 被 T14 当成功停链，改用 HasPendingUIEvent(id,type) 探针（dump.cs:420326，
    ///   public bool）复核：true=真入队停链 / false=继续 C4）；
    /// - C4：TriggerEventUI(id,Type,true,false)（dump.cs:420323；直拉 UI 最可能绕过触发条件；
    ///   同探针复核：true=成功 / false=HeadTalk 兜底。eventType=注入行 Type：直发 2=Result /
    ///   选项 1=Task，DynamicEventType 枚举 dump.cs:57934；Task 16 起 DebugCardType 0/1/2
    ///   可强制覆盖（c4 与探针同 type——实机 probe=True 却无弹窗，扫哪种类型弹模态卡））。</summary>
    internal static CardChannel TriggerEventCard(int eventId, EventDef def)
    {
        var dem = SpikeForce.LiveDem();
        if (dem == null)
        {
            DsTrace.Write("daily", "card: 管理器不可达（未进档/世界未建），全通道放弃 event=" + eventId);
            return CardChannel.None;
        }

        // C1（现状通道：默认参）
        var c1 = TryBoolChannel("c1", dem, eventId, false);
        if (c1) return Win(CardChannel.C1);
        _ch1FailStreak++;

        // Heal（会话内一次）：连续 2 天 C1 失败 → 拍零可疑门控重试 C1
        if (NeedsGateHeal(_ch1FailStreak, _gateHealed))
        {
            var plan = HealGateFields(eventId);
            _gateHealed = true;   // 自愈机会每会话一次（空清单=行本就全零，同样标记防反复检查）
            if (plan.Length > 0)
            {
                DsTrace.Write("daily", "card heal 拍零门控重试 C1: " + plan);
                if (TryBoolChannel("heal", dem, eventId, false)) return Win(CardChannel.Heal);
            }
            else
                DsTrace.Write("daily", "card heal: 注入行门控本就全零，无可自愈");
        }

        // C2（ignorePlayerSelect=true）
        if (TryBoolChannel("c2", dem, eventId, true)) return Win(CardChannel.C2);

        // C3（void；Task 15：无异常≠成功——实机证实静默 no-op，探针复核：true 停链 / false 继续 C4）
        var type = ResolveEventType(def);
        try
        {
            dem.ForceTriggerEvent(eventId, true);
            var c3 = VoidOutcomeOf(ProbePendingUi(dem, eventId, type));
            DsTrace.Write("daily", "card c3 ForceTriggerEvent(" + eventId + ",true) void→probe="
                + (c3 == VoidCallOutcome.ProbeTrue));
            if (DecideVoidChannel(isLastChannel: false, c3) == ChannelDecision.Win) return Win(CardChannel.C3);
        }
        catch (Exception e)
        {
            DsTrace.Write("daily", "card c3 ForceTriggerEvent(" + eventId + ",true) 异常: " + e.Message);
        }

        // C4（直拉 UI，链尾；同探针：true=成功 / false=落 ALL_CHANNELS_FAILED→HeadTalk 兜底。
        // Task 16：Type=实际生效值（DebugCardType 覆盖后），force=cfg 原始值——合并进本行，行数口径不变）
        try
        {
            dem.TriggerEventUI(eventId, type, true, false);
            var c4 = VoidOutcomeOf(ProbePendingUi(dem, eventId, type));
            DsTrace.Write("daily", "card c4 TriggerEventUI(" + eventId + ",Type=" + (int)type
                + ",fresh=true) ui→probe=" + (c4 == VoidCallOutcome.ProbeTrue)
                + " force=" + DsConfig.CardTypeOverride);
            if (DecideVoidChannel(isLastChannel: true, c4) == ChannelDecision.Win) return Win(CardChannel.C4);
        }
        catch (Exception e)
        {
            DsTrace.Write("daily", "card c4 TriggerEventUI(" + eventId + ") 异常: " + e.Message);
        }

        DsTrace.Write("daily", "card ALL_CHANNELS_FAILED event=" + eventId + "，转头顶气泡兜底");
        return CardChannel.None;

        CardChannel Win(CardChannel c)
        {
            _ch1FailStreak = 0;
            return c;
        }
    }

    /// <summary>bool 通道单发（C1/heal/C2 共用）：TryForceTriggerEvent(id, ignorePlayerSelect)
    /// （dump.cs:420302）。返回 false 或抛异常时写一行通道诊断（成功安静——保持 daily.txt 行数口径）。</summary>
    private static bool TryBoolChannel(string name,
        GameCore.HotUpdate.Battle.Logic.DynamicEventManager dem, int eventId, bool ignorePlayerSelect)
    {
        try
        {
            if (dem.TryForceTriggerEvent(eventId, ignorePlayerSelect)) return true;
            DsTrace.Write("daily", "card " + name + " TryForceTriggerEvent(" + eventId + ","
                + ignorePlayerSelect.ToString().ToLower() + ")=False");
        }
        catch (Exception e)
        {
            DsTrace.Write("daily", "card " + name + " TryForceTriggerEvent(" + eventId + ","
                + ignorePlayerSelect.ToString().ToLower() + ") 异常: " + e.Message);
        }
        return false;
    }

    /// <summary>探针/UI 的eventType（Task 15/16）：注入行 Type 字段（直发 2=Result / 选项 1=Task，
    /// InjectorMapping.EventRow；DynamicEventType 枚举 dump.cs:57934）经纯侧 ResolveCardType 吃
    /// DebugCardType 覆盖（-1=自动 / 0/1/2=强制，越界拒绝保持自动+Warn——诊断弹窗形态）。
    /// 行不可读/def 缺失回退 2=Result（直发主流形态）。</summary>
    private static GameCore.HotUpdate.DynamicEventType ResolveEventType(EventDef def)
    {
        int rowType;
        try
        {
            rowType = def != null ? (int)InjectorMapping.EventRow(def)["Type"] : 2;
        }
        catch
        {
            rowType = 2;
        }
        return (GameCore.HotUpdate.DynamicEventType)ResolveCardType(
            rowType, DsConfig.CardTypeOverride, DsConfig.Warn);
    }

    /// <summary>待处理 UI 探针（Task 15）：HasPendingUIEvent(eventConfigId, eventType)
    /// （dump.cs:420326，DynamicEventManager public bool——与 ForceTriggerEvent/TriggerEventUI
    /// 同类，dump.cs:419735 TypeDefIndex 6405）。时机=void 调用后同 tick 立即读：
    /// 入队发生在 void 调用同步执行内，消费方 UpdateUI（dump.cs:420329）逐帧驱动——立即读
    /// 在"入队后、消费前"窗口，真入队必 true、no-op 必 false；改下 1~2 tick 复查反而会在
    /// UpdateUI 已消费后误判真成功为失败（重复拉卡+多余兜底）。探针异常算 false
    /// （保守：多试下一通道，不误停链）。</summary>
    private static bool ProbePendingUi(GameCore.HotUpdate.Battle.Logic.DynamicEventManager dem, int eventId,
        GameCore.HotUpdate.DynamicEventType type)
    {
        try
        {
            return dem.HasPendingUIEvent(eventId, type);
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("daily", "card probe HasPendingUIEvent(" + eventId + ") 异常: " + e.Message);
            return false;
        }
    }

    /// <summary>探针值→void 通道结果（false 含探针异常回退——均按静默 no-op 处理）。</summary>
    private static VoidCallOutcome VoidOutcomeOf(bool probe) =>
        probe ? VoidCallOutcome.ProbeTrue : VoidCallOutcome.ProbeFalse;

    /// <summary>自愈执行半边（Task 14）：读注入事件行的 10 个可疑门控字段 → GateHealPlan 出清单 →
    /// 非空则全数拍零（进程内存态直写，与注入哨兵探针字段零交集，不会误判"行被清"触发整行重放；
    /// 全零行=Config_Event 原生常态形态——ConditionSetId 0 值 68%、MaxTimes 99.8%、
    /// TimeMin/TimeMax/DayCD 约半数、DataMin/DataMax 全零行 275 行）。返回拍零清单（空=无可自愈）。</summary>
    private static string HealGateFields(int eventId)
    {
        try
        {
            var dict = SurvivalLog.ModShared.BagTable.Cm()?._Config_Event_Dict;
            if (dict == null || !dict.TryGetValue(eventId, out var row) || row == null) return "";
            var plan = GateHealPlan(new GateFields
            {
                ConditionSetId = row.ConditionSetId,
                NeedConditionSetId = row.NeedConditionSetId,
                TaskConditionSetId = row.TaskConditionSetId,
                DayCd = row.DayCD,
                CanRepeat = row.CanRepeat,
                MaxTimes = row.MaxTimes,
                TimeMin = row.TimeMin,
                TimeMax = row.TimeMax,
                DataMin = row.DataMin,
                DataMax = row.DataMax,
            });
            if (plan.Length == 0) return "";
            row.ConditionSetId = 0;
            row.NeedConditionSetId = 0;
            row.TaskConditionSetId = 0;
            row.DayCD = 0;
            row.CanRepeat = false;
            row.MaxTimes = 0;
            row.TimeMin = 0;
            row.TimeMax = 0;
            row.DataMin = 0;
            row.DataMax = 0;
            return plan;
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("daily", "card heal 读/拍门控异常: " + e.Message);
            return "";
        }
    }

    /// <summary>头顶气泡（平淡静音 / PendingFlusher 补发提示 / Task 14 全通道失败兜底共用）：
    /// 主角模型经 WebGm.GetLeadingRoleModel（WelcomeTip 样板），Unit_ShowHeadTalk(text, during)
    /// （dump.cs:357769，interop 代理公开；During≈真实秒，实测定稿；默认 3=补发/平淡，4=兜底更醒目）。</summary>
    internal static bool ShowHeadTalk(string text, float during = 3f)
    {
        try
        {
            var model = GameCore.HotUpdate.WebGm.GetLeadingRoleModel(out _);
            if (model == null) return false;
            model.Unit_ShowHeadTalk(text, during);
            return true;
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("daily", "Unit_ShowHeadTalk 异常: " + e.Message);
            return false;
        }
    }

    // —— 分槽目录 ——

    /// <summary>状态槽目录：plugins-data\DailySurprise\saves\&lt;存档基名&gt;（游戏存档文件名本身合法，
    /// 直接作目录名；StateStore.Save 负责建目录）。存档名不可达（进档空窗）=null。</summary>
    internal static string SlotDir()
    {
        var baseName = SaveRef.CurrentSaveBase();
        if (string.IsNullOrEmpty(baseName)) return null;
        return Path.Combine(Paths.BepInExRootPath, "plugins-data", "DailySurprise", "saves", baseName);
    }
}
