using System;
using System.Collections.Generic;
using System.Text;
using SurvivalLog.ModShared;
using AttrName = GameCore.HotUpdate.AttrName;
using AttributeComponent = GameCore.HotUpdate.Battle.Logic.AttributeComponent;
using BattleLogicWorld = GameCore.HotUpdate.Battle.Logic.BattleLogicWorld;
using BuffComponent = GameCore.HotUpdate.Battle.Logic.BuffComponent;
using ConfigManager = GameCore.HotUpdate.ConfigManager;
using Furniture = GameCore.HotUpdate.Battle.Logic.Furniture;
using FurnitureDurabilityComponent = GameCore.HotUpdate.Battle.Logic.FurnitureDurabilityComponent;
using ItemData = GameCore.HotUpdate.Battle.Logic.ItemData;
using ItemManager = GameCore.HotUpdate.Battle.Logic.ItemManager;

namespace SurvivalLog.DailySurprise;

/// <summary>惩罚执行器（Task 9；RewardExecutor 分流 Op!=None 到此）。
/// 六操作一私有方法+入口 switch，全 try/catch 分层隔离（单台/单件失败不连坐，整体异常也不抛出——
/// 返回"落空：原因"Summary）；每次 Apply 写 trace\punish.txt 一行汇总（RewardExecutor 的
/// grants 行同时携带 Summary）。
/// gamekb 取证（2026-11-05_26454866 dump.cs）：
/// - FurnitureDurabilityComponent dump.cs:374067：GetCurrentDurability/GetMaxDurability（:374122 段）、
///   **SetCurrentDurability(int) public**（:374101 段）——直改无副作用；TakeDamage 带 deathCauseKey
///   （毁损通知/崩溃支线）不用，**只扣不毁：SetCurrentDurability(max(1, cur−扣量)) 下限 1**；
/// - 家具池=AgentManager.GetAllFurnitures()（:379587）筛有 FurnitureDurabilityComponent 者
///   （2026-11-22 实测定案：唯防御设施——沙包/尖刺/电网/电锯——与门窗有耐久组件，普通家具/
///   电器没有；原 electricalOnly 电器限定恒空池已退役）；主角=GetLeadingRole()（:379458）；
/// - 永久属性=AttributeComponent.AddStrengtheningValue(AttrName, int, bool isClearHistory=false)
///   （:371515，public int 重载；SetBaseValue_Int/Float 均 private 不可用——强化层负值即永久扣、
///   AttributeComponent 实现 IComponentSave 存档随行）；五维池=AttrName 上限系 101~105
///   （:57466：MaxSatiety=101/MaxMorale=102/MaxStamina=103/MaxHealth=104/MaxVitality=105）；
/// - buff=BuffComponent.AddBuff(long releaserId, int configId)（:372269，RewardExecutor 同款自发）；
/// - 物品=ItemManager.CostItem(long ownerId, long itemId=实例Id, int count, bool allowProtectedCategory=false)
///   （:425131，栈内精准扣 count 件）；TryAddItemToBag 18 参（:425146，RewardExecutor 同款含满保鲜）；
///   GetItemDataList(ownerId)（:425101）/GetLeadingRoleItemCount(cfgId)（:425170）；
/// - 生鲜=Config_Item.Life（:40381，天）+ It_Rot_Product_Id（:40383，List&lt;int&gt; 首项=腐坏产物），
///   未腐坏=ItemData.TimeLeft&gt;0（:424047，EligibilityChecker.HasFreshFood 同口径）；
///   腐坏产物（如 15501 腐烂物）自身 Life&lt;=0 → 按 0/0 不可腐入包。
/// 纯数学（ScaleLoss/PlanPermAttrLoss/ScaleCount）全在 Core/RewardMath（Tests 源链接钉死）。</summary>
internal static class PunishmentOps
{
    /// <summary>Apply 入口：op 分流 + 一行汇总写 trace\punish.txt。契约=**永不抛出**（RewardExecutor
    /// 裸调不设防）——异常兜成"落空：执行异常"。lossMult≤0 防御归 1.0（Resolve 产出恒 &gt;0；
    /// PendingFlusher 补发档 LossMult 缺省 0 且 Op 不会入挂账，纯保险）。</summary>
    internal static string Apply(SpecialOp op, double opParam, double lossMult, Random rand)
    {
        string summary;
        try
        {
            if (lossMult <= 0) lossMult = 1.0;
            summary = op switch
            {
                SpecialOp.DurabilityPercentRandom => OpDurabilityRandom(opParam, lossMult, rand),
                SpecialOp.DurabilityPercentAll => OpDurabilityAll(opParam, lossMult),
                SpecialOp.DurabilityPercentRange => OpDurabilityRange(opParam, lossMult, rand),
                SpecialOp.AttrPermRandom => OpAttrPerm(lossMult, rand),
                SpecialOp.RotFood => OpRotFood(opParam),
                SpecialOp.StealParts => OpStealParts(opParam, lossMult),
                _ => "落空：未知操作 " + op,
            };
        }
        catch (Exception e)
        {
            summary = "落空：执行异常 " + e.Message;
            DsTrace.WriteThrottled("punish", "Apply(" + op + ") 异常: " + e.Message);
        }
        DsTrace.Write("punish", op + " opParam=" + opParam.ToString("0.###") + " lossMult=" + lossMult.ToString("0.###")
            + " → " + summary);
        return summary;
    }

    // ———————————————— 耐久三操作（M1/M3/H2/D2） ————————————————

    /// <summary>M1 老鼠的牙：随机 1 处耐久组件家具（防御设施/门窗，DurabilityPool 同口径），
    /// 耐久扣 max×pct（pct 先 ScaleLoss；扣量=max×pct/100 四舍五入取整）。查不到=落空。</summary>
    private static string OpDurabilityRandom(double opParam, double lossMult, Random rand)
    {
        var pct = RewardMath.ScaleLoss(opParam, lossMult);
        var pool = DurabilityPool();
        if (pool.Count == 0) return "落空：屋内无可扣耐久的防御设施/门窗";

        var detail = new StringBuilder();
        var total = 0;
        HitDurability(pool[rand.Next(pool.Count)], pct, detail, ref total);
        return "耐久-" + pct.ToString("0.###") + "% 防御设施/门窗×1（实扣" + total + "）" + detail;
    }

    /// <summary>M3 冰雹：全屋家具逐台扣 max×pct。</summary>
    private static string OpDurabilityAll(double opParam, double lossMult)
    {
        var pct = RewardMath.ScaleLoss(opParam, lossMult);
        var pool = DurabilityPool();
        if (pool.Count == 0) return "落空：屋内无可扣耐久的防御设施/门窗";

        var detail = new StringBuilder();
        var total = 0;
        var hit = 0;
        foreach (var f in pool)
        {
            try { if (HitDurability(f, pct, detail, ref total)) hit++; }
            catch (Exception e) { DsTrace.WriteThrottled("punish", "耐久逐台异常（跳过该台）: " + e.Message); }
        }
        if (hit == 0) return "落空：全屋家具耐久均未扣成";
        return "耐久-" + pct.ToString("0.###") + "% 全屋" + hit + "/" + pool.Count + "台（实扣合计" + total + "）" + detail;
    }

    /// <summary>H2 管道爆裂/D2 雷击：解码台数区间与 pct（EventCatalog.DecodeDurabilityRange 公开形式），
    /// 台数 rand.Next(min,max+1)，随机抽 N 台（部分 Fisher-Yates 无放回）逐台扣。实有不足 N=全打（如实记）。</summary>
    private static string OpDurabilityRange(double opParam, double lossMult, Random rand)
    {
        EventCatalog.DecodeDurabilityRange(opParam, out var minCount, out var maxCount, out var pctBase);
        var pct = RewardMath.ScaleLoss(pctBase, lossMult);
        var pool = DurabilityPool();
        if (pool.Count == 0) return "落空：屋内无可扣耐久的防御设施/门窗";

        var n = rand.Next(minCount, maxCount + 1);
        if (n > pool.Count) n = pool.Count;   // 台数超实有=全打
        for (var i = 0; i < n; i++)           // 无放回：前 n 位即命中（swap 抽样）
        {
            var j = rand.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var detail = new StringBuilder();
        var total = 0;
        var hit = 0;
        for (var i = 0; i < n; i++)
        {
            try { if (HitDurability(pool[i], pct, detail, ref total)) hit++; }
            catch (Exception e) { DsTrace.WriteThrottled("punish", "耐久逐台异常（跳过该台）: " + e.Message); }
        }
        if (hit == 0) return "落空：" + minCount + "~" + maxCount + "台抽样均未扣成";
        return "耐久-" + pct.ToString("0.###") + "% " + minCount + "~" + maxCount + "台抽" + n
            + "（实扣" + hit + "台合计" + total + "）" + detail;
    }

    /// <summary>可扣耐久家具池（2026-11-23 scan 实测二次收窄）：GetAllFurnitures() → cfgId ∈
    /// Config_TowerDevice 表（24 ID=门窗 30000~35002 + 防御设施 36001~36004 + 原生门窗 200/211/
    /// 212/368/369/9094/9112/8xxxx——被丧尸攻击的"塔防装置"体系）且有耐久组件者。scan 实测全屋
    /// 148 件普通家具也都挂组件（max=1000 隐形耐久、游戏内无可见意义），按全部打=满屏提示且语义
    /// 错——收窄为玩家可见耐久的门窗+防御设施。表不可达=池空（安全向，EligibilityChecker 同方向）。</summary>
    private static List<Furniture> DurabilityPool()
    {
        var result = new List<Furniture>();
        var towerDict = SurvivalLog.ModShared.BagTable.Cm()?._Config_TowerDevice_Dict;
        if (towerDict == null) return result;
        var list = EligibilityChecker.World()?._AgentManager?.GetAllFurnitures();
        if (list == null) return result;

        var scanned = 0;
        foreach (var f in list)
        {
            if (f == null) continue;
            if (++scanned > 1000) break;
            try
            {
                if (!towerDict.ContainsKey(f.AgentConfigId)) continue;   // 唯门窗+防御设施
                if (GameCore.HotUpdate.Battle.Logic.AgentTools.GetAgentComponent<FurnitureDurabilityComponent>(f) == null)
                    continue;
                result.Add(f);
            }
            catch (Exception e)
            {
                DsTrace.WriteThrottled("punish", "耐久池逐件查询异常（跳过该件）: " + e.Message);
            }
        }
        return result;
    }

    /// <summary>单台扣耐久：扣量=round(max×pct/100)，SetCurrentDurability(max(1, cur−扣量))——
    /// 只扣不毁（下限 1）。detail 截长保险丝（400 字符后只累计不罗列）。返回是否实际扣成。</summary>
    private static bool HitDurability(Furniture f, double pct, StringBuilder detail, ref int totalDamage)
    {
        var dura = GameCore.HotUpdate.Battle.Logic.AgentTools.GetAgentComponent<FurnitureDurabilityComponent>(f);
        if (dura == null) return false;

        var max = dura.GetMaxDurability();
        var cur = dura.GetCurrentDurability();
        var dmg = (int)Math.Round(max * pct / 100.0, MidpointRounding.AwayFromZero);
        var after = Math.Max(1, cur - dmg);
        dura.SetCurrentDurability(after);
        totalDamage += cur - after;
        if (detail.Length < 400)
            detail.Append(' ').Append(f.AgentConfigId).Append(':').Append(cur).Append("→").Append(after);
        return true;
    }

    // ———————————————— D1 代价：永久属性 / 行运低潮替补（spec §8.9） ————————————————

    /// <summary>D1：AllowPermanentNegative=true → 强化层随机一项五维上限属性 −round(10×mult)
    ///（AddStrengtheningValue 负值=永久、存档随行）；false → 不扣属性，施加 PickSlump(lossMult)
    /// 行运低潮变体（自然链已由 LuckEngine 替补 D1→D2 兜住，本分流兜掷骰后、确认前开关被关掉的路径）。
    /// 分流计划纯函数=RewardMath.PlanPermAttrLoss（Tests 种子化钉死）。</summary>
    private static string OpAttrPerm(double lossMult, Random rand)
    {
        var lead = EligibilityChecker.World()?._AgentManager?.GetLeadingRole();
        if (lead == null) return "落空：主角不可达";

        var plan = RewardMath.PlanPermAttrLoss(DsConfig.PermNegativeAllowed, lossMult, rand);
        if (!plan.UseAttrLoss)
        {
            var buffs = GameCore.HotUpdate.Battle.Logic.AgentTools.GetAgentComponent<BuffComponent>(lead);
            if (buffs == null) return "落空：BuffComponent 不可达（行运低潮未施加）";
            buffs.AddBuff(lead.InstanceId, plan.SlumpBuffId);
            return "AllowPermanentNegative=false 替补：行运低潮 buff=" + plan.SlumpBuffId;
        }

        var attrs = GameCore.HotUpdate.Battle.Logic.AgentTools.GetAgentComponent<AttributeComponent>(lead);
        if (attrs == null) return "落空：AttributeComponent 不可达";
        attrs.AddStrengtheningValue((AttrName)plan.AttrId, plan.Loss);
        return "attr" + plan.AttrId + " " + plan.Loss + "（强化层永久，存档随行）";
    }

    // ———————————————— M4 变质：生鲜腐坏替换 ————————————————

    /// <summary>M4：主角背包挑 Config_Item.Life 最短且未腐坏（TimeLeft&gt;0）食物最多 opParam 件
    ///（Life 同长按实例 Id 稳定排序），逐件替换为 It_Rot_Product_Id[0] 腐坏产物：CostItem 精准扣
    /// 1 件（栈安全）→ TryAddItemToBag 1:1 补腐坏产物（入包失败如实标注——食物照腐）。</summary>
    private static string OpRotFood(double opParam)
    {
        var world = EligibilityChecker.World();
        var im = world?._ItemManager;
        var owner = EligibilityChecker.PlayerBagOwnerId();
        var cm = BagTable.Cm();
        if (im == null || owner <= 0 || cm == null) return "落空：物品通道不可达";

        var maxCount = (int)Math.Round(opParam, MidpointRounding.AwayFromZero);
        if (maxCount <= 0) return "落空：opParam 非法 " + opParam;

        var candidates = new List<(ItemData item, int life, int rotId)>();
        var scanned = 0;
        var items = im.GetItemDataList(owner);
        if (items != null)
        {
            foreach (var it in items)
            {
                if (it == null) continue;
                if (++scanned > 2000) break;   // 病态长表保险丝（EligibilityChecker 同款）
                try
                {
                    var cfg = cm.Get_Config_Item(it.ItemConfigId);
                    if (cfg == null || cfg.Life <= 0) continue;   // 不可腐
                    var rot = cfg.It_Rot_Product_Id;
                    if (rot == null || rot.Count == 0) continue;  // 无腐坏产物
                    if (it.TimeLeft <= 0) continue;               // 已腐坏
                    candidates.Add((it, cfg.Life, rot[0]));
                }
                catch (Exception e)
                {
                    DsTrace.WriteThrottled("punish", "生鲜逐件查询异常（跳过该件）: " + e.Message);
                }
            }
        }
        if (candidates.Count == 0) return "落空：背包无未腐坏食物";

        candidates.Sort((a, b) => a.life != b.life
            ? a.life.CompareTo(b.life)
            : a.item.InstanceId.CompareTo(b.item.InstanceId));
        if (candidates.Count > maxCount) candidates.RemoveRange(maxCount, candidates.Count - maxCount);

        var detail = new StringBuilder();
        var done = 0;
        foreach (var (item, _, rotId) in candidates)
        {
            try
            {
                im.CostItem(owner, item.InstanceId, 1);   // 移除食物 1 件
                var added = TryAddRot(world, im, cm, owner, rotId);   // 补腐坏产物 1:1
                done++;
                detail.Append(' ').Append(item.ItemConfigId).Append("→").Append(rotId)
                    .Append(added ? "" : "(产物入包失败)");
            }
            catch (Exception e)
            {
                DsTrace.WriteThrottled("punish", "腐坏替换异常（跳过该件）: " + e.Message);
            }
        }
        if (done == 0) return "落空：腐坏替换均未成功";
        return "腐坏" + done + "/" + maxCount + "件（Life 最短优先）" + detail;
    }

    /// <summary>腐坏产物入包（RewardExecutor.GrantItems 同款满保鲜规则：cfg.Life&gt;0 →
    /// startTime=now 小时/timeLeft=Life×24；不可腐 0/0。腐烂物等产物通常 Life≤0=原样入包）。</summary>
    private static bool TryAddRot(BattleLogicWorld world, ItemManager im, ConfigManager cm, long owner, int cfgId)
    {
        var startTime = 0;
        var timeLeft = 0;
        try
        {
            var cfg = cm.Get_Config_Item(cfgId);
            if (cfg != null && cfg.Life > 0)
            {
                startTime = (int)GameCore.HotUpdate.Battle.Logic.RobotSortingManager.GetNowHours(world);
                timeLeft = cfg.Life * 24;
            }
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("punish", "腐坏产物保质期读取异常（按不可腐发放）cfg=" + cfgId + ": " + e.Message);
        }
        try
        {
            return im.TryAddItemToBag(owner, cfgId, 1, startTime, timeLeft,
                0, 1f, null, 0, null, null, timeLeft, 0, false, false, false, false, 0);
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("punish", "TryAddItemToBag(" + cfgId + ") 抛异常: " + e.Message);
            return false;
        }
    }

    // ———————————————— H3 贼影：偷零件 ————————————————

    /// <summary>H3：扣铁制零件(20362) ScaleCount(round(opParam×lossMult))（基准 10；7.5→8 AwayFromZero）。
    /// **当前无调用方，保留能力**（Task 10 Controller 裁定）：H3 是唯一引用方且已被选项声明式短路
    /// （RewardSpec.RunsTopLevelOp=false，零件改在"算了"子事件 99299 声明式 ItemCost 扣）；
    /// Op 枚举与本实现保留，防数据层未来复用（DESIGN.md §10.1）。
    /// 逐栈 CostItem（实例 Id 口径）——持有不足扣到 0 为止；CostItem 返回 void，**扣后重读主角持有数**
    /// （held−after）如实记 Summary（请求量与实扣量分开陈述）。</summary>
    private static string OpStealParts(double opParam, double lossMult)
    {
        var im = EligibilityChecker.World()?._ItemManager;
        var owner = EligibilityChecker.PlayerBagOwnerId();
        if (im == null || owner <= 0) return "落空：物品通道不可达";

        var want = RewardMath.ScaleCount((int)Math.Round(opParam, MidpointRounding.AwayFromZero), lossMult);
        var held = im.GetLeadingRoleItemCount(EligibilityChecker.PartsItemId);
        if (held <= 0) return "落空：主角无铁制零件";

        var taken = 0;
        var items = im.GetItemDataList(owner);
        if (items != null)
        {
            var scanned = 0;
            foreach (var it in items)
            {
                if (taken >= want) break;
                if (it == null || it.ItemConfigId != EligibilityChecker.PartsItemId || it.ItemCount <= 0) continue;
                if (++scanned > 500) break;   // 病态长表保险丝
                try
                {
                    var take = Math.Min(want - taken, it.ItemCount);
                    im.CostItem(owner, it.InstanceId, take);
                    taken += take;
                }
                catch (Exception e)
                {
                    DsTrace.WriteThrottled("punish", "CostItem 零件异常（跳过该栈）: " + e.Message);
                }
            }
        }

        var after = held;
        try { after = im.GetLeadingRoleItemCount(EligibilityChecker.PartsItemId); } catch { }
        var actual = Math.Max(0, held - after);
        if (actual <= 0) return "落空：零件扣减未生效（持有" + held + "，请求" + want + "）";
        return "零件-" + actual + "/" + want + "（持有" + held + "→" + after
            + (taken < want ? "，不足扣尽" : "") + "）";
    }
}
