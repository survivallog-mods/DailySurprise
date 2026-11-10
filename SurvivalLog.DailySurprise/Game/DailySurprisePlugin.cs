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

/// <summary>喜从天降：每个游戏日睡醒触发一个随机天降事件（好运/平淡/霉运）。Task 2=首验 spike：
/// 表注入（TableInjector）+ DebugForceEventId 强制触发链路实证；掷骰/发放按计划逐任务落地，
/// 机制见 spec docs/superpowers/specs/2026-11-10-daily-surprise-design.md 与 DESIGN.md。</summary>
[BepInPlugin(Ds.Guid, Ds.Name, Ds.Version)]
public sealed class DailySurprisePlugin : BasePlugin
{
    internal static new ManualLogSource Log; // new：显式遮蔽 BasePlugin.Log 实例属性（沿用 EndlessGuard 命名）

    /// <summary>首验调试（Task 2）：>0=进档且表就绪后对注入事件行强制触发一次（写 trace\force.txt）。
    /// 用**事件行** ID（99200）；99250 是 RewardSet ID，填它无效（brief Step 3 口径）。0=关闭。</summary>
    internal static ConfigEntry<int> DebugForceEventId;

    /// <summary>首验调试（Task 2，验证B 开关，超出 brief cfg 清单的增补）：true=注入行带
    /// ChoseName_Local=[拿走,算了] 两选项（样式抄原生行 103）；false=无选项直发（样式抄原生行
    /// 10011，验证A）。改后重进档生效（行形态哨兵自动重拍）。Task 3 起 EventCatalog 接管时移除。</summary>
    internal static ConfigEntry<bool> DebugSpikeChose;

    public override void Load()
    {
        Log = base.Log;
        DebugForceEventId = Config.Bind("General", "DebugForceEventId", 0,
            "首验调试：>0=进档后对注入事件行强制触发一次（事件行 ID，如 99200；99250 是 RewardSet ID 无效）。0=关闭。每进档一次只触发一次，结果写 BepInEx\\plugins-data\\DailySurprise\\trace\\force.txt。");
        DebugSpikeChose = Config.Bind("General", "DebugSpikeChose", false,
            "首验调试（验证B）：true=注入事件行带 [拿走,算了] 两选项（选项 UI 观察）；false=无选项直发（弹窗+自动发放观察）。改后重进档生效。");

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
        catch (Exception e) { DsTrace.Write("ticker", "SpikeForce: " + e.Message); }
    }
}

/// <summary>首验强制触发器（Task 2 专用）：表全绿后，若 DebugForceEventId&gt;0 且本档未触发过
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
            + "（变体=" + (DailySurprisePlugin.DebugSpikeChose is { Value: true } ? "B选项" : "A直发")
            + "，行在册 event/rewardSet=" + report.Events + "/" + report.RewardSets
            + "。预期：弹事件卡；A 变体关窗后背包 4×「90压缩饼干」=StartRewardSet 自动发放实证）");
        DailySurprisePlugin.Log?.LogMessage("[DailySurprise] force " + id + " -> " + ok);
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
}
