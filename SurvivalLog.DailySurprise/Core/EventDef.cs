using System.Collections.Generic;

namespace SurvivalLog.DailySurprise;

/// <summary>运势档位（纯逻辑层）。</summary>
public enum LuckKind { Plain, Good, Bad }

/// <summary>事件资格门（玩家家底条件，纯逻辑层；判定逻辑 Task 6+ 落地）。</summary>
public enum Eligibility { None, HasAppliance, HasFurnitureAtLeast1, HasFreshFood, HasParts10 }

/// <summary>RewardSet 覆盖不了、走代码路径的特殊操作（spec §7.2 RewardExecutor）。</summary>
public enum SpecialOp
{
    None,
    DurabilityPercentRandom,
    DurabilityPercentAll,
    DurabilityPercentRange,
    AttrPermRandom,
    RotFood,
    StealParts,
}

/// <summary>物品获得（ItemId=Config_Item 主键，Count=件数）。</summary>
public sealed class ItemGain
{
    public int ItemId;
    public int Count;
}

/// <summary>属性变化（AttrId=Config_AttributeStandard 键，Delta=增量可负）。</summary>
public sealed class AttrChange
{
    public int AttrId;
    public double Delta;
}

/// <summary>选项（原生选项 UI 的一个按钮；Label 进 ChoseName_Local，Reward 进子事件 RewardSet）。</summary>
public sealed class Choice
{
    public string Label;
    public RewardSpec Reward;
}

/// <summary>事件奖励声明（注入 Config_RewardSet 行 + 代码路径特效）。
/// PermBuffByTier=true 表示 Buffs 不固定、按档位/倍率在运行时选变体（Task 8 VariantMath）；
/// RandomPickOne=true 表示 Items 是候选池、只随机发放 1 件（spec §8 R2/E1/E3/E4/L2 的「随机×1」，
/// 与 PermBuffByTier 同款"数据声明 + 执行方解释"分工，Task 8 RewardExecutor 消费）。</summary>
public sealed class RewardSpec
{
    public List<ItemGain> Items;
    public List<int> Buffs;
    public List<AttrChange> Attrs;
    public SpecialOp Op;
    public double OpParam;
    public List<Choice> Choices;
    public bool PermBuffByTier;
    public bool RandomPickOne;
}

/// <summary>事件定义（EventCatalog 的行类型；台词/图/效果/档位/权重全声明式，spec §7.1 载体选型A）。</summary>
public sealed class EventDef
{
    public int Id;
    public string Key;
    public LuckKind Kind;
    public int Rarity;
    public string Title;
    public string Dialogue;
    public string Icon;
    public RewardSpec Reward;
    public Eligibility Elig;
    public int Weight;
}

/// <summary>RewardSpec → Config_RewardSet.ItemChange 的编码规则（纯逻辑，Game/TableInjector 消费）：
/// 原生表语义= itemId 逐次重复出现表件数（非 [id,count] 对）——gamekb Config_RewardSet 行 6420
/// 实证（ItemChange=[2107,2107,2107,2209,2209,...]=3×2107+2×2209+…）。Count&lt;=0 不展开。
/// 注：Items 若是 RandomPickOne 候选池（spec §8「随机×1」），展开结果仅供直发行——
/// 随机择一语义由 Task 8 RewardExecutor 消费 RandomPickOne 标记实现。</summary>
public static class RewardEncoding
{
    public static List<int> ItemChangeOf(RewardSpec reward)
    {
        var flat = new List<int>();
        if (reward?.Items == null) return flat;
        foreach (var g in reward.Items)
        {
            if (g == null || g.Count <= 0) continue;
            for (var i = 0; i < g.Count; i++) flat.Add(g.ItemId);
        }
        return flat;
    }
}
