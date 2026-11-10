using System;
using System.Collections.Generic;
using System.Linq;

namespace SurvivalLog.DailySurprise;

/// <summary>注入映射纯函数（Task 6）：EventDef/RewardSpec/BuffBlueprint → 表行字段字典。
/// 字典键=Config_* 属性名（裸名），值=int/double/string/List&lt;int&gt;/List&lt;string&gt;；
/// IL2CPP 写入层（Game/TableInjector）只做"字典→对象属性"搬运，不掺数据决策。
/// 字段名与形态权威=gamekb 2026-11-05_26454866 dump.cs：
/// - Config_Event：35790 起 67 可写属性（ChoseName/ChoseName_Local=List&lt;string&gt;、ChoseID=List&lt;int&gt;、
///   StartRewardSetId=RewardSetId=int，35833/35838/35844 行）；
/// - Config_RewardSet：46003 起 44 可写属性；AttriKeyChange/AttriValueChange=平行 List&lt;int&gt;
///   （键=AttrName 枚举 1..5、值=整数增量可负——原生行 6011=[1]/[8]、6013=[1,3]/[-15,-15] 实证）；
/// - Config_Buff：28302 起 24 可写属性（BuffDuring=float:28321、Overlay=int:28322、
///   EffectConfigId=int:28328；原生行 EffectConfigId=200000000+buffId 惯例）；
/// - Config_Effect：33687 起（BuffInterval=float:33718、BuffMoraleInterval=float:33720、
///   BuffStaminaMax=int:33730、*Add/*Del=float:33733-33742、BuffSpeed=float:33765、
///   FloatValue0=float:33782）。
/// ID 段（Task 6 brief）：RewardSet 基础行=99250+目录序（99250..99294）、选项行 99295..99299
/// （L1×3+H3×2 目录序顺排）、Effect 行=99290+蓝图序（99290..99303，Effect 独立表与
/// buff 99280..99293/RewardSet 99250..99299 数值重叠不冲突——不同表）。
/// RewardSet 行内容口径（brief 定案，别过度设计）：
/// - 固定 Items → ItemChange 编码（RewardEncoding：itemId 重复=件数）；
/// - RandomPickOne（R2/E1/E3/E4/L2）→ 行留空 + Source=DS_RandomByCode（随机×1 由 Task 8 代码路径解）；
/// - Attrs → AttriKeyChange/AttriValueChange（整数增量；小数增量抛错走代码路径）；
/// - Buffs/PermBuffByTier/Op 系 → RewardSet 一律不承载（Task 8/9 代码路径），弹窗行照建。</summary>
public static class InjectorMapping
{
    // —— ID 段与标记常量（Game 层哨兵/计数与测试共用）——
    public const int FirstRewardSetId = 99250;
    public const int OptionRewardSetFirstId = 99250 + 45;   // 99295：L1×3 + H3×2 = 99295..99299
    public const int FirstEffectId = 99290;
    public const string SourceTag = "DailySurprise";         // 正式命名（前 DailySurprise_Spike，Task 2 spike 遗留）
    public const string RandomByCodeTag = "DS_RandomByCode"; // RandomPickOne 行留空标记（Task 8 代码路径解随机）
    public const string IconPrefix = "../../Res/Icon/";      // Task 11 配图落盘约定（DESIGN.md §2.3）

    /// <summary>事件基础 RewardSet 行 Id = 99250 + 目录序（N1=99250 … D3=99294）。</summary>
    public static int BaseRewardSetIdOf(EventDef e)
    {
        var idx = EventCatalog.EventsIndexOf(e);
        if (idx < 0) throw new ArgumentOutOfRangeException(nameof(e), e.Id, "非 EventCatalog 目录事件");
        return FirstRewardSetId + idx;
    }

    /// <summary>Effect 行 Id = 99290 +（NewId−99280）：蓝图序独立编号（Effect 表内 99290..99303）。</summary>
    public static int EffectIdOf(BuffBlueprint b)
    {
        if (b == null) throw new ArgumentNullException(nameof(b));
        return FirstEffectId + (b.NewId - BuffBlueprints.FirstBlueprintId);
    }

    // ———————————————— Event 行 ————————————————

    /// <summary>EventDef → Config_Event 全字段字典（67 键，形态样式抄原生行 10011/103，
    /// 见键值注；选项事件（Choices 非空）用 103 选择样式——ChoseID 留空，子事件接线归 Task 10）。</summary>
    public static Dictionary<string, object> EventRow(EventDef e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        var choice = e.Reward is { Choices.Count: > 0 };
        var labels = choice ? e.Reward.Choices.Select(c => c.Label).ToList() : new List<string>();
        var icon = IconPrefix + e.Icon;

        return new Dictionary<string, object>
        {
            ["ID"] = e.Id,
            ["Name"] = e.Title,                    // 裸中文直写（GetLocalText 键 miss 原样透传，Task 2 实锤）
            ["Name_Local"] = e.Title,
            ["PlayerSelectId"] = new List<int>(),  // 不限角色（模板 10011/103 均未设）
            ["DemoTwoMode"] = choice ? 3 : 2,      // 10011=2、103=3，按各自模板
            ["EndlessMode"] = 1,                   // 两模板均 1
            ["Type"] = choice ? 1 : 2,             // 1=Choice(103)、2=Result(10011)
            ["EventLogID"] = 0,
            ["LogType"] = 1,                       // 两模板均 1（DynamicEventLogType.Normal）
            ["ShowType"] = 1,
            ["PopUpType"] = choice ? 3 : 1,        // 103=3（选项卡）、10011=1
            ["FurnitureID"] = new List<int>(),
            ["KeepTime"] = 0,
            ["IsSon"] = false,                     // 独立事件（目录事件均非子事件）
            ["EventDes"] = e.Dialogue,
            ["EventDes_Local"] = e.Dialogue,
            ["Info"] = "", ["Info_Local"] = "",
            ["DoneInfo"] = "", ["DoneInfo_Local"] = "",
            ["EventDesTaskEnd"] = e.Dialogue,      // 10011 模板：TaskEnd 文案=EventDes 同文
            ["EventDesTaskEnd_Local"] = e.Dialogue,
            ["Icon"] = icon,
            ["IconEnd"] = icon,                    // 10011/103 均与 Icon 同值
            ["IconRole"] = new List<string>(),
            ["DataMin"] = 1,                       // 两模板均 1/999
            ["DataMax"] = 999,
            ["DayRate"] = 0f,
            ["TimeMin"] = 1,                       // 两模板均 1/24
            ["TimeMax"] = 24,
            ["CanRepeat"] = true,                  // 两模板均 true
            ["DayCD"] = choice ? 3 : 1,            // 103 DayCD=3；10011 DayCD=1
            ["HourCD"] = 0,
            ["StartThink"] = "", ["StartThink_Local"] = "",
            ["ChoseName"] = new List<string>(labels),
            ["ChoseName_Local"] = new List<string>(labels),
            ["ChoseID"] = new List<int>(),         // Task 10 接线（数据层 Choices 就绪即可）
            ["Default"] = 0,
            ["Countdown"] = choice ? 60 : 0,       // 103 Countdown=60（倒计时归零走 Default）
            ["ActionRequire"] = new List<int>(), ["ActionTime"] = new List<int>(),
            ["ItemRequire"] = new List<int>(), ["ItemTime"] = new List<int>(),
            ["TaskTime"] = 0,                      // 刻意偏离模板：任务时限不设（Task 2 定案）
            ["OKDes"] = "我知道了",                // 两模板 OKDes_Local 同文
            ["OKDes_Local"] = "我知道了",
            ["OKDoneDes"] = "", ["OKDoneDes_Local"] = "",
            ["DoneThink"] = "", ["DoneThink_Local"] = "",
            ["TriggerFmod"] = "", ["EndFmod"] = "", // 不抄模板音效（与台词无关）
            ["ConditionSetId"] = 0,                 // 刻意清零门控（Task 2 定案：强制路径只添噪声）
            ["NeedConditionSetId"] = 0,
            ["TaskConditionSetId"] = 0,
            ["StartRewardSetId"] = 0,               // Task 8 Controller 裁定（防空双发）：原生 1760 行同为 0，
                                                    // 弹窗不受影响；奖励统一走 RewardExecutor 代码路径
            ["RewardSetId"] = 0,                    // 刻意置 0：与 StartRewardSet 区分归因（Task 2 定案）
            ["ExpiredRewardSetId"] = 0,
            ["SMSContactId"] = 0,
            ["SMSContent"] = "", ["SMSContent_Local"] = "",
            ["TasteRequire"] = -1,                  // 两模板均 -1（无口味门槛）
            ["GroupKey"] = "", ["GroupDayCD"] = 0,
            ["EndingLine"] = 0, ["MaxTimes"] = 0,
        };
    }

    // ———————————————— RewardSet 行 ————————————————

    /// <summary>单事件全部 RewardSet 行：基础行（1 行）+ 每选项 1 行（L1×3、H3×2）。
    /// 选项行 Id 自 99295 按目录序顺排（与 <see cref="AllRewardSets"/> 同一编号走查，结果一致）。</summary>
    public static IReadOnlyList<(int Id, Dictionary<string, object> Row)> RewardSetsOf(EventDef e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        var rows = new List<(int, Dictionary<string, object>)>();
        var next = OptionRewardSetFirstId;
        for (var i = 0; i < EventCatalog.Events.Count; i++)
        {
            var ev = EventCatalog.Events[i];
            if (ev.Id != e.Id)
            {
                if (ev.Reward is { Choices.Count: > 0 }) next += ev.Reward.Choices.Count;
                continue;
            }
            var baseId = FirstRewardSetId + i;
            rows.Add((baseId, RewardSetRow(ev.Reward, baseId)));
            if (ev.Reward is { Choices.Count: > 0 })
                foreach (var c in ev.Reward.Choices)
                    rows.Add((next, RewardSetRow(c.Reward, next++)));
            return rows;
        }
        throw new ArgumentOutOfRangeException(nameof(e), e.Id, "非 EventCatalog 目录事件");
    }

    /// <summary>全目录 RewardSet 行（45 基础 + 5 选项 = 50 行，Id=99250..99299 连续不重）。</summary>
    public static IReadOnlyList<(int Id, Dictionary<string, object> Row)> AllRewardSets()
    {
        var rows = new List<(int, Dictionary<string, object>)>();
        var next = OptionRewardSetFirstId;
        for (var i = 0; i < EventCatalog.Events.Count; i++)
        {
            var ev = EventCatalog.Events[i];
            var baseId = FirstRewardSetId + i;
            rows.Add((baseId, RewardSetRow(ev.Reward, baseId)));
            if (ev.Reward is { Choices.Count: > 0 })
                foreach (var c in ev.Reward.Choices)
                    rows.Add((next, RewardSetRow(c.Reward, next++)));
        }
        return rows;
    }

    /// <summary>RewardSpec → Config_RewardSet 全字段字典（44 键）。内容口径见类注；
    /// 未承载字段=显式安全缺省（空列表/0，官方消费方不读出未定义态——Task 2 定案）。</summary>
    public static Dictionary<string, object> RewardSetRow(RewardSpec reward, int id)
    {
        if (reward == null) throw new ArgumentNullException(nameof(reward));
        var items = reward.RandomPickOne
            ? new List<int>()
            : RewardEncoding.ItemChangeOf(reward);   // itemId 重复=件数（行 6420 语义）
        var keys = new List<int>();
        var values = new List<int>();
        foreach (var a in reward.Attrs)
        {
            if (a == null) continue;
            if (a.Delta != Math.Floor(a.Delta))
                throw new InvalidOperationException(
                    "AttriValueChange=List<int> 只承载整数增量（原生行 6011/6013 编码）；小数/永久属性增量走 Task 8/9 代码路径。attr=" + a.AttrId);
            keys.Add(a.AttrId);
            values.Add((int)a.Delta);
        }
        return new Dictionary<string, object>
        {
            ["ID"] = id,
            ["Source"] = reward.RandomPickOne ? RandomByCodeTag : SourceTag,
            ["AttriKeyChange"] = keys,
            ["AttriValueChange"] = values,
            ["SurvivalPoint"] = 0, ["BeliefPoint"] = 0,
            ["ItemChange"] = items,
            ["ItemPackage"] = new List<int>(), ["ItemCost"] = new List<int>(),
            ["ClearFurnitureBag"] = new List<int>(),
            ["FurniturelD"] = new List<int>(), ["FurnitureHP"] = new List<int>(),
            ["BuffTargetType"] = new List<int>(), ["BuffTargetID"] = new List<int>(),
            ["AddBuff"] = new List<int>(), ["DelBuff"] = new List<int>(),   // Buffs 走 Task 8 代码路径（口径）
            ["ChangeStatus"] = new List<int>(),
            ["AddTag"] = new List<string>(), ["DelTag"] = new List<string>(),
            ["ForbidTag"] = new List<string>(), ["ForbidTime"] = new List<int>(),
            ["AddSymbol"] = new List<string>(), ["DelSymbol"] = new List<string>(),
            ["UnlockSystem"] = new List<int>(), ["ShowProductionList"] = new List<int>(),
            ["ShowRecipeList"] = new List<int>(),
            ["UnlockProductionCraft"] = new List<int>(), ["UnlockCookingCraft"] = new List<int>(),
            ["GetAchievemenet"] = 0,
            ["ActiveEvent"] = new List<int>(), ["ActiveStory"] = new List<int>(),
            ["MoneyAdd"] = 0,
            ["ShowGuidanceFurnitureID"] = new List<int>(), ["HideGuidanceFurnitureID"] = new List<int>(),
            ["RandomGroupId"] = 0, ["RandomGroupCount"] = 0,
            ["ExposureValue"] = 0, ["EventExp"] = 0, ["ExpType"] = "",
            ["ClearFurnitureBagSatiety"] = 0,
            ["MetNpcIds"] = new List<int>(), ["RemoveEvent"] = new List<int>(),
            ["CounterKey"] = "", ["CounterAdd"] = 0,
        };
    }

    // ———————————————— Buff/Effect 蓝图拆表 ————————————————

    /// <summary>Config_Buff 可写属性全集（dump.cs:28302-28328 逐字）：FieldOverrides 键命中此处→写 buff 行。</summary>
    private static readonly HashSet<string> BuffFieldNames = new()
    {
        "ID", "Name", "Name_Local", "IsGood", "BuffDisplayType", "BuffType", "IsHide", "Mask",
        "Des", "Des_Local", "TagList", "EventTag", "LogPriority", "BuffText", "BuffText_Local",
        "BuffRandomText", "BuffDuring", "Overlay", "IsAutonomy", "InitLayerCount", "AddLayerCount",
        "MaxLayerCount", "ShowConfigId", "EffectConfigId",
    };

    /// <summary>Config_Effect 属性名（dump.cs:33687 起属性集，覆盖蓝图 FieldOverrides 用到的字段族）：
    /// 命中此处→写 effect 行。</summary>
    private static readonly HashSet<string> EffectFieldNames = new()
    {
        "ID", "ConfigName", "ExposureValue", "PonitEnd",
        "ItemSatietyStart", "ItemMoraleStart", "ItemStaminaStart", "ItemHealthStart", "ItemVitalityStart",
        "ItemAttrInterval", "ItemSatietyInterval", "ItemMoraleInterval", "ItemStaminaInterval",
        "ItemHealthInterval", "ItemVitalityInterval", "ItemEndBuff01", "ItemEndBuff02",
        "ItemSatietyEnd", "ItemMoraleEnd", "ItemStaminaEnd", "ItemHealthEnd", "ItemVitalityEnd",
        "EBuffTargetType", "EBuffTargetID", "EAddBuff", "EDelBuff", "EDisableFurniture",
        "BuffInterval", "BuffSatietyInterval", "BuffMoraleInterval", "BuffStaminaInterval",
        "BuffHealthInterval", "BuffVitalityInterval", "BuffAction", "BuffActionProbability",
        "BuffEndBuff01", "BuffEndBuff02",
        "BuffSatietyMax", "BuffMoraleMax", "BuffStaminaMax", "BuffHealthMax", "BuffVitalityMax",
        "BuffSatietyAdd", "BuffMoraleAdd", "BuffStaminaAdd", "BuffHealthAdd", "BuffVitalityAdd",
        "BuffSatietyDel", "BuffMoraleDel", "BuffStaminaDel", "BuffHealthDel", "BuffVitalityDel",
        "BuffSpeed", "BuffSatietyStart", "BuffMoraleStart", "BuffStaminaStart", "BuffHealthStart",
        "BuffVitalityStart", "BuffSatietyEnd", "BuffMoraleEnd", "BuffStaminaEnd", "BuffHealthEnd",
        "BuffVitalityEnd",
        "IntValue0", "IntValue1", "IntValue2", "IntValue3", "IntValue4", "IntValue5",
        "FloatValue0", "FloatValue1", "FloatValue2", "FloatValue3", "FloatValue4", "FloatValue5",
        "StrValue0", "StrValue1", "StrValue2", "StrValue3", "StrValue4", "StrValue5",
    };

    /// <summary>BuffBlueprint →（buff 行覆盖字典, effect 行覆盖字典）：FieldOverrides 按字段名归属拆表
    /// （键在 <see cref="BuffFieldNames"/>→buff；在 <see cref="EffectFieldNames"/>→effect；都不在→抛错
    /// fail fast 防拼写静默串表）。另补三枚映射钉：buff.ID=NewId、buff.EffectConfigId=新 effect Id
    /// （不沿用源行 200000000+id 段，指向 99290 段本 mod 行）、effect.ID=同值；
    /// 以及 Name/Des 中文直写（键与 _Local 同值——Task 2 实锤 GetLocalText 键 miss 原样透传，
    /// 防克隆残留 "Buff_Name_11002" 官方键命中官方文案穿帮）。
    /// 覆盖字典之外的字段由 IL2CPP 层从 CloneSourceId 源行逐属性拷贝（见 Game/TableInjector）。</summary>
    public static (Dictionary<string, object> Buff, Dictionary<string, object> Effect) BuffRows(BuffBlueprint b)
    {
        if (b == null) throw new ArgumentNullException(nameof(b));
        var buff = new Dictionary<string, object>();
        var effect = new Dictionary<string, object>();
        foreach (var kv in b.FieldOverrides)
        {
            if (BuffFieldNames.Contains(kv.Key)) buff[kv.Key] = kv.Value;
            else if (EffectFieldNames.Contains(kv.Key)) effect[kv.Key] = kv.Value;
            else throw new InvalidOperationException(
                "BuffBlueprint(" + b.NewId + ") FieldOverrides 字段 \"" + kv.Key
                + "\" 既不属于 Config_Buff 也不属于 Config_Effect 属性集（dump.cs 28302/33687 起）");
        }
        var effectId = EffectIdOf(b);
        buff["ID"] = b.NewId;
        buff["Name"] = b.Name;
        buff["Des"] = b.Des;
        buff["EffectConfigId"] = effectId;
        effect["ID"] = effectId;
        return (buff, effect);
    }
}
