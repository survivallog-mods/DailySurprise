using System.Collections.Generic;

namespace SurvivalLog.DailySurprise;

/// <summary>运势档位（纯逻辑层）。</summary>
public enum LuckKind { Plain, Good, Bad }

/// <summary>事件资格门（玩家家底条件，纯逻辑层；判定逻辑 Task 6+ 落地）。
/// HasFurnitureAtLeast1=屋内有耐久组件家具（2026-11-22 实测定案：唯防御设施——沙包/尖刺/
/// 电网/电锯——与门窗有 FurnitureDurabilityComponent，普通家具/电器没有；HasAppliance
/// 电器门因电器恒无耐久组件而退役删除）。</summary>
public enum Eligibility { None, HasFurnitureAtLeast1, HasFreshFood, HasParts10 }

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

/// <summary>事件奖励声明（T18 起全载荷注入 Config_RewardSet/Config_RandomGroup 行，由原生
/// RewardSetManager 声明式结算；仅 Op 系特效保留代码路径——PunishmentOps）。
/// PermBuffByTier=true 表示 Buffs 不固定、按档位/倍率选变体（注入时 RewardMath.PickPermBuff 烘焙）；
/// RandomPickOne=true 表示 Items 是候选池、只随机发放 1 件（spec §8 R2/E1/E3/E4/L2 的「随机×1」，
/// T18 起由 Config_RandomGroup 组行声明式承载，注入时件数按倍率烘焙进 CountList）；
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
    /// 代码侧不再执行顶层 Op。Choices null 防御=按无选项处理。</summary>
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
/// T18 奖励原生上卡：件数在注入时按 rewardMult 烘焙（RewardMath.ScaleCount：round(base×mult)
/// 下限 1——与旧代码路径"Grant 时缩放"同语义，行内容即最终发放量）。
/// 注：Items 若是 RandomPickOne 候选池（spec §8「随机×1」），ItemChange 不展开——
/// 随机语义由 Config_RandomGroup 组行声明式承载（InjectorMapping.RandomGroupRows）。</summary>
public static class RewardEncoding
{
    public static List<int> ItemChangeOf(RewardSpec reward, double rewardMult = 1.0) =>
        Flatten(reward?.Items, rewardMult);

    /// <summary>ItemCost 同款编码（Task 10；原生行 68252 实证：itemId 重复=件数，如
    /// [20210,20210,20210,20372,20372]=3×20210+2×20372）。不烘焙倍率（扣件=固定代价）。</summary>
    public static List<int> ItemCostOf(RewardSpec reward) => Flatten(reward?.ItemCosts, 1.0);

    private static List<int> Flatten(List<ItemGain> gains, double rewardMult)
    {
        var flat = new List<int>();
        if (gains == null) return flat;
        foreach (var g in gains)
        {
            if (g == null || g.Count <= 0) continue;
            var count = RewardMath.ScaleCount(g.Count, rewardMult);
            for (var i = 0; i < count; i++) flat.Add(g.ItemId);
        }
        return flat;
    }
}
