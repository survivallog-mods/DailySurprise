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

/// <summary>选项子事件（Task 10 接线）：选项按钮 → 独立 Config_Event 行（原生 10011/104 形态：
/// Type=2/IsSon=true/PopUpType=1，RewardSetId 声明式发奖）。Id 段 99245~99249（事件段
/// 99200~99244 顺延）；Line=结果卡一句文案；顺序契约=与父事件 Choices 同序（父行 ChoseID/Default
/// 与子事件 RewardSetId→选项 RewardSet 行的映射都按此派生）。</summary>
public sealed class SonEvent
{
    public int Id;
    public int ParentId;
    public string Line;
}

/// <summary>事件奖励声明（注入 Config_RewardSet 行 + 代码路径特效）。
/// PermBuffByTier=true 表示 Buffs 不固定、按档位/倍率在运行时选变体（Task 8 VariantMath）；
/// RandomPickOne=true 表示 Items 是候选池、只随机发放 1 件（spec §8 R2/E1/E3/E4/L2 的「随机×1」，
/// 与 PermBuffByTier 同款"数据声明 + 执行方解释"分工，Task 8 RewardExecutor 消费）；
/// ItemCosts=声明式扣件（Config_RewardSet.ItemCost，选项行专用——H3「算了」失去零件×10，
/// Task 10；基础行不承载，代码路径扣减仍归 Op 系）。</summary>
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
    public List<ItemGain> ItemCosts;

    /// <summary>顶层 Op 是否由代码路径执行（Task 10 防双发）：选项事件（Choices 非空）的顶层
    /// 效果由子事件 RewardSet 声明式结算——H3 的 StealParts 体现在「算了」选项 ItemCost，
    /// 代码侧（自然链路与 ForceGrant 调试链路）一律跳过。Choices null 防御=按无选项处理。</summary>
    public bool RunsTopLevelOp => Op != SpecialOp.None && (Choices == null || Choices.Count == 0);
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
    public static List<int> ItemChangeOf(RewardSpec reward) => Flatten(reward?.Items);

    /// <summary>ItemCost 同款编码（Task 10；原生行 68252 实证：itemId 重复=件数，如
    /// [20210,20210,20210,20372,20372]=3×20210+2×20372）。</summary>
    public static List<int> ItemCostOf(RewardSpec reward) => Flatten(reward?.ItemCosts);

    private static List<int> Flatten(List<ItemGain> gains)
    {
        var flat = new List<int>();
        if (gains == null) return flat;
        foreach (var g in gains)
        {
            if (g == null || g.Count <= 0) continue;
            for (var i = 0; i < g.Count; i++) flat.Add(g.ItemId);
        }
        return flat;
    }
}
