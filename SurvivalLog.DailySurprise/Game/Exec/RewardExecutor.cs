using System;
using System.Collections.Generic;
using System.Text;
using SurvivalLog.ModShared;

namespace SurvivalLog.DailySurprise;

/// <summary>Grant 结果（Task 8 产出接口）：GrantedItems=实际入包件数（已按倍率缩放口径）；
/// Queued=背包拒收挂账（spec=**已缩放形状**——补发按 RewardMult=1 原样发，不二次缩放）。</summary>
internal sealed class GrantResult
{
    public int GrantedItems;
    public List<PendingReward> Queued = new();
}

/// <summary>奖励执行器（Task 8 形态保留）。**调用面（T18 + fix#1）**：事件卡显示路径不再调用
/// 物品/buff/属性路径（原生 RewardSet 声明式结算；Op 系→PunishmentOps 由确认补丁 Patch_OpOnConfirm
/// 在玩家点确认时执行，A1）；
/// 气泡路径（事件卡全通道失败——NeedsBubbleGrant）恢复全量调用本类 Grant——显示走
/// 气泡、奖励照发（数量=RewardSpec 基准×当前难度 RewardMult，ScaleCount 与注入行烘焙同公式，
/// 不读注入行、不二次缩放），拒收挂账（PendingFlusher 补发）。
/// 消费 RewardSpec 全形状（Items/Buffs/Attrs/PermBuffByTier/RandomPickOne）；Op 系（耐久/腐坏/偷窃/
/// 永久属性）Task 9 起分流到 <see cref="PunishmentOps.Apply"/>（Summary 进 grants 行 + punish.txt）；
/// Task 10 起选项事件（Choices 非空）顶层 Op 短路（RunsTopLevelOp）——选项效果由子事件
/// RewardSetId 声明式发奖（L1 三选一物品、H3 追=AddBuff 1111/算了=ItemCost 20362×10）。
/// 发放通道取证（gamekb 2026-11-05_26454866 dump.cs）：
/// - 物品=ItemManager.TryAddItemToBag(long ownerId,int cfgId,int count,int startTime,int timeLeft,
///   int useTimes=0,float timeScale=1,Func&lt;int,int,int,int,bool&gt; onFailed,int maxUseTimes=0,
///   float[] instanceVD,float[] instanceEffectEnd,int originalTimeLeft=0,int instanceBurnValue=0,
///   bool polluted=…,bool allowPartial=…)（dump.cs:425146，18 参）；owner=Redux
///   State_Data_Player.PlayerId（CabinetEverywhere 校准点 T14 在役，EligibilityChecker 同源）；
///   allowPartial=false——任一件塞不下即整条 false → DecideQueue 入队。
///   保质期（先例=套件在役定案）：TimeLeft/StartTime 单位=游戏小时（CabinetEverywhere RailSortMath
///   DaysLeftOf 定案：剩余= timeLeft+startTime−now）；Config_Item.Life 单位=天 → 满保鲜
///   timeLeft=Life×24、startTime=GetNowHours(world)（dump.cs:432669）；不可腐（Life&lt;=0）0/0
///   （StackLimit999 补偿同款）。originalTimeLeft 同传满保鲜时长（临期比例口径）。
/// - buff=BuffComponent.AddBuff(long releaserId, int configId)（dump.cs:372269）；主角=
///   AgentManager.GetLeadingRole()（dump.cs:379458）+ AgentTools.GetAgentComponent&lt;T&gt;（379706，
///   EndlessGuard/EligibilityChecker 在役模式）；releaserId=主角自身 InstanceId（自发）。
/// - 属性=AttributeComponent.AddConditionValue(AttrName, int)（dump.cs:371548）——一次性五维
///   增减、负值即扣（N4 精力−2 实机核对项）；AttrId=AttrName 枚举 1..5（dump.cs:57466）。
/// 全程分层 try/catch（物品/buff/属性互不连坐），发放汇总行写 trace\grants.txt。</summary>
internal static class RewardExecutor
{
    /// <summary>发放一档奖励。context=诊断用途（daily 触发/补发/调试强制），可空。
    /// 通道不可达（未进档/主角缺失）时物品整条入队（不丢奖），buff/属性记 trace（下次自然触发不补——
    /// 它们与背包空间无关，失败属异常态，DailyFlow 的调用时机保证世界就绪）。</summary>
    internal static GrantResult Grant(RewardSpec spec, DifficultyParams p, Random rand, string context = null)
    {
        var result = new GrantResult();
        if (spec == null) return result;
        if (p == null) p = DifficultyTable.Resolve(3, null, null);   // 防御：倍率档缺省按基准档 3

        var sb = new StringBuilder();
        sb.Append(string.IsNullOrEmpty(context) ? "grant" : context);
        var acted = false;   // 有动作才写 grants.txt 行（N1 等空载荷不产噪声）

        try { acted |= GrantItems(spec, p, rand, context, result, sb); }
        catch (Exception e) { DsTrace.WriteThrottled("grants", "Grant.Items " + e.Message); }

        try { acted |= GrantBuffs(spec, p, sb); }
        catch (Exception e) { DsTrace.WriteThrottled("grants", "Grant.Buffs " + e.Message); }

        try { acted |= GrantAttrs(spec, sb); }
        catch (Exception e) { DsTrace.WriteThrottled("grants", "Grant.Attrs " + e.Message); }

        if (spec.RunsTopLevelOp)
        {
            // Task 9 分流：Op 系（耐久/永久属性/腐坏/偷窃）归 PunishmentOps（Apply 全隔离不抛出，
            // Summary 进本行 + 一行汇总另写 trace\punish.txt）。
            // Task 10 防双发：RunsTopLevelOp=Choices 非空即短路——选项事件顶层效果由子事件
            // RewardSetId 声明式结算（H3 的 StealParts 体现在「算了」选项 ItemCost 20362×10），
            // 代码侧（自然链路+调试链路）不再执行顶层 Op
            sb.Append(" op=").Append(spec.Op).Append('[')
                .Append(PunishmentOps.Apply(spec.Op, spec.OpParam, p.LossMult, rand)).Append(']');
            acted = true;
        }

        if (acted) DsTrace.Write("grants", sb.ToString());
        return result;
    }

    // ———————————————— 物品：逐条缩放入包，拒收挂账 ————————————————

    private static bool GrantItems(RewardSpec spec, DifficultyParams p, Random rand,
        string context, GrantResult result, StringBuilder sb)
    {
        var picks = PickItems(spec, rand);
        if (picks.Count == 0) return false;

        var world = EligibilityChecker.World();
        var im = world?._ItemManager;
        var owner = EligibilityChecker.PlayerBagOwnerId();
        if (im == null || owner <= 0)
        {
            // 通道不可达=按拒收整批入队（不丢奖，**同样先缩放**——挂账恒为已缩放形状），补发链就绪后重试
            var scaledAll = new List<ItemGain>();
            foreach (var g in picks)
                if (g != null && g.Count > 0)
                    scaledAll.Add(new ItemGain { ItemId = g.ItemId, Count = RewardMath.ScaleCount(g.Count, p.RewardMult) });
            QueueScaled(scaledAll,
                string.IsNullOrEmpty(context) ? "发放通道不可达" : context + " 发放通道不可达", result);
            sb.Append(" items=通道不可达入队").Append(scaledAll.Count).Append("条");
            return true;
        }

        var cm = BagTable.Cm();
        var rejected = new List<ItemGain>();
        foreach (var g in picks)
        {
            if (g == null || g.Count <= 0) continue;
            var scaled = RewardMath.ScaleCount(g.Count, p.RewardMult);

            // 保质期：可腐（Life>0，单位天）满保鲜 startTime=now小时/timeLeft=Life×24 小时；
            // 不可腐 0/0（先例=StackLimit999 补偿、CabinetEverywhere DaysLeftOf 单位定案）
            var startTime = 0;
            var timeLeft = 0;
            try
            {
                var cfg = cm?.Get_Config_Item(g.ItemId);
                if (cfg != null && cfg.Life > 0)
                {
                    startTime = (int)GameCore.HotUpdate.Battle.Logic.RobotSortingManager.GetNowHours(world);
                    timeLeft = cfg.Life * 24;
                }
            }
            catch (Exception e)
            {
                DsTrace.WriteThrottled("grants", "保质期读取异常（按不可腐发放）cfg=" + g.ItemId + ": " + e.Message);
            }

            bool ok;
            try
            {
                ok = im.TryAddItemToBag(owner, g.ItemId, scaled, startTime, timeLeft,
                    0, 1f, null, 0, null, null, timeLeft, 0, false, false, false, false, 0);
            }
            catch (Exception e)
            {
                DsTrace.WriteThrottled("grants", "TryAddItemToBag(" + g.ItemId + "×" + scaled + ") 抛异常: " + e.Message);
                ok = false;
            }

            if (ok) result.GrantedItems += scaled;
            else rejected.Add(new ItemGain { ItemId = g.ItemId, Count = scaled });
            sb.Append(' ').Append(g.ItemId).Append('×').Append(scaled).Append(ok ? "" : "(拒)");
        }

        if (rejected.Count > 0)
            QueueScaled(rejected,
                (string.IsNullOrEmpty(context) ? "" : context + " ") + "背包拒收", result);
        return true;
    }

    /// <summary>RandomPickOne（spec §8 随机×1）：候选池均匀取 1 条（注入 rand 可种子化）；否则全量。</summary>
    private static List<ItemGain> PickItems(RewardSpec spec, Random rand)
    {
        if (spec.Items == null || spec.Items.Count == 0) return new List<ItemGain>();
        if (!spec.RandomPickOne) return spec.Items;
        var pick = RewardMath.PickRandomOne(spec.Items, rand);
        return pick == null ? new List<ItemGain>() : new List<ItemGain> { pick };
    }

    /// <summary>拒收条目入队：**存已缩放 spec**（数量已 ×倍率；2026-11-10 定案——补发按
    /// RewardMult=1 原样发，不二次缩放），经 DecideQueue 挂账。</summary>
    private static void QueueScaled(List<ItemGain> scaledItems, string reason, GrantResult result)
    {
        var remainder = new RewardSpec { Items = scaledItems };
        var pending = RewardMath.DecideQueue(bagAccepted: false, spec: remainder, reason: reason);
        if (pending != null) result.Queued.Add(pending);
    }

    // ———————————————— buff：Buffs 直发 + PermBuffByTier 变体 ————————————————

    private static bool GrantBuffs(RewardSpec spec, DifficultyParams p, StringBuilder sb)
    {
        if (!HasBuffs(spec)) return false;
        var lead = LeadingRole();
        var buffs = lead != null
            ? GameCore.HotUpdate.Battle.Logic.AgentTools
                .GetAgentComponent<GameCore.HotUpdate.Battle.Logic.BuffComponent>(lead)
            : null;
        if (buffs == null)
        {
            DsTrace.WriteThrottled("grants", "BuffComponent 不可达，本档 buff 未施加");
            return true;   // 有声明未施加=异常态，仍写行让 trace 可见
        }

        if (spec.Buffs != null)
            foreach (var id in spec.Buffs)
            {
                if (id <= 0) continue;
                buffs.AddBuff(lead.InstanceId, id);
                sb.Append(' ').Append("buff=").Append(id);
            }

        if (spec.PermBuffByTier)
        {
            // 命运眷顾变体：预设档同号 / 自定义档按倍率（spec §5.3）
            var fortune = RewardMath.PickPermBuff(p.Tier, p.RewardMult);
            buffs.AddBuff(lead.InstanceId, fortune);
            sb.Append(" permBuff=").Append(fortune);
        }
        return true;
    }

    private static bool HasBuffs(RewardSpec spec) =>
        (spec.Buffs != null && spec.Buffs.Count > 0) || spec.PermBuffByTier;

    // ———————————————— 属性：一次性五维增减（可负） ————————————————

    private static bool GrantAttrs(RewardSpec spec, StringBuilder sb)
    {
        if (spec.Attrs == null || spec.Attrs.Count == 0) return false;
        var lead = LeadingRole();
        var attrs = lead != null
            ? GameCore.HotUpdate.Battle.Logic.AgentTools
                .GetAgentComponent<GameCore.HotUpdate.Battle.Logic.AttributeComponent>(lead)
            : null;
        if (attrs == null)
        {
            DsTrace.WriteThrottled("grants", "AttributeComponent 不可达，本档属性增减未生效");
            return true;   // 有声明未生效=异常态，仍写行让 trace 可见
        }

        foreach (var a in spec.Attrs)
        {
            if (a == null) continue;
            var delta = (int)Math.Round(a.Delta, MidpointRounding.AwayFromZero);   // 目录全整数；小数防御取整
            attrs.AddConditionValue((GameCore.HotUpdate.AttrName)a.AttrId, delta);
            sb.Append(' ').Append("attr").Append(a.AttrId).Append(delta >= 0 ? "+" : "").Append(delta);
        }
        return true;
    }

    // ———————————————— 主角访问（WelcomeTip/EligibilityChecker 在役模式） ————————————————

    /// <summary>主角（LeadingRole）：BattleLogicWorld._AgentManager.GetLeadingRole()；
    /// 不可达=null（调用方按通道缺失处理）。</summary>
    private static GameCore.HotUpdate.Battle.Logic.LeadingRole LeadingRole()
    {
        try
        {
            return EligibilityChecker.World()?._AgentManager?.GetLeadingRole();
        }
        catch { return null; }
    }
}
