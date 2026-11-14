using System;
using System.Collections.Generic;
using System.Linq;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>InjectorMapping 纯函数契约测试（Task 6 Step 1，先写后实现；T18 奖励原生上卡改写）。
/// 映射层=EventDef/RewardSpec/BuffBlueprint → 表行字段字典（键=Config_* 属性名，值=int/double/
/// string/List&lt;int&gt;/List&lt;string&gt;）；IL2CPP 写入层（Game/TableInjector，测试上下文不可见）
/// 只做"字典→对象属性"搬运。字段名与形态权威=gamekb 2026-11-05_26454866 dump.cs：
/// Config_Event 67 属性（35790 起）、Config_RewardSet 44 属性（46003 起，Attri 编码=平行
/// List&lt;int&gt;，原生行 6011/6013 实证）、Config_Buff 24 属性（28302 起）、Config_Effect
/// （33687 起）、Config_RandomGroup 7 属性（45795 起，2026-11-12_26615062 取证）。
/// ID 段：RewardSet 基础行=99250+目录序（99250..99294）、选项行 99295..99299
/// （L1×3+H3×2，Task 10 起声明式发奖）、子事件行 99245..99249（事件段 99200..99244 顺延）、
/// Effect 行=99290+蓝图序（99290..99303，Effect 独立表与 buff id 不冲突）、
/// RandomGroup 行=99250+池序（99250..99254，独立表段，T18）。</summary>
public sealed class InjectorMappingTests
{
    private static EventDef ById(int id) => Assert.Single(EventCatalog.Events, e => e.Id == id);

    private static IReadOnlyList<(int Id, Dictionary<string, object> Row)> RowsOf(int eventId) =>
        InjectorMapping.RewardSetsOf(ById(eventId));

    // ———————————————— EventRow：行形态与数据 ————————————————

    [Fact]
    public void EventRow_G1_DirectStyleWithRewardSetIdDialogueAndIcon()
    {
        var row = InjectorMapping.EventRow(ById(EventCatalog.Ids.G1));

        // 直发样式（模板=原生行 10011）：Type=2 Result / PopUpType=1 / DemoTwoMode=2 / DayCD=1
        Assert.Equal(99205, row["ID"]);
        Assert.Equal(2, row["Type"]);
        Assert.Equal(1, row["PopUpType"]);
        Assert.Equal(2, row["DemoTwoMode"]);
        Assert.Equal(1, row["DayCD"]);

        // 台词原文（spec §8.2 逐字）双写 Name/Name_Local、EventDes/EventDes_Local
        Assert.Equal("救灾空投", row["Name"]);
        Assert.Equal("救灾空投", row["Name_Local"]);
        Assert.Equal("天上掉下来一个箱子，降落伞没打开，但箱子的质量出乎意料。", row["EventDes"]);
        Assert.Equal("天上掉下来一个箱子，降落伞没打开，但箱子的质量出乎意料。", row["EventDes_Local"]);

        // T19 结果阶段挂法：直发父行 RewardSetId=基础 RewardSet 行 Id（原生 Result 型（Type=2）1260 行
        // 零 StartRewardSetId、831 行挂 RewardSetId——直发型（Type=2/PopUpType=1/非子）327 行同构
        // （10200=6049 等）；Start 奖励机制原生全套 Type==3 门=Task 型专属）；StartRewardSetId=0
        // （T18 的 Start 挂法是原生零先例形态且实机强制触发链 NRE，废弃）
        Assert.Equal(0, row["StartRewardSetId"]);
        Assert.Equal(99255, row["RewardSetId"]);

        // Icon=原生约定格式（T18）：ResolveIconDir 剥 Assets/RuntimeAssets/Texture/ 前缀转
        // ../../Res/Icon/ds_g1，webui-core RES_IMG_EXTS 自动重试 .webp 命中落盘文件；无扩展名
        Assert.Equal("Assets/RuntimeAssets/Texture/Icon/ds_g1", row["Icon"]);
        Assert.Equal("Assets/RuntimeAssets/Texture/Icon/ds_g1", row["IconEnd"]);

        Assert.Equal("我知道了", row["OKDes_Local"]);
    }

    [Fact]
    public void EventRow_L1_ChoiceStyleWiresThreeSonEvents()
    {
        var row = InjectorMapping.EventRow(ById(EventCatalog.Ids.L1));

        // 选项样式（模板=原生行 103）：Type=1 Choice / PopUpType=3 / DemoTwoMode=3 / DayCD=3 / Countdown=60
        Assert.Equal(1, row["Type"]);
        Assert.Equal(3, row["PopUpType"]);
        Assert.Equal(3, row["DemoTwoMode"]);
        Assert.Equal(3, row["DayCD"]);
        Assert.Equal(60, row["Countdown"]);

        var labels = Assert.IsType<List<string>>(row["ChoseName_Local"]);
        Assert.Equal(new List<string> { "食物层", "药品层", "材料层" }, labels);
        Assert.Equal(labels, Assert.IsType<List<string>>(row["ChoseName"]));

        // Task 10 接线（原生 103/10010 形态）：ChoseID=子事件 Id（与 Choices 同序）、Default=末选项子事件
        Assert.Equal(new List<int> { 99245, 99246, 99247 }, Assert.IsType<List<int>>(row["ChoseID"]));
        Assert.Equal(99247, row["Default"]);
        // 顶层不发奖励（各选项子事件 RewardSetId 声明式发奖）；选项父行 StartRewardSetId=0（T18 口径）
        Assert.Equal(0, row["StartRewardSetId"]);
        Assert.Equal(0, row["RewardSetId"]);
    }

    [Fact]
    public void EventRow_H3_ChoiceStyleWiresTwoSonEvents()
    {
        var row = InjectorMapping.EventRow(ById(EventCatalog.Ids.H3));
        Assert.Equal(1, row["Type"]);
        Assert.Equal(new List<string> { "追上去", "算了" },
            Assert.IsType<List<string>>(row["ChoseName_Local"]));
        Assert.Equal(new List<int> { 99248, 99249 }, Assert.IsType<List<int>>(row["ChoseID"]));
        Assert.Equal(99249, row["Default"]);   // Default=末选项「算了」（原生 103/10010 同构）
    }

    /// <summary>Config_Event 全字段拍平契约：字典键集=dump.cs 67 个可写属性（35663 起，多一字段即漏拍）。</summary>
    [Fact]
    public void EventRow_CoversAllConfigEventProperties()
    {
        var expected = new HashSet<string>
        {
            "ID", "Name", "Name_Local", "PlayerSelectId", "DemoTwoMode", "EndlessMode", "Type",
            "EventLogID", "LogType", "ShowType", "PopUpType", "FurnitureID", "KeepTime", "IsSon",
            "EventDes", "EventDes_Local", "Info", "Info_Local", "DoneInfo", "DoneInfo_Local",
            "EventDesTaskEnd", "EventDesTaskEnd_Local", "Icon", "IconEnd", "IconRole",
            "DataMin", "DataMax", "DayRate", "TimeMin", "TimeMax", "CanRepeat", "DayCD", "HourCD",
            "StartThink", "StartThink_Local", "ChoseName", "ChoseName_Local", "ChoseID", "Default",
            "Countdown", "ActionRequire", "ActionTime", "ItemRequire", "ItemTime", "TaskTime",
            "OKDes", "OKDes_Local", "OKDoneDes", "OKDoneDes_Local", "DoneThink", "DoneThink_Local",
            "TriggerFmod", "EndFmod", "ConditionSetId", "NeedConditionSetId", "TaskConditionSetId",
            "StartRewardSetId", "RewardSetId", "ExpiredRewardSetId", "SMSContactId", "SMSContent",
            "SMSContent_Local", "TasteRequire", "GroupKey", "GroupDayCD", "EndingLine", "MaxTimes",
        };
        Assert.Equal(67, expected.Count);
        foreach (var e in EventCatalog.Events)
        {
            var keys = InjectorMapping.EventRow(e).Keys.ToHashSet();
            Assert.True(keys.SetEquals(expected), $"EventRow({e.Key}) 键集不一致: 多={string.Join(",", keys.Except(expected))} 少={string.Join(",", expected.Except(keys))}");
        }
        // 子事件行（Task 10）同契约：SonEventRow 也 67 键全拍平
        foreach (var s in EventCatalog.SonEvents)
        {
            var keys = InjectorMapping.SonEventRow(s).Keys.ToHashSet();
            Assert.True(keys.SetEquals(expected), $"SonEventRow({s.Id}) 键集不一致: 多={string.Join(",", keys.Except(expected))} 少={string.Join(",", expected.Except(keys))}");
        }
    }

    [Fact]
    public void EventRow_IconPathFollowsEventDefIconForAllEvents()
    {
        foreach (var e in EventCatalog.Events)
        {
            var row = InjectorMapping.EventRow(e);
            // T18 原生约定：IconPrefix+裸名去扩展名（IconFieldOf 单一合成点）
            Assert.Equal(InjectorMapping.IconFieldOf(e.Icon), (string)row["Icon"]);
            Assert.Equal("Assets/RuntimeAssets/Texture/Icon/ds_" + e.Key, (string)row["Icon"]);   // 无 .webp 后缀
        }
    }

    [Fact]
    public void EventRow_BaseRewardSetIdWalksCatalogOrder()
    {
        // 目录序（0 起）：N1=99250 … D3=99294（编号保留供 RewardSet 行 Id 与备查；事件行不再引用）
        Assert.Equal(99250, InjectorMapping.BaseRewardSetIdOf(ById(EventCatalog.Ids.N1)));
        Assert.Equal(99274, InjectorMapping.BaseRewardSetIdOf(ById(EventCatalog.Ids.L1)));
        Assert.Equal(99290, InjectorMapping.BaseRewardSetIdOf(ById(EventCatalog.Ids.H3)));
        Assert.Equal(99294, InjectorMapping.BaseRewardSetIdOf(ById(EventCatalog.Ids.D3)));
    }

    /// <summary>T19 奖励结果阶段挂载钉（全 45 行派生）：直发父行 RewardSetId=基础 RewardSet 行 Id
    /// （原生 Result 型（Type=2）831 行挂 RewardSetId 同构——直发型 327 行（10200=6049 等）；
    /// FSM DoResult 结算+SetResultUI 清单+确认发放）；StartRewardSetId 恒 0（原生 Result 型 1260 行
    /// 零 Start 挂载、Start 机制 Type==3 门=Task 型专属；T18 的 Type=2 挂 Start 零先例且实机
    /// 强制触发链 NRE，废弃）；选项父行 RewardSetId=0（顶层不发，选项效果归子事件）；
    /// ExpiredRewardSetId 恒 0。</summary>
    [Fact]
    public void EventRow_DirectEvents_RewardSetIdWiredToBaseRow()
    {
        foreach (var e in EventCatalog.Events)
        {
            var row = InjectorMapping.EventRow(e);
            var isChoice = e.Reward is { Choices.Count: > 0 };
            Assert.Equal(0, row["StartRewardSetId"]);
            Assert.Equal(isChoice ? 0 : InjectorMapping.BaseRewardSetIdOf(e), row["RewardSetId"]);
            Assert.Equal(0, row["ExpiredRewardSetId"]);
        }
    }

    /// <summary>T19 NRE 防线（表驱动，gamekb 2026-11-12_26615062 dump 属性集核对）：全 50 Event 行
    /// （45 父+5 子）+ 50 RewardSet 行 + 5 RandomGroup 行的所有 List 字段必须非 null——IL2CPP new
    /// 出的表行 List 字段默认 null，原生 CreateDynamicEventArgs/AddResults/SetResultUI/ExecuteRewardSet
    /// 对 null 列表迭代即 NRE（T18 实机 99205 DynamicEventManager NRE 复盘：T17 同坑三列表非空
    /// 初始化后即通，本测试把该防线钉到全部注入表行）。键集=dump 可写属性中的 List 字段全集
    /// （Config_Event 10 / Config_RewardSet 30 / Config_RandomGroup 3），与键集拍平测试互证无漏键。</summary>
    [Fact]
    public void AllRows_EveryListFieldNonNull_FiftyEventFiftyRewardSetFiveGroupRows()
    {
        var eventListKeys = new HashSet<string>   // Config_Event 10 个 List 字段（dump.cs:35730 起）
        {
            "PlayerSelectId", "FurnitureID", "IconRole", "ChoseName", "ChoseName_Local", "ChoseID",
            "ActionRequire", "ActionTime", "ItemRequire", "ItemTime",
        };
        var rewardListKeys = new HashSet<string>  // Config_RewardSet 30 个 List 字段（dump.cs:46081 起）
        {
            "AttriKeyChange", "AttriValueChange", "ItemChange", "ItemPackage", "ItemCost",
            "ClearFurnitureBag", "FurniturelD", "FurnitureHP", "BuffTargetType", "BuffTargetID",
            "AddBuff", "DelBuff", "ChangeStatus", "AddTag", "DelTag", "ForbidTag", "ForbidTime",
            "AddSymbol", "DelSymbol", "UnlockSystem", "ShowProductionList", "ShowRecipeList",
            "UnlockProductionCraft", "UnlockCookingCraft", "ActiveEvent", "ActiveStory",
            "ShowGuidanceFurnitureID", "HideGuidanceFurnitureID", "MetNpcIds", "RemoveEvent",
        };
        var groupListKeys = new HashSet<string>   // Config_RandomGroup 3 个 List 字段（dump.cs:45795 起）
        {
            "IdList", "CountList", "DiscoverLvList",
        };

        foreach (var e in EventCatalog.Events)
            AssertListFields(InjectorMapping.EventRow(e), eventListKeys, "EventRow " + e.Key);
        foreach (var s in EventCatalog.SonEvents)
            AssertListFields(InjectorMapping.SonEventRow(s), eventListKeys, "SonEventRow " + s.Id);
        foreach (var (id, row) in InjectorMapping.AllRewardSets())
            AssertListFields(row, rewardListKeys, "RewardSetRow " + id);
        foreach (var (id, row) in InjectorMapping.RandomGroupRows())
            AssertListFields(row, groupListKeys, "RandomGroupRow " + id);
    }

    /// <summary>逐键断言：行字典必含该 List 字段且值非 null（TableInjector 按字典逐键写全新
    /// Il2Cpp List——映射层漏键/漏值=注入行该字段停留 null=原生迭代 NRE）。</summary>
    private static void AssertListFields(Dictionary<string, object> row, HashSet<string> listKeys, string what)
    {
        foreach (var key in listKeys)
            Assert.True(row.ContainsKey(key) && row[key] is System.Collections.IList,
                what + "." + key + " 缺失或值非列表（null 列表=原生迭代 NRE）");
    }

    // ———————————————— 子事件（Task 10 接线）——————————————

    [Fact]
    public void SonEvents_FiveRowsInOwnSegmentMatchingChoiceOrder()
    {
        // 事件段 99200~99244 已满，子事件顺延 99245~99249；顺序契约=父 Choices 同序
        // （父行 ChoseID 与子事件行 RewardSetId 映射都按此派生）
        Assert.Equal(new[] { 99245, 99246, 99247, 99248, 99249 },
            EventCatalog.SonEvents.Select(s => s.Id).ToArray());
        Assert.Equal(new[] { EventCatalog.Ids.L1, EventCatalog.Ids.L1, EventCatalog.Ids.L1,
            EventCatalog.Ids.H3, EventCatalog.Ids.H3 },
            EventCatalog.SonEvents.Select(s => s.ParentId).ToArray());

        // 与父 Choices 一一同构（数量+顺序）
        foreach (var parent in new[] { ById(EventCatalog.Ids.L1), ById(EventCatalog.Ids.H3) })
        {
            var sons = EventCatalog.SonEvents.Where(s => s.ParentId == parent.Id).ToList();
            Assert.Equal(parent.Reward.Choices.Count, sons.Count);
        }

        // 台词非空（结果卡一句文案）；Id 段与 45 事件段不重叠
        Assert.All(EventCatalog.SonEvents, s => Assert.False(string.IsNullOrWhiteSpace(s.Line)));
        Assert.All(EventCatalog.SonEvents.Select(s => s.Id).ToArray(),
            id => Assert.InRange(id, 99245, 99249));
    }

    /// <summary>子事件行形态（模板=原生 10011/104：Type=2 Result / PopUpType=1 / IsSon=true /
    /// DemoTwoMode 随父(103 样式=3) / RewardSetId=声明式发奖 / DayCD=0 / CanRepeat=false）。</summary>
    [Fact]
    public void SonEventRow_FollowsNativeSonFormAndWiresOptionRewardSet()
    {
        var first = InjectorMapping.SonEventRow(EventCatalog.SonEvents[0]);   // L1 食物层 99245
        Assert.Equal(99245, first["ID"]);
        Assert.Equal(2, first["Type"]);
        Assert.Equal(1, first["PopUpType"]);
        Assert.True((bool)first["IsSon"]);
        Assert.Equal(3, first["DemoTwoMode"]);        // 随父（我们的父行=103 样式；原生 104 同构）
        Assert.Equal(0, first["DayCD"]);
        Assert.False((bool)first["CanRepeat"]);
        Assert.Equal(0, first["Countdown"]);
        Assert.Equal(0, first["Default"]);
        Assert.Empty(Assert.IsType<List<int>>(first["ChoseID"]));

        // 名字=父标题-选项名（原生「暴雨侵袭-用毛巾堵漏」同构）；台词=EventDes/EventDesTaskEnd 同文
        Assert.Equal("神秘补给箱-食物层", first["Name"]);
        Assert.Equal("神秘补给箱-食物层", first["Name_Local"]);
        Assert.Equal(EventCatalog.SonEvents[0].Line, (string)first["EventDes"]);
        Assert.Equal(EventCatalog.SonEvents[0].Line, (string)first["EventDesTaskEnd"]);

        // RewardSetId→选项行 99295（声明式发奖）；条件门清零（Task 2 定案）；图沿用父事件图（T18 新格式）
        Assert.Equal(99295, first["RewardSetId"]);
        Assert.Equal(0, first["StartRewardSetId"]);
        Assert.Equal(0, first["ConditionSetId"]);
        Assert.Equal(InjectorMapping.IconFieldOf(ById(EventCatalog.Ids.L1).Icon), (string)first["Icon"]);
        Assert.Equal("Assets/RuntimeAssets/Texture/Icon/ds_l1", (string)first["Icon"]);

        // H3 两子事件：99248 追→99298、99249 算了→99299
        Assert.Equal(99298, InjectorMapping.SonEventRow(EventCatalog.SonEvents[3])["RewardSetId"]);
        Assert.Equal(99299, InjectorMapping.SonEventRow(EventCatalog.SonEvents[4])["RewardSetId"]);
        Assert.Equal("贼影-追上去", (string)InjectorMapping.SonEventRow(EventCatalog.SonEvents[3])["Name"]);
        Assert.Equal("贼影-算了", (string)InjectorMapping.SonEventRow(EventCatalog.SonEvents[4])["Name"]);
    }

    /// <summary>子事件 RewardSetId 与 AllRewardSets 选项行编号同源（同一走查）：错位会实机点选发错奖。</summary>
    [Fact]
    public void SonEventRow_RewardSetIdsMatchOptionRowNumbering()
    {
        var optionRows = InjectorMapping.AllRewardSets()
            .Where(r => r.Id >= InjectorMapping.OptionRewardSetFirstId).ToDictionary(r => r.Id);
        Assert.Equal(5, optionRows.Count);
        foreach (var s in EventCatalog.SonEvents)
        {
            var rsId = (int)InjectorMapping.SonEventRow(s)["RewardSetId"];
            Assert.Contains(rsId, optionRows.Keys);
            // 子事件台词与选项行内容同源于同一 Choice（按父 Choices 顺序对齐）
            Assert.True(rsId >= InjectorMapping.OptionRewardSetFirstId && rsId <= 99299,
                $"子事件 {s.Id} 的 RewardSetId {rsId} 不在选项行段");
        }
        // 全部 5 个指向互不重复
        Assert.Equal(5, EventCatalog.SonEvents
            .Select(s => (int)InjectorMapping.SonEventRow(s)["RewardSetId"]).Distinct().Count());
    }

    /// <summary>H3 双发防线（Task 10）：选项事件顶层 Op 不再代码执行——Grant 消费
    /// RewardSpec.RunsTopLevelOp 短路（零件损失改由"算了"子事件 ItemCost 声明式）。</summary>
    [Fact]
    public void RewardSpec_RunsTopLevelOp_ShortCircuitedByChoices()
    {
        Assert.False(ById(EventCatalog.Ids.H3).Reward.RunsTopLevelOp);   // StealParts ×Choices=短路
        Assert.False(ById(EventCatalog.Ids.L1).Reward.RunsTopLevelOp);   // 选项事件（Op=None）
        Assert.False(ById(EventCatalog.Ids.N1).Reward.RunsTopLevelOp);   // 无 Op
        Assert.True(ById(EventCatalog.Ids.H2).Reward.RunsTopLevelOp);    // 非选项+Op=执行
        Assert.True(ById(EventCatalog.Ids.M1).Reward.RunsTopLevelOp);
        Assert.True(ById(EventCatalog.Ids.D1).Reward.RunsTopLevelOp);
        // 裸 spec（无 Choices 字段）防御：Op 非空即执行
        Assert.True(new RewardSpec { Op = SpecialOp.StealParts }.RunsTopLevelOp);
    }

    // ———————————————— RewardSetRow：编码口径 ————————————————

    [Fact]
    public void RewardSetsOf_G1_EncodesFixedItems()
    {
        var rows = RowsOf(EventCatalog.Ids.G1);
        var row = Assert.Single(rows);
        Assert.Equal(99255, row.Id);
        Assert.Equal(InjectorMapping.SourceTag, row.Row["Source"]);
        Assert.Equal(new List<int> { 2101, 2101, 2101, 2101, 1, 1, 1, 1, 1, 1 },
            Assert.IsType<List<int>>(row.Row["ItemChange"]));
        // G1 无 buff 声明：AddBuff 空（有 Buffs 的事件走 AddBuff 声明式，见 G4 用例）
        Assert.Empty(Assert.IsType<List<int>>(row.Row["AddBuff"]));
    }

    /// <summary>T18 数量烘焙：rewardMult 在注入时定格进 ItemChange（round(base×mult) 下限 1，
    /// RewardMath.ScaleCount 同语义）——行内容即最终发放量，原生结算不再二次缩放。</summary>
    [Theory]
    [InlineData(2.0, new[] { 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 })]   // 档1 ×2：饼干×8、糖×12
    [InlineData(0.75, new[] { 2101, 2101, 2101, 1, 1, 1, 1, 1 })]                                                     // 档4 ×0.75：×3、×4.5→5（AwayFromZero）
    public void RewardSetsOf_G1_BakesRewardMultiplierIntoItemChange(double mult, int[] want)
    {
        var row = Assert.Single(InjectorMapping.RewardSetsOf(ById(EventCatalog.Ids.G1), rewardMult: mult));
        Assert.Equal(want, Assert.IsType<List<int>>(row.Row["ItemChange"]));
    }

    /// <summary>L1 选项行同样烘焙（倍率缩放不因选项卡路径缺席）。</summary>
    [Fact]
    public void RewardSetsOf_L1OptionRows_BakeRewardMultiplier()
    {
        var rows = InjectorMapping.RewardSetsOf(ById(EventCatalog.Ids.L1), rewardMult: 2.0).ToList();
        // 食物层 99295：饼干×8→16、食品大包裹×2→4
        Assert.Equal(new List<int> { 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 11003, 11003, 11003, 11003 },
            Assert.IsType<List<int>>(rows[1].Row["ItemChange"]));
    }

    /// <summary>哨兵 RewardSet 探针数据源回归钉（fix#1）：TableInjector 的探针期望全部派生自本映射，
    /// 此处钉死「首 Id 99250=N1 空行、G1 行=99255 十件」——探针再错位（如拿 99250 当 G1 行断言
    /// ItemChange.Count=10）会先在本测试暴露，而非实机哨兵恒红。</summary>
    [Fact]
    public void RewardSetsOf_FirstRewardSetIdIsN1EmptyRow_G1IsAtItsOwnBaseId()
    {
        var n1 = Assert.Single(RowsOf(EventCatalog.Ids.N1));
        Assert.Equal(InjectorMapping.FirstRewardSetId, n1.Id);          // 99250 = N1「什么都没发生」
        Assert.Empty(Assert.IsType<List<int>>(n1.Row["ItemChange"]));   // 空行——不是任何有载荷事件的行
        Assert.Equal(InjectorMapping.SourceTag, n1.Row["Source"]);

        var g1 = Assert.Single(RowsOf(EventCatalog.Ids.G1));
        Assert.NotEqual(InjectorMapping.FirstRewardSetId, g1.Id);       // G1 ≠ 99250
        Assert.Equal(InjectorMapping.BaseRewardSetIdOf(ById(EventCatalog.Ids.G1)), g1.Id);  // =99255
        Assert.Equal(10, Assert.IsType<List<int>>(g1.Row["ItemChange"]).Count);

        // 探针入口一致性：BaseRewardSetIdOf 与 RewardSetsOf 首行同源（TableInjector 哨兵只消费这两个）
        Assert.Equal(n1.Id, InjectorMapping.BaseRewardSetIdOf(ById(EventCatalog.Ids.N1)));
    }

    /// <summary>T18 buff 声明式：G4=咖啡×4+精神振奋(11004)——ItemChange 承载物品（烘焙件数）、
    /// AddBuff+BuffTargetType/BuffTargetID=1/0 配对承载 buff（原生行 6014/6106 同构）。</summary>
    [Fact]
    public void RewardSetsOf_G4_ItemsAndBuffsBothDeclarative()
    {
        var g4 = Assert.Single(RowsOf(EventCatalog.Ids.G4));
        Assert.Equal(new List<int> { 7, 7, 7, 7 }, Assert.IsType<List<int>>(g4.Row["ItemChange"]));
        Assert.Equal(new List<int> { 11004 }, Assert.IsType<List<int>>(g4.Row["AddBuff"]));
        Assert.Equal(new List<int> { 1 }, Assert.IsType<List<int>>(g4.Row["BuffTargetType"]));
        Assert.Equal(new List<int> { 0 }, Assert.IsType<List<int>>(g4.Row["BuffTargetID"]));
    }

    /// <summary>T18 RandomPickOne 声明式：R2/E1/E3/E4/L2 的随机×1 由 Config_RandomGroup 承载——
    /// RewardSet 行 ItemChange 留空 + RandomGroupId=组行 Id + RandomGroupCount=1（抽 1 条目）；
    /// Source=正式标记（DS_RandomByCode 旧代码路径标记作废）。</summary>
    [Fact]
    public void RewardSetsOf_RandomPickOneEvents_DeclareRandomGroupInstead()
    {
        var wantGroups = new Dictionary<int, int>   // 事件 → 组行 Id（R2/E1/E3/E4/L2 目录序 99250..99254）
        {
            [EventCatalog.Ids.R2] = 99250,
            [EventCatalog.Ids.E1] = 99251,
            [EventCatalog.Ids.E3] = 99252,
            [EventCatalog.Ids.E4] = 99253,
            [EventCatalog.Ids.L2] = 99254,
        };
        foreach (var (id, groupId) in wantGroups)
        {
            var row = Assert.Single(RowsOf(id));
            Assert.Empty(Assert.IsType<List<int>>(row.Row["ItemChange"]));
            Assert.Equal(groupId, row.Row["RandomGroupId"]);
            Assert.Equal(1, row.Row["RandomGroupCount"]);
            Assert.Equal(InjectorMapping.SourceTag, row.Row["Source"]);
            Assert.Equal(groupId, InjectorMapping.RandomGroupIdOf(ById(id)));   // 池序走查同源
        }
        // 非池事件=0（无组引用）
        Assert.Equal(0, InjectorMapping.RandomGroupIdOf(ById(EventCatalog.Ids.G1)));
    }

    /// <summary>RandomGroup 组行编码（T18，schema/原生行取证 dump.cs:45795、tables 行 106/107/220/7001）：
    /// 7 可写属性全拍平；GroupType=1 物品池；IdList=候选 itemId（Count=1 候选各一条，无权重重复）；
    /// CountList=平行每 id 烘焙件数（rewardMult 定格，候选基准 1 件——与旧代码路径
    /// "PickRandomOne 选中一条再 ScaleCount" 等价）；DiscoverLvList=空（无探索门槛）。</summary>
    [Fact]
    public void RandomGroupRows_FivePools_NativeSchemaAndBakedCounts()
    {
        var rows = InjectorMapping.RandomGroupRows().ToList();
        Assert.Equal(5, rows.Count);
        Assert.Equal(Enumerable.Range(99250, 5), rows.Select(r => r.Id));   // R2/E1/E3/E4/L2 目录序

        // R2 家电下乡池 99250：洗衣机/微波炉/电视机包裹
        var r2 = rows[0].Row;
        Assert.Equal(new List<int> { 14057, 14059, 14062 }, Assert.IsType<List<int>>(r2["IdList"]));
        Assert.Equal(new List<int> { 1, 1, 1 }, Assert.IsType<List<int>>(r2["CountList"]));
        Assert.Equal(1, r2["GroupType"]);
        Assert.Empty(Assert.IsType<List<int>>(r2["DiscoverLvList"]));
        Assert.Equal("喜从天降·家电下乡随机池", r2["RandomGroupName"]);
        Assert.Equal(r2["RandomGroupName"], r2["RandomGroupName_Local"]);   // 中文直写键（GetLocalText miss 透传惯例）

        // 7 键全拍平（dump.cs Config_RandomGroup 可写属性集）
        Assert.Equal(new HashSet<string> { "ID", "RandomGroupName", "RandomGroupName_Local",
            "GroupType", "IdList", "CountList", "DiscoverLvList" }, rows[0].Row.Keys.ToHashSet());

        // E1 手稿池 99251 四候选；倍率烘焙：×2 → CountList 全 2（抽 1 条目得 2 件）
        var e1 = Assert.Single(InjectorMapping.RandomGroupRows(rewardMult: 2.0), r => r.Id == 99251).Row;
        Assert.Equal(new List<int> { 3034, 3035, 3036, 3037 }, Assert.IsType<List<int>>(e1["IdList"]));
        Assert.Equal(new List<int> { 2, 2, 2, 2 }, Assert.IsType<List<int>>(e1["CountList"]));
    }

    /// <summary>PermBuffByTier 注入时烘焙（T18）：E2/L3 的命运眷顾变体按 tier/mult 定格进 AddBuff
    /// （RewardMath.PickPermBuff 同源）——热更换档重注入即换变体；预设档 1~5=同号 FortuneIds。</summary>
    [Theory]
    [InlineData(EventCatalog.Ids.E2, 1, 99285)]
    [InlineData(EventCatalog.Ids.E2, 3, 99287)]
    [InlineData(EventCatalog.Ids.L3, 5, 99289)]
    public void RewardSetsOf_PermBuffByTier_BakesVariantByTier(int eventId, int tier, int wantBuffId)
    {
        var row = Assert.Single(InjectorMapping.RewardSetsOf(ById(eventId), tier: tier));
        var addBuff = Assert.IsType<List<int>>(row.Row["AddBuff"]);
        Assert.Equal(new List<int> { wantBuffId }, addBuff);   // E2/L3 Buffs 本体为空，仅烘焙变体
        // buff 配对目标数组同长（6014/6106 配对口径）
        Assert.Equal(addBuff.Count, Assert.IsType<List<int>>(row.Row["BuffTargetType"]).Count);
    }

    /// <summary>声明式覆盖（T18 防双发判定的数据面）：45 事件里每个非 Op 载荷（Items/Buffs/Attrs/
    /// PermBuffByTier）必须由 RewardSet/RandomGroup 行承载——行承载=原生结算=代码路径停发；
    /// Op 系（耐久/腐坏/偷窃/永久属性）不承载（RunsTopLevelOp 代码路径保留，见下一用例）。</summary>
    [Fact]
    public void DeclarativeCoverage_AllNonOpPayloadsEncodedInRows()
    {
        foreach (var e in EventCatalog.Events)
        {
            if (e.Reward is { Choices.Count: > 0 }) continue;   // 选项事件：载荷在选项行（L1/H3 用例已钉）
            var row = Assert.Single(InjectorMapping.RewardSetsOf(e)).Row;
            var addBuff = Assert.IsType<List<int>>(row["AddBuff"]);
            if (e.Reward.Items.Count > 0 && !e.Reward.RandomPickOne)
                Assert.NotEmpty(Assert.IsType<List<int>>(row["ItemChange"]));
            if (e.Reward.RandomPickOne)
                Assert.NotEqual(0, row["RandomGroupId"]);       // 池事件必挂组行
            if (e.Reward.Buffs.Count > 0 || e.Reward.PermBuffByTier)
                Assert.NotEmpty(addBuff);
            if (e.Reward.Attrs.Count > 0)
            {
                Assert.NotEmpty(Assert.IsType<List<int>>(row["AttriKeyChange"]));
                Assert.NotEmpty(Assert.IsType<List<int>>(row["AttriValueChange"]));
            }
        }
    }

    [Fact]
    public void RewardSetsOf_AttrEvents_EncodeNativeParallelIntLists()
    {
        // 原生行 6011/6013 实证：AttriKeyChange=[AttrName 枚举]，AttriValueChange=[整数增量，可负]
        var n2 = Assert.Single(RowsOf(EventCatalog.Ids.N2));
        Assert.Equal(new List<int> { 2 }, Assert.IsType<List<int>>(n2.Row["AttriKeyChange"]));
        Assert.Equal(new List<int> { 2 }, Assert.IsType<List<int>>(n2.Row["AttriValueChange"]));

        var n4 = Assert.Single(RowsOf(EventCatalog.Ids.N4));
        Assert.Equal(new List<int> { 3 }, Assert.IsType<List<int>>(n4.Row["AttriKeyChange"]));
        Assert.Equal(new List<int> { -2 }, Assert.IsType<List<int>>(n4.Row["AttriValueChange"]));
    }

    [Fact]
    public void RewardSetsOf_L1_GeneratesBasePlusThreeOptionRows()
    {
        var rows = RowsOf(EventCatalog.Ids.L1).ToList();
        Assert.Equal(4, rows.Count);

        // 基础行 + 三选项行（目录序分配选项 id：99295/99296/99297）
        Assert.Equal(99274, rows[0].Id);
        Assert.Equal(99295, rows[1].Id);
        Assert.Equal(99296, rows[2].Id);
        Assert.Equal(99297, rows[3].Id);

        // 食物层=90压缩饼干×8+食品大包裹×2；药品层=医疗包×3+抗生素×3+吗啡×2；材料层=铁制零件×30+不锈钢板×15
        Assert.Equal(new List<int> { 2101, 2101, 2101, 2101, 2101, 2101, 2101, 2101, 11003, 11003 },
            Assert.IsType<List<int>>(rows[1].Row["ItemChange"]));
        Assert.Equal(new List<int>
        {
            2401, 2401, 2401, 2405, 2405, 2405, 2916, 2916,
        }, Assert.IsType<List<int>>(rows[2].Row["ItemChange"]));
        var steel = Assert.IsType<List<int>>(rows[3].Row["ItemChange"]);
        Assert.Equal(45, steel.Count);
        Assert.Equal(20362, steel[0]);
        Assert.Equal(20373, steel[30]);
    }

    [Fact]
    public void RewardSetsOf_H3_OptionRowsAreDeclarative()
    {
        var rows = RowsOf(EventCatalog.Ids.H3).ToList();
        Assert.Equal(3, rows.Count);
        // 选项行 99298/99299（L1×3 之后顺排）——Task 10 起选项行声明式发奖（子事件 RewardSetId 指向）
        Assert.Equal(99298, rows[1].Id);
        Assert.Equal(99299, rows[2].Id);

        // 追=AddBuff 1111（受伤流血）：原生行 6014/6106 实证配对 BuffTargetType=[1]/BuffTargetID=[0]；
        // 不含 ItemChange（Controller 裁定 2026-11-12：零件只在"算了"分支扣，追=流血但保零件，防 +10 刷件）
        var chase = rows[1].Row;
        Assert.Equal(new List<int> { 1111 }, Assert.IsType<List<int>>(chase["AddBuff"]));
        Assert.Equal(new List<int> { 1 }, Assert.IsType<List<int>>(chase["BuffTargetType"]));
        Assert.Equal(new List<int> { 0 }, Assert.IsType<List<int>>(chase["BuffTargetID"]));
        Assert.Empty(Assert.IsType<List<int>>(chase["ItemChange"]));
        Assert.Empty(Assert.IsType<List<int>>(chase["ItemCost"]));

        // 算了=ItemCost 20362×10（itemId 重复=件数，原生行 68252 同编码）：零件在此声明式离开背包
        var giveUp = rows[2].Row;
        Assert.Equal(new List<int> { 20362, 20362, 20362, 20362, 20362, 20362, 20362, 20362, 20362, 20362 },
            Assert.IsType<List<int>>(giveUp["ItemCost"]));
        Assert.Empty(Assert.IsType<List<int>>(giveUp["AddBuff"]));
        Assert.Empty(Assert.IsType<List<int>>(giveUp["ItemChange"]));

        Assert.Equal(InjectorMapping.SourceTag, chase["Source"]);
        Assert.Equal(InjectorMapping.SourceTag, giveUp["Source"]);
    }

    /// <summary>基础行口径回归钉（T18 语义）：基础行 AddBuff 声明式承载（buff 类全上卡）；
    /// ItemCost 仍仅选项行承载（基础行的扣减归 Op 系代码路径）；Op 系载荷（StealParts）
    /// 从不进 RewardSet（RewardSetManager 无对应声明式执行器）。</summary>
    [Fact]
    public void RewardSetRow_ItemCostOnlyForOptions_OpNeverEncoded()
    {
        var g4 = Assert.Single(RowsOf(EventCatalog.Ids.G4));
        Assert.Equal(new List<int> { 11004 }, Assert.IsType<List<int>>(g4.Row["AddBuff"]));
        Assert.Empty(Assert.IsType<List<int>>(g4.Row["ItemCost"]));

        // H3 基础行：StealParts 顶层 Op 不承载（父级被 Choices 短路、算了线=子事件 ItemCost）
        var h3Base = RowsOf(EventCatalog.Ids.H3)[0];
        Assert.Empty(Assert.IsType<List<int>>(h3Base.Row["ItemCost"]));
        Assert.Empty(Assert.IsType<List<int>>(h3Base.Row["ItemChange"]));

        // Op 系事件（M1 耐久）基础行全载荷空——惩罚执行归 PunishmentOps 代码路径
        var m1 = Assert.Single(RowsOf(EventCatalog.Ids.M1));
        Assert.Empty(Assert.IsType<List<int>>(m1.Row["ItemChange"]));
        Assert.Empty(Assert.IsType<List<int>>(m1.Row["AddBuff"]));
    }

    [Fact]
    public void AllRewardSets_FiftyUniqueContiguousIds()
    {
        var all = InjectorMapping.AllRewardSets();
        Assert.Equal(50, all.Count);   // 45 基础 + L1×3 + H3×2
        Assert.Equal(Enumerable.Range(99250, 50).ToHashSet(), all.Select(r => r.Id).ToHashSet());
    }

    /// <summary>Config_RewardSet 全字段拍平契约：字典键集=dump.cs 44 个可写属性（46003 起）。</summary>
    [Fact]
    public void RewardSetRow_CoversAllConfigRewardSetProperties()
    {
        var expected = new HashSet<string>
        {
            "ID", "Source", "AttriKeyChange", "AttriValueChange", "SurvivalPoint", "BeliefPoint",
            "ItemChange", "ItemPackage", "ItemCost", "ClearFurnitureBag", "FurniturelD", "FurnitureHP",
            "BuffTargetType", "BuffTargetID", "AddBuff", "DelBuff", "ChangeStatus", "AddTag", "DelTag",
            "ForbidTag", "ForbidTime", "AddSymbol", "DelSymbol", "UnlockSystem", "ShowProductionList",
            "ShowRecipeList", "UnlockProductionCraft", "UnlockCookingCraft", "GetAchievemenet",
            "ActiveEvent", "ActiveStory", "MoneyAdd", "ShowGuidanceFurnitureID",
            "HideGuidanceFurnitureID", "RandomGroupId", "RandomGroupCount", "ExposureValue",
            "EventExp", "ExpType", "ClearFurnitureBagSatiety", "MetNpcIds", "RemoveEvent",
            "CounterKey", "CounterAdd",
        };
        Assert.Equal(44, expected.Count);
        foreach (var (_, row) in InjectorMapping.AllRewardSets())
        {
            var keys = row.Keys.ToHashSet();
            Assert.True(keys.SetEquals(expected), "RewardSetRow 键集不一致: 多=" + string.Join(",", keys.Except(expected)) + " 少=" + string.Join(",", expected.Except(keys)));
        }
    }

    [Fact]
    public void RewardSetRow_FractionalAttrDelta_Throws()
    {
        // 表 schema 只承载整数增量（List<int>）；小数/永久属性增量走 Task 8/9 代码路径，映射层 fail fast
        var spec = new RewardSpec { Attrs = new() { new AttrChange { AttrId = 2, Delta = 0.5 } } };
        Assert.Throws<InvalidOperationException>(() => InjectorMapping.RewardSetRow(spec, 99299));
    }

    // ———————————————— BuffRows：蓝图拆表 ————————————————

    private static BuffBlueprint Blueprint(int newId) =>
        Assert.Single(BuffBlueprints.All, b => b.NewId == newId);

    [Fact]
    public void BuffRows_SplitsFieldsByTableOwnership()
    {
        // 命运眷顾Ⅰ（99285，克隆 90）：BuffDuring/BuffDuring 系属 Config_Buff、
        // BuffInterval/BuffMoraleInterval 属 Config_Effect（dump.cs 两类属性集为准）
        var (buff, effect) = InjectorMapping.BuffRows(Blueprint(99285));

        Assert.Equal(99285, buff["ID"]);
        Assert.Equal(-1, buff["BuffDuring"]);         // 永久
        Assert.Equal(0, buff["Overlay"]);
        Assert.Equal(99295, buff["EffectConfigId"]);  // 指到新 effect 行（99290+5）

        Assert.Equal(99295, effect["ID"]);
        Assert.Equal(7200, effect["BuffInterval"]);
        Assert.Equal(20, effect["BuffMoraleInterval"]);

        // 拆表不串门
        Assert.DoesNotContain("BuffInterval", buff.Keys);
        Assert.DoesNotContain("BuffDuring", effect.Keys);
    }

    [Fact]
    public void BuffRows_H1Clone_HasPinnedEffectValuesAndCatalogReferencesNewId()
    {
        // 顺手修①：99280 心结照抄克隆的数值效果显式钉死（Morale/StaminaAdd=−0.005，防源行漂移）
        var (buff, effect) = InjectorMapping.BuffRows(Blueprint(99280));
        Assert.Equal(-0.005, effect["BuffMoraleAdd"]);
        Assert.Equal(-0.005, effect["BuffStaminaAdd"]);
        Assert.Equal(99290, buff["EffectConfigId"]);

        // 99281 失眠阴影钉死 StaminaMax=−20（源 Effect 200080007 同值，防漂移）
        var (_, effect2) = InjectorMapping.BuffRows(Blueprint(99281));
        Assert.Equal(-20, effect2["BuffStaminaMax"]);

        // H1/D3 的 buff 引用=克隆行 99280（非源 24112）——目录数据钉死
        Assert.Equal(new List<int> { 99280 }, ById(EventCatalog.Ids.H1).Reward.Buffs);
        Assert.Equal(new List<int> { 99280, 99281 }, ById(EventCatalog.Ids.D3).Reward.Buffs);
    }

    [Fact]
    public void BuffRows_WritesChineseNameAndDesAsKeys()
    {
        // 键与 _Local 同值（Task 2 事件行同款）：GetLocalText 键 miss 原样透传，
        // 防克隆行残留 "Buff_Name_11002" 官方键命中官方文案穿帮
        var (buff, _) = InjectorMapping.BuffRows(Blueprint(99282));
        Assert.Equal("蚊虫叮咬", buff["Name"]);
        Assert.Equal("蚊虫叮咬", buff["Name_Local"]);
        Assert.Equal("昨晚房里有一只蚊子，你输了：精力上限-5，持续12小时", buff["Des"]);
    }

    [Fact]
    public void BuffRows_UnknownFieldName_Throws()
    {
        var bad = new BuffBlueprint
        {
            NewId = 99299, CloneSourceId = 90, Name = "x", Des = "x",
            FieldOverrides = new Dictionary<string, object> { ["BuffDuringX"] = 1 },
        };
        Assert.Throws<InvalidOperationException>(() => InjectorMapping.BuffRows(bad));
    }

    [Fact]
    public void BuffRows_AllFourteen_RouteEveryOverrideWithIds()
    {
        foreach (var b in BuffBlueprints.All)
        {
            var (buff, effect) = InjectorMapping.BuffRows(b);
            var expected = b.FieldOverrides.Keys.ToHashSet();
            expected.UnionWith(new[] { "ID", "Name", "Des", "EffectConfigId" });
            Assert.Equal(expected, buff.Keys.Union(effect.Keys).ToHashSet());

            var effectId = InjectorMapping.EffectIdOf(b);
            Assert.Equal(b.NewId, buff["ID"]);
            Assert.Equal(effectId, buff["EffectConfigId"]);
            Assert.Equal(effectId, effect["ID"]);
        }
    }

    [Fact]
    public void EffectIds_UseIndependentEffectTableSegment()
    {
        // Effect 行 99290..99303 独立编号（与 buff 表 99280..99293 不同表，不冲突）；
        // 编号方案=99290+（NewId−99280），与 RewardSet 99250..99299 数值重叠亦无冲突（不同表）
        Assert.Equal(99290, InjectorMapping.EffectIdOf(Blueprint(99280)));
        Assert.Equal(99303, InjectorMapping.EffectIdOf(Blueprint(99293)));
        Assert.Equal(14, BuffBlueprints.All.Select(InjectorMapping.EffectIdOf).Distinct().Count());
    }
}
