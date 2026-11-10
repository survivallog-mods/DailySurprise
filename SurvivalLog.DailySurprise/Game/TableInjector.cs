using System;
using SurvivalLog.ModShared;

namespace SurvivalLog.DailySurprise;

/// <summary>EnsureInjected 的结果：当前进程内本 mod 自注入行的在册行数。
/// null（EnsureInjected 返回 null）≠ 空报告：null=表未就绪/未全绿（调用方下轮再试），
/// 空字段=0 行=尚未注入。Buff/Effect 表 Task 6+ 落地，本任务恒 0。</summary>
internal sealed class InjectionReport
{
    public int Events;
    public int RewardSets;
    public int BuffRows;
    public int EffectRows;
}

/// <summary>单行注入器（Task 2 首验形态，Task 4 起数据源=EventCatalog 的 G1「救灾空投」，
/// Task 6 重写为四表全量注入）：Config_Event 写 1 行（99205，样式抄原生行 10011/103，
/// 见下方 Style 常量区注）+ Config_RewardSet 写 1 行（99250=G1 奖励 90压缩饼干×4+饼干×6）。
/// 时机/幂等/哨兵照抄 EndlessGuard ArrowTower 定案模式：
/// - Cm() 单例就绪 ≠ dict 就绪（2026-10-24 实锤），两字典任一 null=装表中，下轮再试；
/// - 哨兵=行 99205/99250 在字典且行形态与当前 cfg 变体一致（官方 ConfigManager.Initialize 重建
///   字典后哨兵转红，ticker 自动重放；cfg 翻转 DebugSpikeChose 后行形态过期同样重放）；
/// - Inject 行级幂等：行在则复用对象、全字段无条件拍平（半途异常重试不被判重挡死）；
/// - 全部 List 字段全新 Il2Cpp 实例逐元素填（官方消费方直读 .Count，null/共享实例有 NRE/Clear 连坐
///   风险——ArrowTower 教训）。表是进程内存态，不写任何存档。
/// 触发 API（dump.cs 2026-11-05_26454866）：
/// - DynamicEventManager.TryForceTriggerEvent(int eventId, bool ignorePlayerSelect=False)（dump.cs:415573，
///   返回 bool=是否触发成功，失败原因打 trace 供首验判读）；
/// - 管理器可达性：BattleLogicWorld._DynamicEventManager（internal 属性 dump.cs:385844，interop 代理
///   全公开——同字段 _AgentManager 已被 EndlessGuard HealModellessDevices 在役使用）。
/// 注：G1 的 Icon 用原生资源路径（ds_g1.png 要到 Task 11 配图生产线才落盘），故不读 EventDef.Icon。</summary>
internal static class TableInjector
{
    // —— 数据源：EventCatalog G1「救灾空投」（Id 99205；台词/奖励逐字=spec §8.2 首行）——
    private static EventDef G1 { get; } = FindG1();

    private static EventDef FindG1()
    {
        foreach (var e in EventCatalog.Events)
            if (e.Id == EventCatalog.Ids.G1)
                return e;
        throw new InvalidOperationException("EventCatalog 缺少 G1(99205)");
    }

    /// <summary>RewardSet 行 Id：沿首验值 99250（Task 6 起按事件序号重排 99250+）。</summary>
    private const int RewardSetId = 99250;

    /// <summary>选项变体开关（验证B，cfg DebugSpikeChose——Task 7 清理）：直接读插件 cfg
    /// （EndlessGuard ArrowTower 读 EndlessGuardPlugin.ArrowTowerEnabled 同款跨类静态引用）。</summary>
    private static bool ChoseVariant => DailySurprisePlugin.DebugSpikeChose is { Value: true };

    // —— 行样式常量（Task 2 spike 取证值原样内联；Task 6 重写时随 InjectorMapping 数据化）——
    // 逐字照抄 gamekb Config_Event.json 原生行（出处见各常量注）：
    // - A（默认，无选项直发，验证A）：原生行 10011「暴雨侵袭-用毛巾堵漏」（Type=2 Result）
    //   Type=2 / ShowType=1 / PopUpType=1 / KeepTime=0 / Icon=EventNew/022 / OKDes_Local=我知道了；
    // - B（DebugSpikeChose=true，两选项，验证B）：原生行 103「事件3」（Type=1 Choice）
    //   Type=1 / ShowType=1 / PopUpType=3 / KeepTime=0 / Countdown=60 / Default=末选项子事件。
    // 两模板共有：EndlessMode/LogType/DataMin/DataMax/TimeMin/TimeMax/TasteRequire=-1（逐字核对一致）；
    // DemoTwoMode 两模板不一致（10011=2、103=3），按各自模板取。
    // 刻意偏离模板清零的门控字段（保证强制触发不被表内条件挡）：ConditionSetId /
    // NeedConditionSetId / TaskConditionSetId / TaskTime——模板行 10011 带 NeedConditionSetId=13、
    // 103 带 ConditionSetId=3，是自然随机触发的资格门，对强制路径只添噪声。
    private const int StyleSourceRowA = 10011;
    private const int TypeA = 2;         // 10011 _TypeField=2（Result）
    private const int ShowTypeA = 1;     // 10011 _ShowTypeField=1
    private const int PopUpTypeA = 1;    // 10011 _PopUpTypeField=1
    private const int KeepTimeA = 0;     // 10011 _KeepTimeField=0
    private const string IconA = "Assets/RuntimeAssets/Texture/EventNew/022"; // 10011 _IconField
    private const string OkDesA = "我知道了";                                    // 10011 _OKDes_LocalField

    private const int StyleSourceRowB = 103;
    private const int TypeB = 1;         // 103 _TypeField=1（Choice）
    private const int ShowTypeB = 1;     // 103 _ShowTypeField=1
    private const int PopUpTypeB = 3;    // 103 _PopUpTypeField=3
    private const int KeepTimeB = 0;     // 103 _KeepTimeField=0
    private const int CountdownB = 60;   // 103 _CountdownField=60（倒计时归零自动走 Default）
    private const string IconB = "Assets/RuntimeAssets/Texture/Literature/UI_Item_Icon_Liter_M_1490200101"; // 103 _IconField
    private static readonly string[] ChoseLabels = { "拿走", "算了" };   // brief Step 4 原文
    // 选项子事件指向原生行（本版本只注入 1 行；10011/10012=原生 10010 的两个 Type=2 IsSon 子事件，
    // 各带自己的 RewardSet 6013/6014——点击选项即触发对应原生子事件，行为有官方定义）
    private static readonly int[] ChoseIds = { 10011, 10012 };
    private const int DefaultChoseId = 10012;    // 103 _DefaultField=105=其末选项，同构取末选项

    private const int DemoTwoModeA = 2;  // 10011 _DemoTwoModeField=2
    private const int DemoTwoModeB = 3;  // 103 _DemoTwoModeField=3

    private const int EndlessMode = 1;   // 两行均=1
    private const int LogType = 1;       // 两行均=1（DynamicEventLogType.Normal）
    private const int DataMin = 1;       // 两行均=1
    private const int DataMax = 999;     // 两行均=999
    private const int TimeMin = 1;       // 两行均=1
    private const int TimeMax = 24;      // 两行均=24
    private const int TasteRequire = -1; // 两行均=-1（无口味门槛）

    /// <summary>ticker 每步入口之外的独立入口（当前同一条路）：表就绪且两行在册（含形态一致）
    /// → 返回报告；未就绪/注入后仍未全绿 → null（调用方下轮再试）。</summary>
    internal static InjectionReport EnsureInjected()
    {
        try
        {
            var cm = BagTable.Cm();
            if (cm == null) return null;
            var evDict = cm._Config_Event_Dict;
            var rsDict = cm._Config_RewardSet_Dict;
            if (evDict == null || rsDict == null) return null;   // 装表中，下轮再试

            if (SentinelsGreen(evDict, rsDict)) return Report();
            Inject(evDict, rsDict);
            return SentinelsGreen(evDict, rsDict) ? Report() : null;
        }
        catch (Exception e)
        {
            DsTrace.Write("inject", "EnsureInjected 异常: " + e.Message);
            return null;
        }
    }

    private static InjectionReport Report() => new()
    {
        Events = 1,
        RewardSets = 1,
        BuffRows = 0,
        EffectRows = 0,
    };

    /// <summary>哨兵复查：两行在册 + 事件行形态（Type）与当前变体一致。读行字段可抛
    /// （interop 病态）→ 按缺行处理触发重放（幂等无害）。</summary>
    private static bool SentinelsGreen(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Event> evDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RewardSet> rsDict)
    {
        try
        {
            if (!evDict.ContainsKey(G1.Id) || !rsDict.ContainsKey(RewardSetId))
                return false;
            var row = evDict[G1.Id];
            if (row == null) return false;
            return row.Type == (ChoseVariant ? TypeB : TypeA);
        }
        catch { return false; }
    }

    /// <summary>行级幂等注入：行在则复用、全字段无条件拍平。任一半途异常整体记 trace（哨兵下轮重放）。</summary>
    private static void Inject(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Event> evDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RewardSet> rsDict)
    {
        try
        {
            var evCreated = !evDict.TryGetValue(G1.Id, out var ev) || ev == null;
            if (evCreated)
            {
                ev = new GameCore.HotUpdate.Config_Event();
                evDict[G1.Id] = ev;
            }
            var chose = ChoseVariant;
            FillEventRow(ev, chose);

            var rsCreated = !rsDict.TryGetValue(RewardSetId, out var rs) || rs == null;
            if (rsCreated)
            {
                rs = new GameCore.HotUpdate.Config_RewardSet();
                rsDict[RewardSetId] = rs;
            }
            FillRewardSetRow(rs);

            DsTrace.Write("inject", "rows injected: event=" + G1.Id + "(" + G1.Title + ")"
                + (evCreated ? "(新建)" : "(拍平)") + " 变体=" + (chose ? "B(选项,样式源103)" : "A(直发,样式源10011)")
                + " type=" + ev.Type + " popup=" + ev.PopUpType + " keep=" + ev.KeepTime
                + " StartRewardSetId=" + ev.StartRewardSetId
                + " | rewardSet=" + RewardSetId + (rsCreated ? "(新建)" : "(拍平)")
                + " ItemChange=" + DescribeItems(rs.ItemChange));
        }
        catch (Exception e) { DsTrace.Write("inject", "Inject 异常: " + e.Message); }
    }

    /// <summary>Config_Event 99205 全字段拍平（样式来源与刻意偏离见样式常量区注；
    /// 未提及字段=显式安全缺省：空串/空列表/0，官方消费方不读出未定义态）。</summary>
    private static void FillEventRow(GameCore.HotUpdate.Config_Event ev, bool chose)
    {
        ev.ID = G1.Id;
        ev.Name = G1.Title;                                  // 裸中文直写（GetLocalText 键 miss 原样透传，EndlessGuard 实锤）
        ev.Name_Local = G1.Title;
        ev.PlayerSelectId = L();                             // 空列表：不限角色（模板行 10011/103 均未设）
        ev.DemoTwoMode = chose ? DemoTwoModeB : DemoTwoModeA;
        ev.EndlessMode = EndlessMode;
        ev.Type = chose ? TypeB : TypeA;
        ev.EventLogID = 0;
        ev.LogType = LogType;
        ev.ShowType = chose ? ShowTypeB : ShowTypeA;
        ev.PopUpType = chose ? PopUpTypeB : PopUpTypeA;
        ev.FurnitureID = L();
        ev.KeepTime = chose ? KeepTimeB : KeepTimeA;
        ev.IsSon = false;                                    // 独立事件，非任何选项的子事件
        ev.EventDes = G1.Dialogue;
        ev.EventDes_Local = G1.Dialogue;                     // 裸中文（首验结论：GetLocalText 键 miss 原样透传）
        ev.Info = ""; ev.Info_Local = "";
        ev.DoneInfo = ""; ev.DoneInfo_Local = "";
        ev.EventDesTaskEnd = G1.Dialogue;                    // 10011 模板：TaskEnd 文案=EventDes 同文
        ev.EventDesTaskEnd_Local = G1.Dialogue;
        ev.Icon = chose ? IconB : IconA;                     // 原生路径（原生资源必在；ds_g1.png 待 Task 11）
        ev.IconEnd = ev.Icon;                                // 10011/103 均与 Icon 同值
        ev.IconRole = LS();
        ev.DataMin = DataMin;
        ev.DataMax = DataMax;
        ev.DayRate = 0f;
        ev.TimeMin = TimeMin;
        ev.TimeMax = TimeMax;
        ev.CanRepeat = true;                                 // 两模板均 true
        ev.DayCD = chose ? 3 : 1;                            // 103 DayCD=3；10011 DayCD=1
        ev.HourCD = 0;
        ev.StartThink = ""; ev.StartThink_Local = "";        // 无开场内心戏（模板带 towel 文案，不抄）
        if (chose)
        {
            ev.ChoseName = LS(ChoseLabels);
            ev.ChoseName_Local = LS(ChoseLabels);
            ev.ChoseID = L(ChoseIds);
            ev.Default = DefaultChoseId;
            ev.Countdown = CountdownB;
        }
        else
        {
            ev.ChoseName = LS(); ev.ChoseName_Local = LS();  // 空列表非 null（官方消费方直读 .Count）
            ev.ChoseID = L();
            ev.Default = 0;
            ev.Countdown = 0;
        }
        ev.ActionRequire = L(); ev.ActionTime = L();
        ev.ItemRequire = L(); ev.ItemTime = L();
        ev.TaskTime = 0;                                     // 刻意偏离：任务时限不设（见样式常量区注）
        ev.OKDes = OkDesA;                                   // 两模板 OKDes_Local 同为「我知道了」
        ev.OKDes_Local = OkDesA;
        ev.OKDoneDes = ""; ev.OKDoneDes_Local = "";
        ev.DoneThink = ""; ev.DoneThink_Local = "";
        ev.TriggerFmod = ""; ev.EndFmod = "";                // 不抄模板音效（rain/抽屉声与台词无关）
        ev.ConditionSetId = 0;                               // 刻意清零门控（见样式常量区注）
        ev.NeedConditionSetId = 0;
        ev.TaskConditionSetId = 0;
        ev.StartRewardSetId = RewardSetId;                   // 直发变体的观察点
        ev.RewardSetId = 0;                                  // 刻意置 0：与 StartRewardSet 区分归因（模板 10011 用的是此项）
        ev.ExpiredRewardSetId = 0;
        ev.SMSContactId = 0;
        ev.SMSContent = ""; ev.SMSContent_Local = "";
        ev.TasteRequire = TasteRequire;
        ev.GroupKey = ""; ev.GroupDayCD = 0;
        ev.EndingLine = 0; ev.MaxTimes = 0;
    }

    /// <summary>Config_RewardSet 99250 全字段拍平：ItemChange=G1 奖励展开（RewardEncoding 语义：
    /// itemId 重复出现=件数，gamekb 行 6420 实证）=4×2101+6×1，其余全零/空列表（Source 留痕便于实机 dump 对账）。</summary>
    private static void FillRewardSetRow(GameCore.HotUpdate.Config_RewardSet rs)
    {
        var items = RewardEncoding.ItemChangeOf(G1.Reward);
        rs.ID = RewardSetId;
        rs.Source = "DailySurprise_Spike";
        rs.AttriKeyChange = L(); rs.AttriValueChange = L();
        rs.SurvivalPoint = 0; rs.BeliefPoint = 0;
        rs.ItemChange = L(items.ToArray());
        rs.ItemPackage = L(); rs.ItemCost = L();
        rs.ClearFurnitureBag = L();
        rs.FurniturelD = L(); rs.FurnitureHP = L();
        rs.BuffTargetType = L(); rs.BuffTargetID = L();
        rs.AddBuff = L(); rs.DelBuff = L(); rs.ChangeStatus = L();
        rs.AddTag = LS(); rs.DelTag = LS(); rs.ForbidTag = LS(); rs.ForbidTime = L();
        rs.AddSymbol = LS(); rs.DelSymbol = LS();
        rs.UnlockSystem = L(); rs.ShowProductionList = L(); rs.ShowRecipeList = L();
        rs.UnlockProductionCraft = L(); rs.UnlockCookingCraft = L();
        rs.GetAchievemenet = 0;
        rs.ActiveEvent = L(); rs.ActiveStory = L();
        rs.MoneyAdd = 0;
        rs.ShowGuidanceFurnitureID = L(); rs.HideGuidanceFurnitureID = L();
        rs.RandomGroupId = 0; rs.RandomGroupCount = 0;
        rs.ExposureValue = 0; rs.EventExp = 0; rs.ExpType = "";
        rs.ClearFurnitureBagSatiety = 0;
        rs.MetNpcIds = L(); rs.RemoveEvent = L();
        rs.CounterKey = ""; rs.CounterAdd = 0;
    }

    /// <summary>Il2Cpp int 列表构造（每字段全新实例——官方消费方直读 .Count，null/共享实例有连坐风险）。</summary>
    private static Il2CppSystem.Collections.Generic.List<int> L(params int[] v)
    {
        var l = new Il2CppSystem.Collections.Generic.List<int>();
        if (v != null) foreach (var x in v) l.Add(x);
        return l;
    }

    /// <summary>Il2Cpp string 列表构造（ChoseName(_Local) 等 List&lt;string&gt; 字段用）。</summary>
    private static Il2CppSystem.Collections.Generic.List<string> LS(params string[] v)
    {
        var l = new Il2CppSystem.Collections.Generic.List<string>();
        if (v != null) foreach (var x in v) l.Add(x);
        return l;
    }

    /// <summary>取证行：ItemChange 明细（"2101×4" 风格；读失败=原样问号）。</summary>
    private static string DescribeItems(Il2CppSystem.Collections.Generic.List<int> list)
    {
        try
        {
            if (list == null || list.Count == 0) return "(空)";
            var parts = new System.Text.StringBuilder();
            int cur = 0, n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (i == 0) { cur = list[0]; n = 1; continue; }
                if (list[i] == cur) { n++; continue; }
                parts.Append(cur).Append('×').Append(n).Append(' ');
                cur = list[i]; n = 1;
            }
            parts.Append(cur).Append('×').Append(n);
            return parts.ToString();
        }
        catch { return "(读取失败)"; }
    }
}
