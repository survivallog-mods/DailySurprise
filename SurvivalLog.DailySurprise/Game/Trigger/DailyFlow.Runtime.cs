using System;
using System.IO;
using BepInEx;

namespace SurvivalLog.DailySurprise;

/// <summary>DailyFlow 的接线半边（仅主工程编译；纯判定半边在同 partial 的 DailyFlow.cs，Tests 源链接）。
/// OnNewDay 全 try/catch 隔离——本 mod 的任何异常绝不影响原日结算；异常写节流 trace\daily.txt。
/// 链路：灾后门控 → 分槽 Load → ShouldTrigger（同日幂等）→ 表就绪探针 → DifficultyTable.Resolve
///（tier/自定义来自 DsConfig；非法值由 Resolve 回退档 3）→ LuckEngine.Roll（资格=EligibilityChecker，
/// 永久负面/替补=EventCatalog）→ NextState 记账（Save 原子写）→ 按形态展示（事件卡 TryForceTriggerEvent
/// 返回值 / 平淡静音 Unit_ShowHeadTalk）→ **RewardExecutor.Grant 发放**（Task 8：物品×倍率缩放入包、
/// 拒收入 PendingRewards 重存；事件行 StartRewardSetId=0 无原生直发，不存在双发）→
/// 每天恰好一行 trace\daily.txt（day+EventId+返回值+granted/queued+WorldStateType）。
/// 进档补发（PendingFlusher，DsTicker 驱动）先于本每日触发链执行。</summary>
internal static partial class DailyFlow
{
    /// <summary>灾前哨兵（WorldStateType.PreDisaster=1，dump.cs:58260；枚举仅二值 1/2，dump.cs:58256~58262）。</summary>
    private const int WsPreDisaster = 1;

    /// <summary>每日触发入口（Patch_CaptureDaySnapshot postfix 与 DayWatcher 兜底共用；同日幂等天然防双触发）。
    /// day&lt;=0（异常参数）直接忽略。</summary>
    internal static void OnNewDay(int day)
    {
        try
        {
            if (day <= 0) return;

            // —— 模式门控：灾前序章完全静默（spec §8 生效模式：仅灾后普通档+无尽档）——
            // 口径：WorldStateType 枚举仅 PreDisaster=1/PostDisaster=2（dump.cs:58256），"已知非灾前即放行"。
            // 章表佐证（Config_Chapter.json）：仅章节 1 WorldStateType=1（灾前倒计时），章节 0/2/1000+（含
            // 探索/无尽期全部章节）均=2；无尽档在灾后章节内 → 放行。实测值随每日记录行写 trace 供作者核对。
            if (!IsPostDisaster())
            {
                // 门静默记 gate.txt（节流）而非 daily.txt——daily.txt 保持"每天至多一行触发记录"的口径，
                // 灾前档实机验证（睡 1 天）要求 daily.txt 零记录
                DsTrace.WriteThrottled("gate", "day=" + day + " 非灾后（WorldStateType=" + LastWorldState
                    + "），按门控静默");
                return;
            }

            // —— 分槽状态 ——
            var slotDir = SlotDir();
            if (slotDir == null)
            {
                DsTrace.WriteThrottled("daily", "day=" + day + " 存档名不可达，本轮放弃（未记账，不丢天）");
                return;
            }
            var state = StateStore.Load(slotDir, out var hadCorruption);
            if (hadCorruption)
                DsTrace.Write("daily", "state.json 损坏已重置（slot=" + Path.GetFileName(slotDir) + "）");

            if (!ShouldTrigger(state, day, postDisaster: true)) return;   // 同日幂等闸（双触发/读档重睡）

            // —— 表就绪探针：四表未全绿不掷骰（不记账——本天让位，表就绪后的下一天照常；trace 可见）——
            if (TableInjector.EnsureInjected() == null)
            {
                DsTrace.WriteThrottled("daily", "day=" + day + " 四表未就绪，本轮放弃（未记账）");
                return;
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
                how = "card=" + TriggerEventCard(outcome.EventId);
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
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("daily", "OnNewDay(day=" + day + ") 异常: " + e.Message);
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

    /// <summary>事件卡：DynamicEventManager.TryForceTriggerEvent（bool 版，dump.cs:415573；
    /// SpikeForce 同款管理器可达性判定）。返回值如实写每日 trace 行。</summary>
    private static bool TriggerEventCard(int eventId)
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.IsInstanceCreated)
                return false;
            var dem = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.Instance
                ?._DynamicEventManager;
            if (dem == null) return false;
            return dem.TryForceTriggerEvent(eventId, false);
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("daily", "TryForceTriggerEvent(" + eventId + ") 异常: " + e.Message);
            return false;
        }
    }

    /// <summary>头顶气泡（平淡静音 / PendingFlusher 补发提示共用）：主角模型经
    /// WebGm.GetLeadingRoleModel（WelcomeTip 样板），Unit_ShowHeadTalk(text, 3)
    /// （dump.cs:357769，interop 代理公开；During≈真实秒，实测定稿）。</summary>
    internal static bool ShowHeadTalk(string text)
    {
        try
        {
            var model = GameCore.HotUpdate.WebGm.GetLeadingRoleModel(out _);
            if (model == null) return false;
            model.Unit_ShowHeadTalk(text, 3f);
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
