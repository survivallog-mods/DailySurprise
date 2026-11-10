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

/// <summary>喜从天降：每个游戏日睡醒触发一个随机天降事件（好运/平淡/霉运）。Task 6=四表全量注入
/// （TableInjector + InjectorMapping）+ DebugForceEventId 强制触发链路；掷骰/发放按计划逐任务落地，
/// 机制见 spec docs/superpowers/specs/2026-11-10-daily-surprise-design.md 与 DESIGN.md。</summary>
[BepInPlugin(Ds.Guid, Ds.Name, Ds.Version)]
public sealed class DailySurprisePlugin : BasePlugin
{
    internal static new ManualLogSource Log; // new：显式遮蔽 BasePlugin.Log 实例属性（沿用 EndlessGuard 命名）

    /// <summary>首验调试（Task 2）：>0=进档且表就绪后对注入事件行强制触发一次（写 trace\force.txt）。
    /// 用**事件行** ID（99200~99244 任意目录事件，如 99205=G1 直发/99224=L1 选项）。0=关闭。</summary>
    internal static ConfigEntry<int> DebugForceEventId;

    public override void Load()
    {
        Log = base.Log;
        DebugForceEventId = Config.Bind("General", "DebugForceEventId", 0,
            "首验调试：>0=进档后对注入事件行强制触发一次（事件行 ID 99200~99244，如 99205=G1；99250 是 RewardSet ID 无效）。0=关闭。每进档一次只触发一次，结果写 BepInEx\\plugins-data\\DailySurprise\\trace\\force.txt。");

        // 自证文件 trace\_DONE.txt（内容=GUID+时间戳，硬规则9：不依赖 LogOutput.log）
        DsTrace.Write("_DONE", Ds.Guid);
        Log.LogInfo("[DailySurprise] " + Ds.Name + " v" + Ds.Version + " loaded（Task 2 spike：表注入+强制触发）");

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
}

/// <summary>2s 轮询（EndlessGuard GuardTicker 同款形态）：SpikeForce 单步=表注入哨兵复查 +
/// （cfg 开着时）强制触发一次。async 状态机 MoveNext 禁挂铁律（2026-10-26 两连事故定案）与本类
/// 无关——本 mod 不挂 Harmony，仅轮询。</summary>
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
        try { SpikeForce.OnStep(); }
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "SpikeForce: " + e.Message); }
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

        var id = DailySurprisePlugin.DebugForceEventId?.Value ?? 0;
        if (id <= 0) return;

        var dem = LiveDem();
        if (dem == null) return;                       // 未进档/世界未建，下轮再试

        if (ReferenceEquals(_forcedOn, dem) && _forcedId == id) return;   // 本档已触发且 cfg 未变

        bool ok;
        try { ok = dem.TryForceTriggerEvent(id, false); }
        catch (Exception e)
        {
            DsTrace.Write("force", "TryForceTriggerEvent(" + id + ") 抛异常: " + e.Message);
            _forcedOn = dem; _forcedId = id;           // 抛异常同样记已试（不刷屏），换档/改 cfg 后再试
            return;
        }
        _forcedOn = dem; _forcedId = id;
        DsTrace.Write("force", "TryForceTriggerEvent(" + id + ") -> " + ok
            + "（" + DescribeEvent(id)
            + "，在册 event/rewardSet/buff/effect=" + report.Events + "/" + report.RewardSets
            + "/" + report.BuffRows + "/" + report.EffectRows
            + "。预期：直发事件弹卡、关窗后自动发放（StartRewardSet）；选项事件（L1=99224/H3=99240）弹选项卡——选项接线 Task 10 落地前点选不生效）");
        DailySurprisePlugin.Log?.LogMessage("[DailySurprise] force " + id + " -> " + ok);
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

    /// <summary>DynamicEventManager 可达性：BattleLogicWorld 单例 → _DynamicEventManager
    /// （internal 属性 dump.cs:385844，interop 代理公开；BaseSingleton.IsInstanceCreated 防护
    /// ——BagTable.Cm / EndlessGuard HealModellessDevices 同款访问模式）。</summary>
    private static GameCore.HotUpdate.Battle.Logic.DynamicEventManager LiveDem()
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
