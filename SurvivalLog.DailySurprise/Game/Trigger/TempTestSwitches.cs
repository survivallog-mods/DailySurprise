using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace SurvivalLog.DailySurprise;

/// <summary>★★★ 临时测试开关（作者 2026-11-14 要求，验完整文件删除 + 三处接线还原）★★★
/// 接线点：DailySurprisePlugin.Load（TempTest.Init）/ DsTicker.Update（TempTest.Step）/
/// HourWatcher.Step（RandomDailyHourEnabled 分支）。删除后无任何残留行为。
/// - DebugRandomDailyHour：每天触发小时随机（每日掷 1~23 一次，覆盖 TriggerHour）——作者想
///   观察"每天不定时"的体感；掷值走会话记忆（同日同会话稳定，跨会话重掷——当日已消费由
///   StateStore.LastTriggeredDay 幂等闸兜住，不双触发）。
/// - DebugHourlyPopup：每个游戏小时均匀随机弹一个目录事件卡（45 选 1，进档首拍先弹一次确认
///   开关生效）——只走展示回退链 TriggerEventCard，不掷难度骰、不记 RollState、不代码发放
///   （防物品/buff 洪水灌档；选项事件点选仍走原生子事件声明式发奖，属玩家主动行为）。
/// - DebugForceEventKey：按事件名（"G1 救灾空投"式下拉）完整触发一次——与 DebugForceEventId
///   同链（TriggerEventCard 弹卡+原生结算奖励+ForceGrant 补跑 Op/气泡兜底），触发后写回复位
///   "关闭"（再选即再触发，免读档）。2026-11-23 改版：作者实测证明 T19 后原生卡确认即结算奖励，
///   原"仅展示不发放"为过时认知（T19 前 Start 挂法坏链时期的描述）。</summary>
internal static class TempTest
{
    internal static ConfigEntry<bool> RandomDailyHour;
    internal static ConfigEntry<bool> HourlyPopup;
    internal static ConfigEntry<string> ForceEventKey;
    internal static ConfigEntry<bool> ScanFurnitures;

    internal static void Init(ConfigFile config)
    {
        RandomDailyHour = config.Bind("General", "DebugRandomDailyHour", false,
            "临时测试（验完删除）：每天触发小时随机（每日掷 1~23 一次，覆盖 TriggerHour）。");
        HourlyPopup = config.Bind("General", "DebugHourlyPopup", false,
            "临时测试（验完删除）：每个游戏小时随机弹一个目录事件卡（45 选 1，仅展示不发放不记账；选项事件点选仍原生发奖）。");
        // 下拉选项 = "关闭" + 45 个 "Key 标题"（面板 sel 显示即值，玩家不用记 ID）
        var names = new List<string> { "关闭" };
        foreach (var e in EventCatalog.Events) names.Add(e.Key + " " + e.Title);
        ForceEventKey = config.Bind("General", "DebugForceEventKey", "关闭",
            new ConfigDescription("临时测试（验完删除）：面板选事件=进档后完整触发一次（奖励原生结算+Op 惩罚执行，与 DebugForceEventId 同链）；触发完自动复位为\"关闭\"，再选即再触发，无需读档。",
                new AcceptableValueList<string>(names.ToArray())));
        ScanFurnitures = config.Bind("General", "DebugScanFurnitures", false,
            "临时诊断（验完删除）：进档后扫描全屋家具逐件写 trace\\scan.txt（AgentConfigId/名称/运行时类型/耐久组件有无+数值）——定位耐久池空根因（2026-11-23 落空实证：屋内无可扣耐久的防御设施/门窗）。");
    }

    /// <summary>RandomDailyHour 开关现值（Init 前=false 兜底，不抛）。</summary>
    internal static bool RandomDailyHourEnabled => RandomDailyHour?.Value ?? false;

    // —— 随机每日小时（HourWatcher 消费） ——

    private static readonly Random Rng = new();
    private static int _memoDay = -1;
    private static int _memoHour;

    /// <summary>当日随机触发小时（1~23，HourGateReached 要求 &gt;0）：每日首见掷一次并写
    /// trace\hourtest.txt（作者可核对当天掷到了几点）。同会话同日返回稳定值。</summary>
    internal static int RandomHourFor(int day)
    {
        if (day != _memoDay)
        {
            _memoDay = day;
            _memoHour = Rng.Next(1, 24);
            DsTrace.Write("hourtest", "day=" + day + " 随机触发小时=" + _memoHour + "（DebugRandomDailyHour）");
        }
        return _memoHour;
    }

    // —— DsTicker 每 2s 入口 ——

    private static GameCore.HotUpdate.Battle.Logic.BattleLogicWorld _armedOn;
    private static int _lastKey = -1;   // day*100+hour；-1=下拍重弹（进档首拍/表未就绪重试）
    private static GameCore.HotUpdate.Battle.Logic.BattleLogicWorld _forcedOn;
    private static string _forcedKey;

    internal static void Step()
    {
        try
        {
            var world = EligibilityChecker.World();
            if (world == null) return;   // 未进档/世界未建：静默
            if (!ReferenceEquals(_armedOn, world)) { _armedOn = world; _lastKey = -1; }   // 进档/换档：先弹一次
            ForceStep(world);
            HourlyStep(world);
            ScanStep(world);
        }
        catch (Exception e) { DsTrace.WriteThrottled("hourtest", "TempTest.Step: " + e.Message); }
    }

    /// <summary>全屋家具诊断扫描（DebugScanFurnitures）：进档后写 trace\scan.txt——每件打
    /// AgentConfigId/配置名/运行时类型名/耐久组件有无与数值。**等家具就绪**：进档初期
    /// GetAllFurnitures 返回 0 件（列表异步填充，2026-11-23 00:15 实测首拍 0 件=耐久池空根因），
    /// 轮询至 Count&gt;0 才扫，最多等 150 拍（5 分钟）后按 0 件结论扫。</summary>
    private static GameCore.HotUpdate.Battle.Logic.BattleLogicWorld _scannedOn;
    private static int _scanWaits;

    private static void ScanStep(GameCore.HotUpdate.Battle.Logic.BattleLogicWorld world)
    {
        if (!(ScanFurnitures?.Value ?? false)) return;
        if (ReferenceEquals(_scannedOn, world)) return;   // 本档已扫：开关关闭再开或换档重扫

        var list = TryGetList();
        if (list == null) return;   // world/_AgentManager 未建：下拍再试
        if (list.Count == 0 && _scanWaits < 150)
        {
            _scanWaits++;           // 家具列表异步填充中：等（首拍 0 件≠空屋，见头注）
            return;
        }
        _scannedOn = world;
        _scanWaits = 0;
        DsTrace.Write("scan", "GetAllFurnitures 共 " + list.Count + " 件（逐件：cfgId 名称 | 运行时类型 | 耐久组件）");
        var withDura = 0;
        foreach (var f in list)
        {
            if (f == null) continue;
            try
            {
                var dura = GameCore.HotUpdate.Battle.Logic.AgentTools
                    .GetAgentComponent<GameCore.HotUpdate.Battle.Logic.FurnitureDurabilityComponent>(f);
                string cfgName = "?";
                try
                {
                    var dict = SurvivalLog.ModShared.BagTable.FurnDict(SurvivalLog.ModShared.BagTable.Cm());
                    if (dict != null && dict.TryGetValue(f.AgentConfigId, out var c) && c != null) cfgName = c.Name_Local;
                }
                catch { }
                var typeName = "?";
                try { typeName = f.GetType().Name; } catch { }
                var duraTxt = "无";
                if (dura != null)
                {
                    withDura++;
                    duraTxt = "有 max=" + dura.GetMaxDurability() + " cur=" + dura.GetCurrentDurability();
                }
                DsTrace.Write("scan", "  cfg=" + f.AgentConfigId + "「" + cfgName + "」 | " + typeName + " | " + duraTxt);
            }
            catch (Exception e) { DsTrace.WriteThrottled("scan", "逐件扫描异常: " + e.Message); }
        }
        DsTrace.Write("scan", "扫描完成：带耐久组件 " + withDura + "/" + list.Count + " 件（DurabilityPool 同口径）");
    }

    private static Il2CppSystem.Collections.Generic.List<GameCore.HotUpdate.Battle.Logic.Furniture> TryGetList()
    {
        try
        {
            return EligibilityChecker.World()?._AgentManager?.GetAllFurnitures();
        }
        catch { return null; }
    }

    /// <summary>按事件名完整触发一次（DebugForceEventKey，2026-11-23 改版）：与 DebugForceEventId
    /// 完全同链——TriggerEventCard 弹卡（原生卡确认=RewardSet 声明式结算奖励）+ ForceGrant 补跑
    /// Op 系惩罚与气泡兜底。触发成功后**写回复位"关闭"**：面板再选一次=再触发一次，免读档免换值。
    /// 时序主防=就绪闸（表注入完 + Configs 补注册完才触发，2026-11-23 00:15 根因：触发比 registrar
    /// postfix 早 7 秒→快照查无行→全通道失败）；残余全失败（就绪后偶发）保留 15 拍兜底上限（不
    /// ForceGrant 不复位防双发，超限放弃复位留痕）。</summary>
    private static int _forceFails;

    private static void ForceStep(GameCore.HotUpdate.Battle.Logic.BattleLogicWorld world)
    {
        var key = ForceEventKey?.Value;
        if (string.IsNullOrEmpty(key) || key == "关闭") { _forceFails = 0; return; }
        if (ReferenceEquals(_forcedOn, world) && _forcedKey == key) return;   // 写回前的瞬时防重

        var def = FindByKey(key);
        if (def == null)
        {
            DsTrace.WriteThrottled("hourtest", "DebugForceEventKey 解析不到事件: " + key);
            ForceEventKey.Value = "关闭";   // 非法值复位（下拉值集外的手改）
            _forcedKey = "关闭";            // 指纹同步（同上 bug 修）
            return;
        }
        if (TableInjector.EnsureInjected() == null) return;   // 四表未就绪：下拍再试（不记指纹不丢机会）
        if (!SpikeForce.ConfigsRegistered()) return;          // 快照就绪闸（2026-11-23 根修）：注册前
                                                             // 触发恒全通道失败，等就绪后触发一次

        _forcedOn = world; _forcedKey = key;
        var channel = DailyFlow.TriggerEventCard(def.Id, def);
        if (channel == CardChannel.None)
        {
            _forceFails++;
            if (_forceFails < 15)
            {
                _forcedOn = null;   // 清指纹下拍重试（不 ForceGrant 不复位——防与重试成功后的原生结算双发）
                DsTrace.WriteThrottled("hourtest", "force「" + key + "」全通道失败（进档初期 UI 未就绪时序），第 "
                    + _forceFails + " 拍将重试");
                return;
            }
            DsTrace.Write("hourtest", "force「" + key + "」重试 15 拍仍全通道失败——放弃并复位（本次不发放不执行）");
            _forceFails = 0;
            ForceEventKey.Value = "关闭";
            return;
        }
        var retried = _forceFails > 0 ? "（重试" + _forceFails + "拍后成功）" : "";
        _forceFails = 0;
        DsTrace.Write("hourtest", "force「" + key + "」id=" + def.Id + " -> " + channel + retried
            + "（DebugForceEventKey：与 ID 强制同链全量执行，触发后复位）");
        SpikeForce.ForceGrant(def.Id, channel);   // Op 系惩罚 + 气泡兜底（同 ID 强制链）
        ForceEventKey.Value = "关闭";             // 触发即复位：面板再选一次=再触发一次
        _forcedKey = "关闭";                      // 指纹同步复位（2026-11-23 bug 修：否则同档内重选同一
                                                  // 事件时指纹仍命中旧值被跳过=只能读档重触发）
    }

    /// <summary>下拉值（"G1 救灾空投"）→ 目录事件：按 Key 前缀匹配（Key 与标题间固定一空格）。</summary>
    private static EventDef FindByKey(string key)
    {
        foreach (var e in EventCatalog.Events)
            if (key.StartsWith(e.Key + " ", StringComparison.Ordinal)) return e;
        return null;
    }

    /// <summary>每小时随机弹窗（DebugHourlyPopup）：day+hour 变化即弹；进档首拍先弹一次
    /// （确认开关生效）；表未就绪本拍不算数（下拍 2s 重试）。</summary>
    private static void HourlyStep(GameCore.HotUpdate.Battle.Logic.BattleLogicWorld world)
    {
        if (!(HourlyPopup?.Value ?? false)) return;

        var gtm = world._GameTimeManager;
        if (gtm == null) return;
        int day, hour;
        try { day = gtm.GetDay(); hour = gtm.GetHour24(); }
        catch (Exception e) { DsTrace.WriteThrottled("hourtest", "读时间异常: " + e.Message); return; }
        if (day <= 0) return;

        var key = day * 100 + hour;
        if (key == _lastKey) return;
        _lastKey = key;

        if (TableInjector.EnsureInjected() == null)
        {
            _lastKey = -1;   // 四表未就绪：本小时不算数，下拍（2s）重试
            return;
        }

        var list = EventCatalog.Events;
        if (list.Count == 0) return;
        var def = list[Rng.Next(list.Count)];
        var channel = DailyFlow.TriggerEventCard(def.Id, def);
        DsTrace.Write("hourtest", "day=" + day + " hour=" + hour + " event=" + def.Id
            + "「" + def.Title + "」-> "
            + (channel != CardChannel.None ? channel.ToString() : "全通道失败") + "（DebugHourlyPopup）");
    }
}
