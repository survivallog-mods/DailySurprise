using System;

namespace SurvivalLog.DailySurprise;

/// <summary>定时小时门轮询（Task 14，作者要求：每天固定凌晨 1 点触发）。TriggerHour&gt;0 时为
/// 本 mod 的**唯一**每日触发源——结算 hook（Patch_CaptureDaySnapshot postfix）与 DayWatcher 兜底
/// 均退位为只记 trace，防双通道；TriggerHour=0 时本类静默（旧行为=结算即触发）。
/// 节拍：DsTicker 每 2s 调 Step。当前游戏日/时读法（gamekb 2026-11-12_26615062 dump.cs）：
/// BattleLogicWorld._GameTimeManager（internal，dump.cs:390437，interop 代理公开）→
/// GameTimeManager.GetDay()（public，dump.cs:426273）/ GetHour24()（public，dump.cs:426309，0~23 口径）。
/// 判定=DailyFlow.HourGateReached（纯函数，Tests 钉死）：day 越过已触发日且 hour&gt;=TriggerHour。
/// 重试节流：OnNewDay 返回 false（瞬态放弃：存档名不可达/四表未就绪）→ 不记 _firedDay，下轮重试；
/// 定局（含同日已掷/灾前静默）→ 记 _firedDay 至当天不再投递。换档/重进档由 BattleLogicWorld 实例
/// 身份变化感知（SpikeForce 双指纹同款思路），重置 _firedDay 防跨档误判同日。</summary>
internal static class HourWatcher
{
    private static GameCore.HotUpdate.Battle.Logic.BattleLogicWorld _armedOn;
    private static int _firedDay = -1;

    internal static void Step()
    {
        try
        {
            var world = EligibilityChecker.World();
            if (world == null) return;      // 未进档/世界未建：静默（hook/DayWatcher 同款空窗语义）
            if (!ReferenceEquals(_armedOn, world))
            {
                // 新会话（进档/换档/世界重建）：重置已触发日记忆——不同档的 day 数值互不相关
                _armedOn = world;
                _firedDay = -1;
            }

            var gtm = world._GameTimeManager;
            if (gtm == null) return;
            int day, hour;
            try { day = gtm.GetDay(); hour = gtm.GetHour24(); }
            catch (Exception e)
            {
                DsTrace.WriteThrottled("ticker", "HourWatcher 读时间异常: " + e.Message);
                return;
            }
            if (day <= 0) return;

            // 临时测试开关（验完删）：DebugRandomDailyHour=true 时当天触发小时随机（1~23），
            // 覆盖 TriggerHour；其余判定（HourGateReached/重试节流）原样复用
            var triggerHour = TempTest.RandomDailyHourEnabled ? TempTest.RandomHourFor(day) : DsConfig.TriggerHourValue;
            if (triggerHour <= 0) return;   // 0=旧行为：结算 hook 即时触发，小时门不参与

            if (!DailyFlow.HourGateReached(day, hour, triggerHour, _firedDay)) return;
            if (DailyFlow.OnNewDay(day)) _firedDay = day;
        }
        catch (Exception e) { DsTrace.WriteThrottled("ticker", "HourWatcher: " + e.Message); }
    }
}
