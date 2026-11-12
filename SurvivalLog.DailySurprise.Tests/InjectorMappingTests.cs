using System;
using System.Collections.Generic;
using System.Linq;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>InjectorMapping 纯函数契约测试（Task 6 Step 1，先写后实现）。
/// 映射层=EventDef/RewardSpec/BuffBlueprint → 表行字段字典（键=Config_* 属性名，值=int/double/
/// string/List&lt;int&gt;/List&lt;string&gt;）；IL2CPP 写入层（Game/TableInjector，测试上下文不可见）
/// 只做"字典→对象属性"搬运。字段名与形态权威=gamekb 2026-11-05_26454866 dump.cs：
/// Config_Event 67 属性（35790 起）、Config_RewardSet 44 属性（46003 起，Attri 编码=平行
/// List&lt;int&gt;，原生行 6011/6013 实证）、Config_Buff 24 属性（28302 起）、Config_Effect
/// （33687 起）。ID 段：RewardSet 基础行=99250+目录序（99250..99294）、选项行 99295..99299
/// （L1×3+H3×2，Task 10 起声明式发奖）、子事件行 99245..99249（事件段 99200..99244 顺延）、
/// Effect 行=99290+蓝图序（99290..99303，Effect 独立表与 buff id 不冲突）。</summary>
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

        // StartRewardSetId=0（Task 8 Controller 裁定：防空双发——原生 1760 行同为 0，弹窗不受影响；
        // 全部奖励统一走 RewardExecutor 代码路径）；RewardSetId 亦 0（Task 2 起归因单一）
        Assert.Equal(0, row["StartRewardSetId"]);
        Assert.Equal(0, row["RewardSetId"]);

        // Icon=EventDef.Icon 相对路径（Task 11 配图生产线的落盘约定，规格 v2 webp）
        Assert.Equal("../../Res/Icon/ds_g1.webp", row["Icon"]);
        Assert.Equal("../../Res/Icon/ds_g1.webp", row["IconEnd"]);

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
        // 顶层不发奖励（各选项子事件 RewardSetId 声明式发奖）；StartRewardSetId=0（Task 8 裁定口径不变）
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
            Assert.Equal("../../Res/Icon/" + e.Icon, (string)row["Icon"]);
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

    /// <summary>Task 8 Controller 裁定钉：全部 45 事件行 StartRewardSetId=0——奖励统一走
    /// RewardExecutor 代码路径（防原生直发与代码路径双发）；RewardSet 行本身保留（展示/备查）。</summary>
    [Fact]
    public void EventRow_AllStartRewardSetIdsZero_CodePathOnly()
    {
        foreach (var e in EventCatalog.Events)
        {
            var row = InjectorMapping.EventRow(e);
            Assert.Equal(0, row["StartRewardSetId"]);
            Assert.Equal(0, row["RewardSetId"]);
            Assert.Equal(0, row["ExpiredRewardSetId"]);
        }
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

        // RewardSetId→选项行 99295（声明式发奖）；条件门清零（Task 2 定案）；图沿用父事件图
        Assert.Equal(99295, first["RewardSetId"]);
        Assert.Equal(0, first["StartRewardSetId"]);
        Assert.Equal(0, first["ConditionSetId"]);
        Assert.Equal("../../Res/Icon/" + ById(EventCatalog.Ids.L1).Icon, (string)first["Icon"]);

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
        // 口径：Buffs 不进 RewardSet（倍率缩放走 Task 8 代码路径）
        Assert.Empty(Assert.IsType<List<int>>(row.Row["AddBuff"]));
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

    [Fact]
    public void RewardSetsOf_G4_ItemsCarriedButBuffsLeftToCodePath()
    {
        // G4=咖啡×4+精神振奋(11004)：ItemChange 只承载物品，buff 由代码路径发放
        var g4 = Assert.Single(RowsOf(EventCatalog.Ids.G4));
        Assert.Equal(new List<int> { 7, 7, 7, 7 }, Assert.IsType<List<int>>(g4.Row["ItemChange"]));
        Assert.Empty(Assert.IsType<List<int>>(g4.Row["AddBuff"]));
    }

    [Fact]
    public void RewardSetsOf_RandomPickOneEvents_LeaveRowEmptyWithCodeMarker()
    {
        // 口径（Task 6 brief）：R2/E1/E3/E4/L2 的随机×1 不在 RewardSet 里解——
        // 行留空 + Source=DS_RandomByCode 标记，随机由 Task 8 代码路径解
        foreach (var id in new[] { EventCatalog.Ids.R2, EventCatalog.Ids.E1, EventCatalog.Ids.E3, EventCatalog.Ids.E4, EventCatalog.Ids.L2 })
        {
            var row = Assert.Single(RowsOf(id));
            Assert.Empty(Assert.IsType<List<int>>(row.Row["ItemChange"]));
            Assert.Equal(InjectorMapping.RandomByCodeTag, row.Row["Source"]);
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

    /// <summary>基础行口径回归钉：非选项行（含带 Buffs 的 G4）仍走代码路径——AddBuff/ItemCost 不承载
    /// （Task 8 Controller 裁定不变；声明式仅选项行启用）。</summary>
    [Fact]
    public void RewardSetRow_BaseRowsStayCodePath_DeclarativeOnlyForOptions()
    {
        var g4 = Assert.Single(RowsOf(EventCatalog.Ids.G4));
        Assert.Empty(Assert.IsType<List<int>>(g4.Row["AddBuff"]));
        Assert.Empty(Assert.IsType<List<int>>(g4.Row["ItemCost"]));

        // 显式 declarative=false 的 H3 基础行：Op 系载荷（StealParts）从不进 RewardSet
        var h3Base = RowsOf(EventCatalog.Ids.H3)[0];
        Assert.Empty(Assert.IsType<List<int>>(h3Base.Row["ItemCost"]));
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
