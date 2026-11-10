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
/// PermBuffByTier=true 表示 Buffs 不固定、按档位/倍率在运行时选变体（Task 8 VariantMath）。</summary>
public sealed class RewardSpec
{
    public List<ItemGain> Items;
    public List<int> Buffs;
    public List<AttrChange> Attrs;
    public SpecialOp Op;
    public double OpParam;
    public List<Choice> Choices;
    public bool PermBuffByTier;
}

/// <summary>事件定义（Task 3 EventCatalog 的行类型；台词/图/效果/档位/权重全声明式，spec §7.1 载体选型A）。</summary>
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

/// <summary>首验 spike 事件（Task 2 专用，Task 3 起 EventCatalog 接管后删除）：天上掉压缩饼干。
/// brief Step 1 逐字：Id=99200、Key="spike"、台词"这是一次测试：天上掉下来一箱压缩饼干。"、
/// Reward.Items=[{2101,4}]（2101=「90压缩饼干」，gamekb Config_Item 实证）。</summary>
public static class SpikeEvent
{
    public static readonly EventDef Instance = new()
    {
        Id = 99200,
        Key = "spike",
        Kind = LuckKind.Good,
        Rarity = 1,
        Title = "喜从天降·首验",
        Dialogue = "这是一次测试：天上掉下来一箱压缩饼干。",
        Icon = SpikeRowTemplate.IconA,
        Reward = new RewardSpec
        {
            Items = new List<ItemGain> { new ItemGain { ItemId = 2101, Count = 4 } },
            Buffs = new List<int>(),
            Attrs = new List<AttrChange>(),
            Op = SpecialOp.None,
            OpParam = 0,
            Choices = new List<Choice>(),
            PermBuffByTier = false,
        },
        Elig = Eligibility.None,
        Weight = 1,
    };
}

/// <summary>spike 注入行的表级常量（Task 2 专用，随 SpikeEvent 一并删除）。
/// 样式字段逐字照抄 gamekb Config_Event.json 原生行（出处见各常量注）：
/// - A（默认，无选项直发，验证A）：原生行 10011「暴雨侵袭-用毛巾堵漏」（Type=2 Result）
///   Type=2 / ShowType=1 / PopUpType=1 / KeepTime=0 / Icon=EventNew/022 / OKDes_Local=我知道了；
/// - B（DebugSpikeChose=true，两选项，验证B）：原生行 103「事件3」（Type=1 Choice）
///   Type=1 / ShowType=1 / PopUpType=3 / KeepTime=0 / Countdown=60 / Default=末选项子事件。
/// 两模板共有：EndlessMode/LogType/DataMin/DataMax/TimeMin/TimeMax/TasteRequire=-1（逐字核对一致）；
/// DemoTwoMode 两模板不一致（10011=2、103=3），按各自模板取。
/// 刻意偏离模板清零的门控字段（保证强制触发不被表内条件挡）：ConditionSetId /
/// NeedConditionSetId / TaskConditionSetId / TaskTime——模板行 10011 带 NeedConditionSetId=13、
/// 103 带 ConditionSetId=3，是自然随机触发的资格门，对首验强制路径只添噪声。</summary>
public static class SpikeRowTemplate
{
    public const int EventId = 99200;
    public const int RewardSetId = 99250;

    // —— 验证 A（无选项，样式源=原生行 10011）——
    public const int StyleSourceRowA = 10011;
    public const int TypeA = 2;         // 10011 _TypeField=2（Result）
    public const int ShowTypeA = 1;     // 10011 _ShowTypeField=1
    public const int PopUpTypeA = 1;    // 10011 _PopUpTypeField=1
    public const int KeepTimeA = 0;     // 10011 _KeepTimeField=0
    public const string IconA = "Assets/RuntimeAssets/Texture/EventNew/022"; // 10011 _IconField
    public const string OkDesA = "我知道了";                                    // 10011 _OKDes_LocalField

    // —— 验证 B（两选项，样式源=原生行 103）——
    public const int StyleSourceRowB = 103;
    public const int TypeB = 1;         // 103 _TypeField=1（Choice）
    public const int ShowTypeB = 1;     // 103 _ShowTypeField=1
    public const int PopUpTypeB = 3;    // 103 _PopUpTypeField=3
    public const int KeepTimeB = 0;     // 103 _KeepTimeField=0
    public const int CountdownB = 60;   // 103 _CountdownField=60（倒计时归零自动走 Default）
    public const string IconB = "Assets/RuntimeAssets/Texture/Literature/UI_Item_Icon_Liter_M_1490200101"; // 103 _IconField
    public static readonly string[] ChoseLabels = { "拿走", "算了" };   // brief Step 4 原文
    // 选项子事件指向原生行（本任务只注入 1 行；10011/10012=原生 10010 的两个 Type=2 IsSon 子事件，
    // 各带自己的 RewardSet 6013/6014——点击选项即触发对应原生子事件，行为有官方定义）
    public static readonly int[] ChoseIds = { 10011, 10012 };
    public const int DefaultChoseId = 10012;    // 103 _DefaultField=105=其末选项，同构取末选项

    // —— 模板间不一致、按各自模板取的字段 ——
    public const int DemoTwoModeA = 2;  // 10011 _DemoTwoModeField=2
    public const int DemoTwoModeB = 3;  // 103 _DemoTwoModeField=3

    // —— 两模板共有值（10011 与 103 字段值相同，逐字核对）——
    public const int EndlessMode = 1;   // 两行均=1
    public const int LogType = 1;       // 两行均=1（DynamicEventLogType.Normal）
    public const int DataMin = 1;       // 两行均=1
    public const int DataMax = 999;     // 两行均=999
    public const int TimeMin = 1;       // 两行均=1
    public const int TimeMax = 24;      // 两行均=24
    public const int TasteRequire = -1; // 两行均=-1（无口味门槛）
}

/// <summary>RewardSpec → Config_RewardSet.ItemChange 的编码规则（纯逻辑，Game/TableInjector 消费）：
/// 原生表语义= itemId 逐次重复出现表件数（非 [id,count] 对）——gamekb Config_RewardSet 行 6420
/// 实证（ItemChange=[2107,2107,2107,2209,2209,...]=3×2107+2×2209+…）。Count&lt;=0 不展开。</summary>
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
