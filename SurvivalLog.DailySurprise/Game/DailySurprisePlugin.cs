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
        DsConfig.Init(Config);   // cfg 全 9 项（General.Enabled 总开关 + spec §7.4 正式 7 项 + TriggerHour）集中绑定，热更=实时取值

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
            try
            {
                harmony.CreateClassProcessor(t).Patch();
                if (t == typeof(Patch_OpOnConfirm)) Patch_OpOnConfirm.MarkInstalled();
                DsTrace.Write("daily", "patch 挂载成功：" + t.Name);
            }
            catch (Exception e) { DsTrace.Write("daily", "patch 挂载失败 " + t.Name + ": " + e.Message); }
        }
    }
}

/// <summary>2s 轮询（EndlessGuard GuardTicker 同款形态）：快照注册兜底、进档补发、定时小时门、
/// 日翻兜底各一单步。async 状态机 MoveNext 禁挂铁律（2026-10-26 两连事故定案）与本类无关——
/// ticker 仅轮询，Harmony 只挂普通公开方法（InstallPatches）。</summary>
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
        try { ConfigRegistrar.Step(); }   // Task 17：快照注册兜底（Configs 补注册先于每日触发链；
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "ConfigRegistrar: " + e.Message); }  //     主通道=postfix StartBattle_AfterAgent
        try { PendingFlusher.Step(); }   // 进档补发挂账奖励（Task 8；先于每日触发——DayWatcher 在后）
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "PendingFlusher: " + e.Message); }
        try { HourWatcher.Step(); }   // 定时小时门（Task 14：TriggerHour>0 时唯一触发源；=0 静默）
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "HourWatcher: " + e.Message); }
        try { DayWatcher.Step(); }   // 兜底日翻检测（SaveMgr 模式；TriggerHour>0 时退位只记 trace）
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "DayWatcher: " + e.Message); }
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
