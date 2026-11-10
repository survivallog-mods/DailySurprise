using System;
using SurvivalLog.ModShared;

namespace SurvivalLog.DailySurprise;

/// <summary>EnsureInjected 的结果：当前进程内本 mod 自注入行的在册行数。
/// null（EnsureInjected 返回 null）≠ 空报告：null=表未就绪/未全绿（调用方下轮再试），
/// 空字段=0 行=尚未注入。Buff/Effect 表 Task 8+ 落地，本任务恒 0。</summary>
internal sealed class InjectionReport
{
    public int Events;
    public int RewardSets;
    public int BuffRows;
    public int EffectRows;
}

/// <summary>首验 spike 注入器（Task 2 专用最小版）：Config_Event 写 1 行（99200，样式抄原生行
/// 10011/103，见 Core/SpikeRowTemplate 注）+ Config_RewardSet 写 1 行（99250=4×压缩饼干 2101）。
/// 时机/幂等/哨兵照抄 EndlessGuard ArrowTower 定案模式：
/// - Cm() 单例就绪 ≠ dict 就绪（2026-10-24 实锤），两字典任一 null=装表中，下轮再试；
/// - 哨兵=行 99200/99250 在字典且行形态与当前 cfg 变体一致（官方 ConfigManager.Initialize 重建
///   字典后哨兵转红，ticker 自动重放；cfg 翻转 DebugSpikeChose 后行形态过期同样重放）；
/// - Inject 行级幂等：行在则复用对象、全字段无条件拍平（半途异常重试不被判重挡死）；
/// - 全部 List 字段全新 Il2Cpp 实例逐元素填（官方消费方直读 .Count，null/共享实例有 NRE/Clear 连坐
///   风险——ArrowTower 教训）。表是进程内存态，不写任何存档。
/// 触发 API（dump.cs 2026-11-05_26454866）：
/// - DynamicEventManager.TryForceTriggerEvent(int eventId, bool ignorePlayerSelect=False)（dump.cs:415573，
///   返回 bool=是否触发成功，失败原因打 trace 供首验判读）；
/// - 管理器可达性：BattleLogicWorld._DynamicEventManager（internal 属性 dump.cs:385844，interop 代理
///   全公开——同字段 _AgentManager 已被 EndlessGuard HealModellessDevices 在役使用）。
/// </summary>
internal static class TableInjector
{
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

    /// <summary>选项变体开关（验证B）：直接读插件 cfg（EndlessGuard ArrowTower 读
    /// EndlessGuardPlugin.ArrowTowerEnabled 同款跨类静态引用）。</summary>
    private static bool ChoseVariant => DailySurprisePlugin.DebugSpikeChose is { Value: true };

    /// <summary>哨兵复查：两行在册 + 事件行形态（Type）与当前变体一致。读行字段可抛
    /// （interop 病态）→ 按缺行处理触发重放（幂等无害）。</summary>
    private static bool SentinelsGreen(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Event> evDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RewardSet> rsDict)
    {
        try
        {
            if (!evDict.ContainsKey(SpikeRowTemplate.EventId) || !rsDict.ContainsKey(SpikeRowTemplate.RewardSetId))
                return false;
            var row = evDict[SpikeRowTemplate.EventId];
            if (row == null) return false;
            return row.Type == (ChoseVariant ? SpikeRowTemplate.TypeB : SpikeRowTemplate.TypeA);
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
            var evCreated = !evDict.TryGetValue(SpikeRowTemplate.EventId, out var ev) || ev == null;
            if (evCreated)
            {
                ev = new GameCore.HotUpdate.Config_Event();
                evDict[SpikeRowTemplate.EventId] = ev;
            }
            var chose = ChoseVariant;
            FillEventRow(ev, chose);

            var rsCreated = !rsDict.TryGetValue(SpikeRowTemplate.RewardSetId, out var rs) || rs == null;
            if (rsCreated)
            {
                rs = new GameCore.HotUpdate.Config_RewardSet();
                rsDict[SpikeRowTemplate.RewardSetId] = rs;
            }
            FillRewardSetRow(rs);

            DsTrace.Write("inject", "rows injected: event=" + SpikeRowTemplate.EventId
                + (evCreated ? "(新建)" : "(拍平)") + " 变体=" + (chose ? "B(选项,样式源103)" : "A(直发,样式源10011)")
                + " type=" + ev.Type + " popup=" + ev.PopUpType + " keep=" + ev.KeepTime
                + " StartRewardSetId=" + ev.StartRewardSetId
                + " | rewardSet=" + SpikeRowTemplate.RewardSetId + (rsCreated ? "(新建)" : "(拍平)")
                + " ItemChange=" + DescribeItems(rs.ItemChange));
        }
        catch (Exception e) { DsTrace.Write("inject", "Inject 异常: " + e.Message); }
    }

    /// <summary>Config_Event 99200 全字段拍平（样式来源与刻意偏离见 SpikeRowTemplate 类注；
    /// 未提及字段=显式安全缺省：空串/空列表/0，官方消费方不读出未定义态）。</summary>
    private static void FillEventRow(GameCore.HotUpdate.Config_Event ev, bool chose)
    {
        ev.ID = SpikeRowTemplate.EventId;
        ev.Name = SpikeEvent.Instance.Title;               // 裸中文直写（GetLocalText 键 miss 原样透传，EndlessGuard 实锤）
        ev.Name_Local = SpikeEvent.Instance.Title;
        ev.PlayerSelectId = L();                            // 空列表：不限角色（模板行 10011/103 均未设）
        ev.DemoTwoMode = chose ? SpikeRowTemplate.DemoTwoModeB : SpikeRowTemplate.DemoTwoModeA;
        ev.EndlessMode = SpikeRowTemplate.EndlessMode;
        ev.Type = chose ? SpikeRowTemplate.TypeB : SpikeRowTemplate.TypeA;
        ev.EventLogID = 0;
        ev.LogType = SpikeRowTemplate.LogType;
        ev.ShowType = chose ? SpikeRowTemplate.ShowTypeB : SpikeRowTemplate.ShowTypeA;
        ev.PopUpType = chose ? SpikeRowTemplate.PopUpTypeB : SpikeRowTemplate.PopUpTypeA;
        ev.FurnitureID = L();
        ev.KeepTime = chose ? SpikeRowTemplate.KeepTimeB : SpikeRowTemplate.KeepTimeA;
        ev.IsSon = false;                                   // 独立事件，非任何选项的子事件
        ev.EventDes = SpikeEvent.Instance.Dialogue;
        ev.EventDes_Local = SpikeEvent.Instance.Dialogue;   // 裸中文（brief Step 2：EventDes_Local 裸中文）
        ev.Info = ""; ev.Info_Local = "";
        ev.DoneInfo = ""; ev.DoneInfo_Local = "";
        ev.EventDesTaskEnd = SpikeEvent.Instance.Dialogue;  // 10011 模板：TaskEnd 文案=EventDes 同文
        ev.EventDesTaskEnd_Local = SpikeEvent.Instance.Dialogue;
        ev.Icon = chose ? SpikeRowTemplate.IconB : SpikeRowTemplate.IconA;  // 原生路径（原生资源必在）
        ev.IconEnd = ev.Icon;                               // 10011/103 均与 Icon 同值
        ev.IconRole = LS();
        ev.DataMin = SpikeRowTemplate.DataMin;
        ev.DataMax = SpikeRowTemplate.DataMax;
        ev.DayRate = 0f;
        ev.TimeMin = SpikeRowTemplate.TimeMin;
        ev.TimeMax = SpikeRowTemplate.TimeMax;
        ev.CanRepeat = true;                                // 两模板均 true
        ev.DayCD = chose ? 3 : 1;                           // 103 DayCD=3；10011 DayCD=1
        ev.HourCD = 0;
        ev.StartThink = ""; ev.StartThink_Local = "";       // 无开场内心戏（模板带 towel 文案，不抄）
        if (chose)
        {
            ev.ChoseName = LS(SpikeRowTemplate.ChoseLabels);
            ev.ChoseName_Local = LS(SpikeRowTemplate.ChoseLabels);
            ev.ChoseID = L(SpikeRowTemplate.ChoseIds);
            ev.Default = SpikeRowTemplate.DefaultChoseId;
            ev.Countdown = SpikeRowTemplate.CountdownB;
        }
        else
        {
            ev.ChoseName = LS(); ev.ChoseName_Local = LS(); // 空列表非 null（官方消费方直读 .Count）
            ev.ChoseID = L();
            ev.Default = 0;
            ev.Countdown = 0;
        }
        ev.ActionRequire = L(); ev.ActionTime = L();
        ev.ItemRequire = L(); ev.ItemTime = L();
        ev.TaskTime = 0;                                    // 刻意偏离：任务时限不设（见 SpikeRowTemplate 注）
        ev.OKDes = SpikeRowTemplate.OkDesA;                 // 两模板 OKDes_Local 同为「我知道了」
        ev.OKDes_Local = SpikeRowTemplate.OkDesA;
        ev.OKDoneDes = ""; ev.OKDoneDes_Local = "";
        ev.DoneThink = ""; ev.DoneThink_Local = "";
        ev.TriggerFmod = ""; ev.EndFmod = "";               // 不抄模板音效（rain/抽屉声与台词无关）
        ev.ConditionSetId = 0;                              // 刻意清零门控（见 SpikeRowTemplate 注）
        ev.NeedConditionSetId = 0;
        ev.TaskConditionSetId = 0;
        ev.StartRewardSetId = SpikeRowTemplate.RewardSetId; // brief Step 2：StartRewardSetId→99250（验证A 的观察点）
        ev.RewardSetId = 0;                                 // 刻意置 0：与 StartRewardSet 区分归因（模板 10011 用的是此项）
        ev.ExpiredRewardSetId = 0;
        ev.SMSContactId = 0;
        ev.SMSContent = ""; ev.SMSContent_Local = "";
        ev.TasteRequire = SpikeRowTemplate.TasteRequire;
        ev.GroupKey = ""; ev.GroupDayCD = 0;
        ev.EndingLine = 0; ev.MaxTimes = 0;
    }

    /// <summary>Config_RewardSet 99250 全字段拍平：ItemChange=4×2101（RewardEncoding 语义：itemId
    /// 重复出现=件数，gamekb 行 6420 实证），其余全零/空列表（Source 留痕便于实机 dump 对账）。</summary>
    private static void FillRewardSetRow(GameCore.HotUpdate.Config_RewardSet rs)
    {
        var items = RewardEncoding.ItemChangeOf(SpikeEvent.Instance.Reward);
        rs.ID = SpikeRowTemplate.RewardSetId;
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
