using System;
using System.Reflection;
using HarmonyLib;

namespace SurvivalLog.DailySurprise;

/// <summary>Op 惩罚确认结算（A1 修复：卡显示的 Op 系惩罚与奖励同在点"我知道了"后生效，不再弹卡同帧执行）。
/// 挂点=DynamicEventManager.OnEndResult(int eventConfigId)（dump.cs:420602，RVA 0x26DC9D0，0x110 字节普通
/// 实例方法，interop 代理公开）。gamekb 2026-11-19 build 反汇编取证：事件卡前端 Reducer_Web_EventChoice
/// .RA_Confirm 点确认时发通知 26002（DynamicEvent_EndResult），本方法是其处理者——向事件 FSM 投 EndResult(115)，
/// FSM 离开 DoResult 态时原生才 ExecuteRewardSet(RewardSetId)；官方代码侧结算同款监听（LoanSharkManager
/// .OnDynamicEventEndResult 等注册同一通知码）。C4 直拉 UI 无 FSM 实例时原生查不到事件 LogError 早退——
/// postfix 不受其早退影响照常投递。Postfix 只投递 DailyFlow.OnResultConfirmed（目录外/非 Op 事件零副作用），
/// 全程 try/catch 隔离。挂载失败=Installed 保持 false，每日链回退旧的弹卡同帧执行（防惩罚静默丢失）。</summary>
[HarmonyPatch]
internal static class Patch_OpOnConfirm
{
    /// <summary>补丁是否已挂载（InstallPatches 成功后置位；false=调用方回退弹卡同帧执行）。</summary>
    internal static bool Installed { get; private set; }

    internal static void MarkInstalled() => Installed = true;

    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(GameCore.HotUpdate.Battle.Logic.DynamicEventManager), "OnEndResult",
            new[] { typeof(int) });

    private static void Postfix(int eventConfigId)
    {
        try { DailyFlow.OnResultConfirmed(eventConfigId); }
        catch (Exception e) { DsTrace.WriteThrottled("daily", "confirm: " + e.Message); }
    }
}
