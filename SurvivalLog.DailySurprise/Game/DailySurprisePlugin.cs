using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.DailySurprise;

/// <summary>喜从天降：每个游戏日睡醒触发一个随机天降事件（好运/平淡/霉运）。Task 7=每日触发链
/// （CaptureDaySnapshot 补丁+灾后门控+同日幂等+DayWatcher 兜底）；机制见 spec
/// docs/superpowers/specs/2026-11-10-daily-surprise-design.md 与 DESIGN.md。</summary>
[BepInPlugin(Ds.Guid, Ds.Name, Ds.Version)]
public sealed class DailySurprisePlugin : BasePlugin
{
    internal static new ManualLogSource Log; // new：显式遮蔽 BasePlugin.Log 实例属性（沿用 EndlessGuard 命名）

    public override void Load()
    {
        Log = base.Log;
        DsConfig.Init(Config);   // cfg 全 12 项（General.Enabled 总开关 + spec §7.4 正式 8 项 + TriggerHour + 调试 DebugForceEventId/DebugCardType）集中绑定，热更=实时取值
        TempTest.Init(Config);   // 临时测试开关（DebugRandomDailyHour/DebugHourlyPopup，验完随 TempTestSwitches.cs 删除）

        // 自证文件 trace\_DONE.txt（内容=GUID+时间戳，硬规则9：不依赖 LogOutput.log）
        DsTrace.Write("_DONE", Ds.Guid);
        IconDrop.Install();   // Task 11：内嵌配图（Art/**）落盘 WebUI\Res\Icon\（幂等；先于任何表注入，结果见 trace\icons.txt）
        Log.LogInfo("[DailySurprise] " + Ds.Name + " v" + Ds.Version + " loaded（Task 7：每日触发链）");

        // 逐类隔离挂载 Harmony 补丁（SaveMgr 定案模式：PatchAll 整包挂载时单个 IL 错炸整个 Load）
        InstallPatches();

        // 2s 巡检 ticker（EndlessGuard GuardTicker 已实证注册模式：ClassInjector + 独立 GameObject
        // + DontDestroyOnLoad + AddComponent；public Update + IntPtr 双构造器——interop 注入类的引擎
        // 回调只桥接 public 生命周期方法，2026-10-25 探针实锤）。步进内容全 try/catch 隔离（异常写 trace）。
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<DsTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<DsTicker>();
        var go = new GameObject("DailySurprise.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<DsTicker>();

        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }

    /// <summary>逐类隔离挂载 Harmony 补丁（SaveMgr InstallPatches 同款；本仓 Game/Trigger/ 下
    /// 标注 [HarmonyPatch] 的类各挂一个，单个失败只禁自身并留 trace）。</summary>
    private static void InstallPatches()
    {
        var harmony = new HarmonyLib.Harmony(Ds.Guid);
        foreach (var t in typeof(DailySurprisePlugin).Assembly.GetTypes())
        {
            if (!t.IsClass || !t.IsDefined(typeof(HarmonyLib.HarmonyPatch), false)) continue;
            try { harmony.CreateClassProcessor(t).Patch(); DsTrace.Write("daily", "patch 挂载成功：" + t.Name); }
            catch (Exception e) { DsTrace.Write("daily", "patch 挂载失败 " + t.Name + ": " + e.Message); }
        }
    }
}

/// <summary>2s 轮询（EndlessGuard GuardTicker 同款形态）：SpikeForce 单步=表注入哨兵复查 +
/// （cfg 开着时）强制触发一次；DayWatcher 单步=日翻兜底（Task 7）。async 状态机 MoveNext 禁挂铁律
/// （2026-10-26 两连事故定案）与本类无关——ticker 仅轮询，Harmony 只挂普通公开方法（InstallPatches）。</summary>
internal sealed class DsTicker : MonoBehaviour
{
    private float _acc;
    public DsTicker(IntPtr p) : base(p) { }
    public DsTicker() { }
    public void Update()
    {
        _acc += UnityEngine.Time.unscaledDeltaTime;
        if (_acc < 2f) return;
        _acc = 0;
        if (!DsConfig.MasterEnabled) return;   // General.Enabled 总开关（ModCenter 启停位）：一夫当关短路全部业务链
        try { ConfigRegistrar.Step(); }   // Task 17：快照注册兜底（Configs 补注册先于任何触发/强制链；
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "ConfigRegistrar: " + e.Message); }  //     主通道=postfix StartBattle_AfterAgent
        try { SpikeForce.OnStep(); }
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "SpikeForce: " + e.Message); }
        try { TempTest.Step(); }   // 临时测试：每小时随机弹窗（DebugHourlyPopup，验完删）
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "TempTest: " + e.Message); }
        try { PendingFlusher.Step(); }   // 进档补发挂账奖励（Task 8；先于每日触发——DayWatcher 在后）
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "PendingFlusher: " + e.Message); }
        try { HourWatcher.Step(); }   // 定时小时门（Task 14：TriggerHour>0 时唯一触发源；=0 静默）
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "HourWatcher: " + e.Message); }
        try { DayWatcher.Step(); }   // 兜底日翻检测（SaveMgr 模式；TriggerHour>0 时退位只记 trace）
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "DayWatcher: " + e.Message); }
    }
}

/// <summary>强制触发器（Task 2 起常驻调试链路，Task 6 起可指向任意目录事件）：表全绿后，若
/// DebugForceEventId&gt;0 且本档未触发过
///（防重复=管理器实例身份 + cfg 值双指纹：进档/换档新建 BattleLogicWorld、或作者改 cfg 热更，
/// 任一变化即重新武装）→ TryForceTriggerEvent(id) 一次，返回值与上下文写 trace\force.txt。
/// 管理器不可达（主菜单/场景切换空窗）=本轮静默，下轮再试。</summary>
internal static class SpikeForce
{
    private static GameCore.HotUpdate.Battle.Logic.DynamicEventManager _forcedOn;
    private static int _forcedId;

    internal static void OnStep()
    {
        var report = TableInjector.EnsureInjected();   // 未就绪=下轮再试（内部自带 try/catch+trace）
        if (report == null) return;
        if (!ConfigsRegistered()) return;   // 快照就绪闸（2026-11-23 根修）：Configs 补注册完成前
                                          // TryForceTriggerEvent 查不到行恒 False——实测触发可比
                                          // registrar postfix 早 7 秒，等注册完才触发一次，杜绝盲试

        var id = DsConfig.DebugForceEventId?.Value ?? 0;
        if (id <= 0) return;

        var dem = LiveDem();
        if (dem == null) return;                       // 未进档/世界未建，下轮再试

        if (ReferenceEquals(_forcedOn, dem) && _forcedId == id) return;   // 本档已触发且 cfg 未变

        // Task 14：与每日链同源的展示回退链（C1→Heal→C2→C3→C4，逐通道诊断行写 daily.txt）——
        // 强制通道是弹窗修复的实机验证入口，单发 TryForce 在 card=False 机型上验不出修复效果
        var channel = DailyFlow.TriggerEventCard(id, DailyFlow.FindEvent(id));
        var ok = channel != CardChannel.None;
        _forcedOn = dem; _forcedId = id;
        DsTrace.Write("force", "展示回退链(" + id + ") -> " + (ok ? channel.ToString() : "全通道失败")
            + "（" + DescribeEvent(id)
            + "，在册 event/rewardSet/randomGroup/buff/effect=" + report.Events + "/" + report.RewardSets
            + "/" + report.RandomGroups + "/" + report.BuffRows + "/" + report.EffectRows
            + "。预期：直发事件弹卡——奖励清单上卡、点\"我知道了\"原生发放（StartRewardSetId 已恢复，"
            + "数量/变体按当前难度烘焙）；选项事件（L1=99224/H3=99240）弹选项卡——点选触发子事件"
            + "（99245~99249）声明式发奖；Op 系惩罚（M1/M3/M4/H2/D2/D1）由调试链 PunishmentOps 执行）");
        DailySurprisePlugin.Log?.LogMessage("[DailySurprise] force " + id + " -> " + channel);
        ForceGrant(id, channel);   // T18 fix#1：卡成功=仅 Op 系；全败=气泡兜底语义全量代码发奖
    }

    /// <summary>调试强制的发奖分流（T18 防双发 + fix#1 气泡兜底同步语义，与 DailyFlow.OnNewDay 同源）：
    /// 卡通道成功（含选项卡）→ 物品/buff/属性/永久增益由原生 RewardSet 声明式结算（直发行
    /// StartRewardSetId 已恢复），代码只跑 RunsTopLevelOp 的 Op 系（选项事件顶层短路维持——
    /// H3 零件损失在「算了」子事件 ItemCost）；全通道失败（NeedsBubbleGrant，调试链无气泡展示、
    /// 奖励照发）→ RewardExecutor 全量代码发奖+拒收挂账（数量=RewardSpec 基准×当前难度，
    /// 与注入行烘焙同公式，不二次缩放）。2026-11-23 起 TempTest（DebugForceEventKey）复用本分流
    /// ——下拉链与 ID 链执行语义完全一致（作者实测推翻"仅展示"旧认知：T19 后原生卡确认即结算奖励）。</summary>
    internal static void ForceGrant(int id, CardChannel channel)
    {
        try
        {
            var def = DailyFlow.FindEvent(id);
            if (def == null) return;
            var p = DifficultyTable.Resolve(DsConfig.Tier, DsConfig.CustomOrDefault(), DsConfig.Warn);
            if (DailyFlow.NeedsBubbleGrant(PresentationKind.EventCard, channel))
            {
                var res = RewardExecutor.Grant(def.Reward, p, new System.Random(), "force event=" + id + " bubble");
                if (res.Queued.Count > 0)
                {
                    var slotDir = DailyFlow.SlotDir();
                    if (slotDir != null)
                    {
                        var st = StateStore.Load(slotDir, out _);
                        st.PendingRewards.AddRange(res.Queued);
                        StateStore.Save(st, slotDir);
                    }
                    DsTrace.Write("force", "强制发放（气泡兜底）：拒收入队 " + res.Queued.Count + " 条"
                        + (slotDir == null ? "（存档名不可达未持久化！）" : "（已入 state.json，下次进档补发）"));
                }
            }
            else if (def.Reward is { RunsTopLevelOp: true })
            {
                var summary = PunishmentOps.Apply(def.Reward.Op, def.Reward.OpParam, p.LossMult, new System.Random());
                DsTrace.Write("force", "强制惩罚：event=" + id + " op=" + def.Reward.Op + "[" + summary + "]");
            }
        }
        catch (Exception e) { DsTrace.WriteThrottled("force", "ForceGrant: " + e.Message); }
    }

    /// <summary>事件行描述（目录命中=Key+标题+样式；未命中=原样 id——目录外 id 也允许强制，
    /// 观察原生行为用）。</summary>
    private static string DescribeEvent(int id)
    {
        foreach (var e in EventCatalog.Events)
            if (e.Id == id)
                return e.Key + "「" + e.Title + "」" + (e.Reward is { Choices.Count: > 0 } ? "选项" : "直发");
        return "非目录事件";
    }

    /// <summary>Configs 快照就绪闸：首事件行（99200）已在 dem.Configs 字典=补注册完成。
    /// 注册前触发只会全通道失败（2026-11-23 00:15 实测：触发比 registrar postfix 早 7 秒）。
    /// 任何异常=未就绪（安全向等待）。TempTest.ForceStep 同闸复用。</summary>
    internal static bool ConfigsRegistered()
    {
        try
        {
            var dem = LiveDem();
            if (dem == null) return false;
            var configs = dem.Configs;
            return configs != null && configs.ContainsKey(EventCatalog.FirstEventId);
        }
        catch { return false; }
    }

    /// <summary>DynamicEventManager 可达性：BattleLogicWorld 单例 → _DynamicEventManager
    /// （internal 属性 dump.cs:385844，interop 代理公开；BaseSingleton.IsInstanceCreated 防护
    /// ——BagTable.Cm / EndlessGuard HealModellessDevices 同款访问模式）。
    /// Task 14 起 DailyFlow 回退链同源复用（internal）。</summary>
    internal static GameCore.HotUpdate.Battle.Logic.DynamicEventManager LiveDem()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.IsInstanceCreated)
                return null;
            return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.Instance?._DynamicEventManager;
        }
        catch { return null; }
    }
}

/// <summary>自证 trace：写 plugins-data\DailySurprise\trace\{category}.txt，UTF-8（无 BOM）追加，
/// 每行带时间戳前缀。异常一律吞掉（EndlessGuard Trace 惯例：自证写入绝不打扰游戏）。
/// BaseDir 可注入（默认 null → 运行时经 BepInEx.Paths 解析根路径）以便单测；
/// 本任务 SmokeTest 只验 Ds 常量，不测文件写入。</summary>
internal static class DsTrace
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>trace 基目录；null=运行时解析 BepInEx.Paths.BepInExRootPath 下
    /// plugins-data\DailySurprise\trace（单测注入临时目录后即可离线验证写入）。</summary>
    internal static string BaseDir { get; set; }

    internal static void Write(string category, string message)
    {
        try
        {
            var dir = BaseDir ?? Path.Combine(Paths.BepInExRootPath, "plugins-data", "DailySurprise", "trace");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, category + ".txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n", Utf8NoBom);
        }
        catch { /* 自证失败绝不打扰游戏 */ }
    }

    /// <summary>节流写（Task 6 顺手修④）：同 category+message 每会话首见即写，其后每 50 次再写一次
    /// （附计数）——ticker 级重复异常（2s 一条同文案）不再刷屏。与 Write 同为尽力而为、绝不打扰游戏。
    /// 仅 ticker 单线程调用，无需加锁。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, int> ThrottleCounts = new();

    internal static void WriteThrottled(string category, string message)
    {
        try
        {
            var key = category + "|" + message;
            ThrottleCounts.TryGetValue(key, out var n);
            n++;
            ThrottleCounts[key] = n;
            if (n == 1 || n % 50 == 0)
                Write(category, n == 1 ? message : message + "（同因已累计 " + n + " 次，每 50 次记一次）");
        }
        catch { }
    }
}
