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
            var triggerHour = DsConfig.TriggerHourValue;
            if (triggerHour > 0)
            {
                // Task 14：TriggerHour>0 时结算通道退位——只留一行节流 trace 不触发（防双通道），
                // 本日触发改由 HourWatcher 小时门在 hour>=TriggerHour 时投递
                DsTrace.WriteThrottled("daily", "hook: day=" + day + " 结算（TriggerHour=" + triggerHour
                    + ">0，小时门接管本日触发）");
                return;
            }
            DailyFlow.OnNewDay(day);   // TriggerHour=0：旧行为（结算即触发）
        }
        catch (Exception e) { DsTrace.WriteThrottled("daily", "hook: " + e.Message); }
    }
}
