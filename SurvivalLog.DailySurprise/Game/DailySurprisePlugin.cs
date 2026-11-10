using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace SurvivalLog.DailySurprise;

/// <summary>喜从天降：每个游戏日睡醒触发一个随机天降事件（好运/平淡/霉运）。Task 1 骨架=自证
/// trace + WelcomeTip；事件链路（表注入/掷骰/发放）按计划逐任务落地，机制见 spec
/// docs/superpowers/specs/2026-11-10-daily-surprise-design.md 与 DESIGN.md。</summary>
[BepInPlugin(Ds.Guid, Ds.Name, Ds.Version)]
public sealed class DailySurprisePlugin : BasePlugin
{
    internal static new ManualLogSource Log; // new：显式遮蔽 BasePlugin.Log 实例属性（沿用 EndlessGuard 命名）

    public override void Load()
    {
        Log = base.Log;
        // 自证文件 trace\_DONE.txt（内容=GUID+时间戳，硬规则9：不依赖 LogOutput.log）
        DsTrace.Write("_DONE", Ds.Guid);
        Log.LogInfo("[DailySurprise] " + Ds.Name + " v" + Ds.Version + " loaded（骨架：自证+WelcomeTip）");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
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
