using System;
using System.Reflection;
using HarmonyLib;

namespace SurvivalLog.DailySurprise;

/// <summary>结算时机锚点（SaveMgr Patch_CaptureDaySnapshot 定案模式）：
/// postfix GameSaveManager.CaptureDaySnapshot(int day)（public，dump.cs:88810；自然日翻转全模式触发）。
/// TargetMethod 参数类型钉死（防重载漂移）；Postfix 只投递 DailyFlow.OnNewDay，全程 try/catch 隔离
/// ——本 mod 任何异常绝不影响原结算。同日幂等（DailyFlow.ShouldTrigger）天然吸收 hook 与 DayWatcher
/// 的双触发。红线：不挂 async 状态机 MoveNext（2026-10-26 定案），本补丁目标为普通公开实例方法。</summary>
[HarmonyPatch]
internal static class Patch_CaptureDaySnapshot
{
    /// <summary>hook 最近触发时刻（DayWatcher 60s 让路窗口用）。</summary>
    internal static DateTime LastFire = DateTime.MinValue;

    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(GameCore.HotUpdate.GameSaveManager), "CaptureDaySnapshot",
            new[] { typeof(int) });

    private static void Postfix(int day)
    {
        try
        {
            LastFire = DateTime.Now;
            DailyFlow.OnNewDay(day);
        }
        catch (Exception e) { DsTrace.WriteThrottled("daily", "hook: " + e.Message); }
    }
}
