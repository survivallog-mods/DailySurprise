using System;
using System.Reflection;
using HarmonyLib;

namespace SurvivalLog.DailySurprise;

/// <summary>快照注册器（Task 17 根因修复）：把本 mod 的 50 行 Config_Event 注册进
/// DynamicEventManager.Configs 私有快照（Dictionary&lt;int,DynamicEventConfig&gt;，dump.cs:419768/
/// 420005）——快照是 TryForceTriggerEvent(c1) 与 UI 泵 UpdateUI 的唯一事件源，此前只写
/// ConfigManager._Config_Event_Dict 永远进不了快照，c1 恒 False、c4 入队后被 UI 泵丢弃 → 永不弹卡。
///
/// 挂点（gamekb 2026-11-12_26615062 反汇编取证，与本仓 .superpowers/sdd task-17-report 互证）：
/// - Initialize(GameMode)（dump:420146，RVA 0x26D6880）≠快照构建点：38 字节，仅
///   IsLocked = gameMode==Guide(1)||Explore(2)；且 StartBattle_Early 每次 new Configs 会把
///   先前注册清掉。brief 指定挂 Initialize 属前提偏差，本类改挂真实构建完成点。
/// - 快照构建循环在 StartBattle_AfterAgent(GameSaveData)（dump:420152，RVA 0x26E3F90）：
///   遍历 Get_Config_DynamicEvent_All()=_Config_Event_Dict.Values 新 List，逐行先过滤
///   GameKey.IsRowBlockedInDemoTwo(row.DemoTwoMode)/IsRowBlockedInEndless(row.EndlessMode)，
///   装配 DynamicEventConfig 后 AddConditions/AddTasks/AddResults，Configs.Add(ID,cfg)。
///   本 mod 行 DemoTwoMode=2（直发）/3（选项+子）在正式档全被 DemoTwo 过滤拦截（EndlessMode=1
///   在无尽档另拦）——这是"表里有行快照却没有"的持久原因；另有时序原因（ticker 注入可能晚于
///   战斗开始）。postfix 在原生构建完成后补注册，两因都治。
/// - 字段映射/过滤/条件语义逐字段镜像原生循环（RegistrarPlan.MapConfig 头注有字段级出处）；
///   幂等：Configs 已含 Id 则跳过（Configs 每次战斗重建、本注册每次构建后重放）。
/// - 红线遵守：本补丁目标为普通公开实例方法（0x534 字节，安全 detour 前导），不挂 async MoveNext。
/// trace\registrar.txt：每次构建后一行"新增注册 N 行/已存在 M 行/清单总数"+诊断行
/// （GameMode/IsLocked/双模式开关/探针行双过滤值 ⇒ 缺席原因——时序还是过滤）。</summary>
[HarmonyPatch]
internal static class ConfigRegistrar
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(GameCore.HotUpdate.Battle.Logic.DynamicEventManager), "StartBattle_AfterAgent",
            new[] { typeof(GameCore.HotUpdate.GameSaveData) });

    private static void Postfix(GameCore.HotUpdate.Battle.Logic.DynamicEventManager __instance)
    {
        try { EnsureRegistered(__instance, "postfix"); }
        catch (Exception e) { DsTrace.WriteThrottled("registrar", "postfix 异常: " + e.Message); }
    }

    /// <summary>ticker 兜底通道（DsTicker 每 2s，先于 SpikeForce/每日触发）：覆盖 patch 未挂载/
    /// 首载行未注入（时序）两种残余场景。全绿时只做 50 次 ContainsKey，静默；有变化才留痕。</summary>
    internal static void Step()
    {
        var dem = SpikeForce.LiveDem();
        if (dem == null) return;   // 未进档/世界未建：下轮再试
        EnsureRegistered(dem, "ticker");
    }

    /// <summary>幂等注册主流程：表行确保在册（缺失先同步触发一次四表注入）→ 去重决策 →
    /// 逐行装配注册 → registrar.txt 记账。任何异常只废本轮，不影响原生流程。</summary>
    internal static void EnsureRegistered(GameCore.HotUpdate.Battle.Logic.DynamicEventManager dem, string source)
    {
        var configs = TryGet(() => dem.Configs);
        if (configs == null) return;   // Configs 未建（战斗未开始）：ticker/postfix 下轮再试

        var evDict = EnsureRowsInjected();
        var probeBefore = TryGet(() => configs.ContainsKey(RegistrarPlan.EventIds()[0]));
        var decision = RegistrarPlan.Decide(RegistrarPlan.EventIds(),
            id => TryGet(() => configs.ContainsKey(id)) == true,
            id => TryGet(() => evDict != null && evDict.ContainsKey(id)) == true);

        var added = 0;
        var failed = 0;
        foreach (var id in decision.ToAdd)
        {
            var row = TryGet(() => evDict.TryGetValue(id, out var r) ? r : null);
            if (row == null || RegisterOne(dem, configs, row)) continue;
            failed++;
        }
        added = decision.ToAdd.Count - failed;

        // postfix=每次原生构建后必写一行（binding 诊断口径）；ticker=仅状态有变化时写（防 2s/条刷屏）
        var line = "[source=" + source + "] 新增注册 " + added + " 行/已存在 " + decision.AlreadyPresent.Count
            + " 行/清单总数 " + RegistrarPlan.EventIds().Count
            + "（行缺失 " + decision.RowMissing.Count + "、注册失败 " + failed + "）｜" + Diagnose(dem, evDict, probeBefore == true);
        if (source == "postfix") DsTrace.Write("registrar", line);
        else if (added > 0 || failed > 0 || decision.RowMissing.Count > 0 || decision.AlreadyPresent.Count < RegistrarPlan.EventIds().Count)
            DsTrace.WriteThrottled("registrar", line);
    }

    /// <summary>四表就绪保障：事件行任一缺失时同步跑一趟 EnsureInjected（幂等重放，内部自带
    /// try/catch+trace），随后重读字典；仍缺=行缺失（时序），交由调用方按 rowMissing 记账。</summary>
    private static Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Event> EnsureRowsInjected()
    {
        var cm = SurvivalLog.ModShared.BagTable.Cm();
        var dict = TryGet(() => cm?._Config_Event_Dict);
        if (dict != null && TryGet(() => dict.ContainsKey(RegistrarPlan.EventIds()[0])) == true)
            return dict;
        TableInjector.EnsureInjected();   // 未就绪/缺行：同步补一趟（null=表未就绪，下轮再试）
        return TryGet(() => SurvivalLog.ModShared.BagTable.Cm()?._Config_Event_Dict);
    }

    /// <summary>单行注册（镜像原生装配；失败=false 只废本行不中断整批）：
    /// DayRate≠0 拒注册（进原生随机池=无奖励自发弹卡）；AddConditions 异常拒注册
    /// （空/残缺条件列表会让事件"全条件通过"被原生 TriggerRandomEvent 自发 roll）；
    /// AddTasks/AddResults 异常以空列表兜底（本目录行本就零任务，CdResult 只管 CD 记账）。</summary>
    private static bool RegisterOne(GameCore.HotUpdate.Battle.Logic.DynamicEventManager dem,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Battle.Logic.DynamicEventConfig> configs,
        GameCore.HotUpdate.Config_Event row)
    {
        try
        {
            var dayRate = row.DayRate;
            var shape = RegistrarPlan.MapConfig(new RegistrarPlan.RowShape
            {
                Id = row.ID,
                Type = row.Type,
                TimeMin = row.TimeMin,
                TimeMax = row.TimeMax,
                IsSon = row.IsSon,
                ChoseNameCount = row.ChoseName == null ? 0 : row.ChoseName.Count,
                TaskTime = row.TaskTime,
                TaskConditionSetId = row.TaskConditionSetId,
                GroupKey = row.GroupKey,
                GroupDayCd = row.GroupDayCD,
                DayRate = dayRate,
            });
            if (!RegistrarPlan.IsRollerImmune(dayRate))
            {
                DsTrace.WriteThrottled("registrar", "拒绝注册 " + shape.EventId + "：DayRate=" + dayRate
                    + "≠0 会进原生随机池（无奖励自发弹卡）");
                return false;
            }

            var cfg = new GameCore.HotUpdate.Battle.Logic.DynamicEventConfig
            {
                EventId = shape.EventId,
                EventType = (GameCore.HotUpdate.DynamicEventConfigType)shape.EventType,
                TimeMin = shape.TimeMin,
                TimeMax = shape.TimeMax,
                IsChild = shape.IsChild,
                IsChose = shape.IsChose,
                TaskTime = shape.TaskTime,
                TaskConditionSetId = shape.TaskConditionSetId,
                GroupKey = shape.GroupKey,
                GroupDayCD = shape.GroupDayCd,
                PlayerSelectId = row.PlayerSelectId,   // 原生同款共享引用（空列表=不限角色）
                DynamicConditions = NewCondList(),
                DynamicTasks = NewTaskList(),
                DynamicResults = new Il2CppSystem.Collections.Generic.List<GameCore.HotUpdate.Battle.Logic.IDynamicResult>(),
            };

            try { dem.AddConditions(cfg, row); }
            catch (Exception e)
            {
                DsTrace.WriteThrottled("registrar", "拒绝注册 " + shape.EventId + "：AddConditions 异常 "
                    + e.Message + "（残缺条件列表有进随机池风险，fail closed）");
                return false;
            }
            try { dem.AddTasks(cfg, row); }
            catch (Exception e)
            {
                cfg.DynamicTasks = NewTaskList();
                DsTrace.WriteThrottled("registrar", "注册 " + shape.EventId + "：AddTasks 异常以空任务列表兜底 " + e.Message);
            }
            try { dem.AddResults(cfg, row); }
            catch (Exception e)
            {
                cfg.DynamicResults = new Il2CppSystem.Collections.Generic.List<GameCore.HotUpdate.Battle.Logic.IDynamicResult>();
                DsTrace.WriteThrottled("registrar", "注册 " + shape.EventId + "：AddResults 异常以空结果列表兜底 " + e.Message);
            }

            configs[shape.EventId] = cfg;   // 索引器写入：缺键即增、撞键覆盖（内容与原生构建逐字段一致，
            return true;                    // 防 DemoTwo 档原生先入册时 Add 重复键炸原生加载）
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("registrar", "注册失败 " + TryGet(() => row.ID, -1) + ": " + e.Message);
            return false;
        }
    }

    private static Il2CppSystem.Collections.Generic.List<GameCore.HotUpdate.Battle.Logic.IDynamicCondition> NewCondList() =>
        new();

    private static Il2CppSystem.Collections.Generic.List<GameCore.HotUpdate.Battle.Logic.IDynamicTask> NewTaskList() =>
        new();

    /// <summary>诊断行后半（best-effort，任一项读失败不拦记账）：Initialize(GameMode) 等价值
    /// （BattleLogicWorld.GameModeKey，public getter）+ TryForceTriggerEvent 另一道闸 IsLocked +
    /// 双模式开关 + 探针行（首 Id）双过滤实测 ⇒ 缺席原因（时序 vs 过滤）。</summary>
    private static string Diagnose(GameCore.HotUpdate.Battle.Logic.DynamicEventManager dem,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Event> evDict, bool probeBefore)
    {
        var probeId = RegistrarPlan.EventIds()[0];
        var gameMode = TryGet(() => (int)EligibilityChecker.World().GameModeKey, -1);
        var locked = TryGet(() => dem.IsLocked);
        var demoTwo = TryGet(() => GameCore.HotUpdate.GameKey.IsDemoTwo);
        var endless = TryGet(() => GameCore.HotUpdate.GameKey.IsEndlessSave);
        var row = TryGet(() => evDict != null && evDict.TryGetValue(probeId, out var r) ? r : null);
        var demoMode = TryGet(() => row == null ? -1 : row.DemoTwoMode, -1);
        var endlessMode = TryGet(() => row == null ? -1 : row.EndlessMode, -1);
        var blockedDemo = TryGet(() => GameCore.HotUpdate.GameKey.IsRowBlockedInDemoTwo(demoMode));
        var blockedEndless = TryGet(() => GameCore.HotUpdate.GameKey.IsRowBlockedInEndless(endlessMode));
        var rowInTable = row != null;
        return "GameMode=" + gameMode + " IsLocked=" + (locked == true ? "True" : "False")
            + " IsDemoTwo=" + (demoTwo == true ? "True" : "False")
            + " IsEndlessSave=" + (endless == true ? "True" : "False")
            + "｜探针 " + probeId + "：注册前在快照=" + (probeBefore ? "True" : "False")
            + " 行在表=" + rowInTable + " DemoTwoMode=" + demoMode + " 拦截=" + (blockedDemo == true)
            + " EndlessMode=" + endlessMode + " 拦截=" + (blockedEndless == true)
            + " ⇒ 缺席原因=" + RegistrarPlan.AbsenceReason(rowInTable, blockedDemo == true, blockedEndless == true);
    }

    /// <summary>best-effort 取值（interop 病态读不炸主流程）：异常/缺省 → fallback。</summary>
    private static T TryGet<T>(Func<T> get, T fallback = default)
    {
        try { var v = get(); return v == null ? fallback : v; }
        catch { return fallback; }
    }
}
