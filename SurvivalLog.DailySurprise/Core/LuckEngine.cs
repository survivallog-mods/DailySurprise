using System;
using System.Collections.Generic;

namespace SurvivalLog.DailySurprise;

/// <summary>掷骰存档态（spec §5.1；由调用方在每日结算时更新，引擎只读不写）。
/// BadStreak=连败天数（保底判定用）；YesterdayEventId=防连抽对照；
/// PermNegativeUsed=永久负面已用次数；DisasterCount=统计字段（引擎不消费，Task 5+ 维护）；
/// PendingRewards=待发放奖励挂账（Task 5 加，StateStore 往返；引擎不消费）。</summary>
public sealed class RollState
{
    public int LastTriggeredDay;
    public int BadStreak;
    public int PermNegativeUsed;
    public int YesterdayEventId;
    public int DisasterCount;

    /// <summary>挂账奖励（默认空列表非 null；序列化忽略 null，Load 对缺字段旧档归一为空列表）。</summary>
    public List<PendingReward> PendingRewards = new();
}

/// <summary>掷骰结果。EventId=0 是落空标记（好运/平淡整层无资格时返回，调用方自行处理如平淡静默）；
/// RarityIndex=实际使用的稀有度层下标（霉运降层后即降层结果）。</summary>
public struct RollOutcome
{
    public LuckKind Kind;
    public int RarityIndex;
    public int EventId;
    public bool PityUsed;
    public bool Substituted;
}

/// <summary>掷骰引擎（纯逻辑，spec §5.1/§5.4 八条语义；引擎不查事件目录——候选集与全部谓词注入，可种子化测试）。
/// 稀有度层下标约定（Task 4 EventCatalog 消费）：好运 0..3=普/稀/史/传；霉运 0..3=轻/中/重/灾厄；平淡 0..4=N1..N5。
/// 随机消费只经 rand.NextDouble（逐次可数：保底省一次、防连抽重掷恰好多一次）。</summary>
public static class LuckEngine
{
    private static readonly double[] s_plainLayerWeights = { 35, 20, 15, 15, 15 };

    /// <summary>平淡档内 N 层权重（spec §8.1：N1=35、N2=20、N3=N4=N5=15）。
    /// 访问返回防御性副本（T3 遗留 Minor 修复：public static readonly 数组可被外部写污染）；
    /// 引擎内部消费私有原数组，roll 热路径零克隆开销。</summary>
    public static double[] PlainLayerWeights => (double[])s_plainLayerWeights.Clone();

    /// <summary>每日一掷（spec §5.1 主流程，逐条）：
    /// 1 保底（BadStreak≥PityDays 且 PityDays&gt;0 → 强制好运，跳过运势掷骰）；
    /// 2 按 Good/Plain/Bad 比例掷运势档；3 档内按 Rarity 权重掷稀有度层；
    /// 4/6 资格过滤后层内按 Weight 加权选；5 选中昨日事件 → 同候选集重掷一次（仍中则接受）；
    /// 6 后半 霉运整层无资格 → 逐层下调（灾厄→重→中→轻，轻度层调用方保证恒有资格）；
    /// 好运/平淡层空 → 落空标记（EventId=0）；
    /// 7 永久负面达上限（isPermNegative 且 PermNegativeUsed≥PermNegativeCap）→ substitute 替补（替补不再过检查）。</summary>
    public static RollOutcome Roll(
        DifficultyParams p,
        RollState s,
        IReadOnlyList<EventDef> candidates,
        Func<int, bool> eligible,
        Func<int, bool> isPermNegative,
        Func<int, int> substitute,
        Random rand)
    {
        // 1. 保底
        var pity = p.PityDays > 0 && s.BadStreak >= p.PityDays;
        var kind = pity ? LuckKind.Good : RollKind(p, rand);   // 2. 运势档掷骰

        // 3. 档内稀有度层掷骰（数组=权重，和不必为 1）
        var rarity = PickWeighted(LayerWeights(p, kind), rand);

        // 4/5/6. 资格过滤 → 层内加权选 → 防连抽重掷一次；霉运整层空逐层下调
        var chosen = PickEvent(kind, rarity, candidates, eligible, s, rand);
        if (chosen == null && kind == LuckKind.Bad)
        {
            while (chosen == null && rarity > 0)
            {
                rarity--;
                chosen = PickEvent(kind, rarity, candidates, eligible, s, rand);
            }
        }

        if (chosen == null)
            return new RollOutcome { Kind = kind, RarityIndex = rarity, EventId = 0, PityUsed = pity };

        // 7. 永久负面上限 → 替补
        var eventId = chosen.Id;
        var substituted = false;
        if (isPermNegative(eventId) && s.PermNegativeUsed >= p.PermNegativeCap)
        {
            eventId = substitute(eventId);
            substituted = true;
        }

        return new RollOutcome
        {
            Kind = kind,
            RarityIndex = rarity,
            EventId = eventId,
            PityUsed = pity,
            Substituted = substituted,
        };
    }

    private static double[] LayerWeights(DifficultyParams p, LuckKind kind) =>
        kind == LuckKind.Good ? p.GoodRarity : kind == LuckKind.Bad ? p.BadRarity : s_plainLayerWeights;

    /// <summary>运势档掷骰：Good→Plain→Bad 累计段（权重 0 的轴永不被掷中）。</summary>
    private static LuckKind RollKind(DifficultyParams p, Random rand)
    {
        var roll = rand.NextDouble() * (p.Good + p.Plain + p.Bad);
        if (roll < p.Good)
            return LuckKind.Good;
        if (roll < p.Good + p.Plain)
            return LuckKind.Plain;
        return LuckKind.Bad;
    }

    /// <summary>层内选事件：先剔除异档/异层/零权重/无资格候选，再按 Weight 加权选；
    /// 选中昨日事件 → 同候选集（已过滤）重掷一次，仍中则接受（单事件层自然落此分支）。</summary>
    private static EventDef PickEvent(LuckKind kind, int rarity, IReadOnlyList<EventDef> candidates,
        Func<int, bool> eligible, RollState s, Random rand)
    {
        List<EventDef> pool = null;
        if (candidates != null)
        {
            foreach (var c in candidates)
            {
                if (c == null || c.Kind != kind || c.Rarity != rarity || c.Weight <= 0 || !eligible(c.Id))
                    continue;
                (pool ??= new List<EventDef>()).Add(c);
            }
        }
        if (pool == null || pool.Count == 0)
            return null;

        var pick = PickWeighted(pool, rand);
        if (pick.Id == s.YesterdayEventId)
            pick = PickWeighted(pool, rand);   // 5. 防连抽：重掷一次
        return pick;
    }

    /// <summary>加权选下标（数组语义）：roll∈[0,total) 累计扣减，零/负权重项永不中；
    /// 全零数组（调用方配置错误）兜底下标 0 且不消耗随机数。</summary>
    private static int PickWeighted(double[] weights, Random rand)
    {
        var total = 0d;
        var first = -1;
        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0)
                continue;
            if (first < 0)
                first = i;
            total += weights[i];
        }
        if (first < 0)
            return 0;

        var roll = rand.NextDouble() * total;
        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0)
                continue;
            roll -= weights[i];
            if (roll < 0)
                return i;
        }
        return first;   // 浮点右边界兜底
    }

    /// <summary>池内按 Weight 加权选事件（池已保证非空且 Weight&gt;0）。</summary>
    private static EventDef PickWeighted(List<EventDef> pool, Random rand)
    {
        var total = 0d;
        foreach (var e in pool)
            total += e.Weight;

        var roll = rand.NextDouble() * total;
        foreach (var e in pool)
        {
            roll -= e.Weight;
            if (roll < 0)
                return e;
        }
        return pool[^1];   // 浮点右边界兜底
    }
}
