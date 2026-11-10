using System;
using System.Collections.Generic;

namespace SurvivalLog.DailySurprise;

/// <summary>PendingRewards 补发链（Task 8；spec §5.2 延迟发放路径）：
/// 进档后（ticker 就绪时）**先于每日触发**补发挂账队列——逐条 RewardExecutor.Grant
/// （**不再×倍率**：入队时已存"已缩放 spec"（2026-11-10 定案），故补发按 RewardMult=1.0 原样发，
/// 该决定同时记 RewardScaleTests.DecideQueue_StoresScaledSpecAsIs_NoRescalingOnFlush）；
/// 补发成功出队，仍拒收保留（下次进档再试）；队列清空且有实际补发 → 主角头顶气泡
/// 「昨天塞不下的东西送到了」。
/// 落盘=先重载磁盘再 ApplyFlushResult 合并（fix round 1 防丢更新：等待窗口内其他写入者的
/// 新记账/新挂账不被陈旧快照回滚，窗口期新增条目同轮发放——纯函数+测试钉死）。
/// 就绪判定=物品通道三要素（ItemManager / 背包 owner / 主角）可达——任一缺=本轮放弃下轮再试
/// （进档空窗正常态，WelcomeTicker 同款耐心等待；每档只试一轮，防每 2s 重写存档刷盘）。
/// 由 DsTicker 每 2s 调 Step（置于 DayWatcher 之前——兜底日翻触发的同 tick 内补发先于掷骰；
/// 结算 hook 路径的每日触发发生在睡醒结算，天然晚于进档补发）。</summary>
internal static class PendingFlusher
{
    /// <summary>补发完成的气泡文案（spec §5.2；时长 3 秒=WelcomeTip/ShowHeadTalk 实测定稿）。</summary>
    internal const string DoneBubbleText = "昨天塞不下的东西送到了";

    private static bool _worldReady;   // BattleLogicWorld 沿（false→true=进档）
    private static bool _flushed;      // 本档已试过一轮（仍拒收的留到下次进档）
    private static RollState _state;   // 发现货点快照（仅"有无挂账"预检；发放以 FlushOneRound 重载的磁盘为准——fix round 1）
    private static string _slot;
    private static int _waits;         // 通道等待计数（超限放弃本轮，防每 2s 读盘）

    internal static void Step()
    {
        try
        {
            bool ready;
            try
            {
                ready = GameCore.HotUpdate.BaseSingleton
                    <GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.IsInstanceCreated;
            }
            catch { return; }

            if (!ready)
            {
                // 回主菜单/退档：重新武装；工作状态一并作废（防跨档残留旧 state 写进新档）
                _worldReady = false;
                _state = null;
                _slot = null;
                return;
            }

            if (!_worldReady)
            {
                _worldReady = true;
                _flushed = false;                          // 进档沿：本轮起尝试补发
                _waits = 0;
            }
            if (_flushed) return;

            if (_state == null)
            {
                // 存档名进档空窗不可达属正常：等下轮（不置 _flushed）
                var slotDir = DailyFlow.SlotDir();
                if (slotDir == null) return;
                _state = StateStore.Load(slotDir, out _);
                _slot = slotDir;
                if (_state.PendingRewards.Count == 0) { Finish(); return; }   // 无挂账：静默收工
            }
            if (!ChannelReady())
            {
                // 通道未就绪：下轮再试；60s（30×2s）仍不就绪=本轮放弃（挂账留到下次进档）
                if (++_waits < 30) return;
                DsTrace.Write("grants", "补发等待通道超时，本轮放弃（挂账保留）");
                Finish();
                return;
            }

            FlushOneRound();
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("grants", "PendingFlusher: " + e.Message);
            Finish();   // 异常也收工（防 2s 刷屏重试；挂账仍在存档里，下次进档再试）
        }
    }

    /// <summary>物品通道三要素：ItemManager / 背包 owner / 主角（与 RewardExecutor.Grant 内部同源）。</summary>
    private static bool ChannelReady()
    {
        try
        {
            var world = EligibilityChecker.World();
            if (world?._ItemManager == null) return false;
            if (EligibilityChecker.PlayerBagOwnerId() <= 0) return false;
            if (world._AgentManager?.GetLeadingRole() == null) return false;
            return true;
        }
        catch { return false; }
    }

    /// <summary>补发一轮（fix round 1：**先重载再发放**——发现货点的 _state 快照可能落后磁盘，
    /// ChannelReady 等待窗口（最长 ~60s）内 DailyFlow/SpikeForce 可能已写入新记账/新挂账）：
    /// 发放列表=**重载后的最新队列**（窗口期新增条目同轮发放，不丢）；逐条 Grant
    /// （RewardMult=1.0——spec 已缩放，不二次缩放），成功出队、拒收保留（Reason 用常量
    /// "补发重试"——首挂账 Reason 已含 day/event 上下文，续链防无限增长）；落盘走
    /// RewardMath.ApplyFlushResult（磁盘字段赢，只换 PendingRewards——纯函数+测试钉死）；
    /// 队列清空且有实际补发 → 气泡。重载→Save 全程同线程同步块，无并发写入窗口。</summary>
    private static void FlushOneRound()
    {
        var p = new DifficultyParams { Tier = DsConfig.Tier, RewardMult = 1.0 };   // 补发不缩放（定案）
        var latest = StateStore.Load(_slot, out var hadCorruption);
        if (hadCorruption)
            DsTrace.Write("grants", "补发重载：state.json 损坏已重置（按 Store 语义以默认值合并）");

        var remaining = new List<PendingReward>();
        var grantedAny = false;
        foreach (var pending in latest.PendingRewards)
        {
            var res = RewardExecutor.Grant(pending?.Spec, p, new Random(), "补发重试");
            grantedAny |= res.GrantedItems > 0;
            remaining.AddRange(res.Queued);
        }

        var hadPending = latest.PendingRewards.Count;
        StateStore.Save(RewardMath.ApplyFlushResult(latest, remaining), _slot);
        DsTrace.Write("grants", "补发一轮：挂账" + hadPending + " 条 → 补发" + (grantedAny ? "有" : "无")
            + "、剩余 " + remaining.Count + " 条");

        if (grantedAny && remaining.Count == 0)
        {
            // 队列清空才播（spec：补发全部完成后）；trace 文案与实际显示结果对齐（fix Minor#5）
            var shown = DailyFlow.ShowHeadTalk(DoneBubbleText);
            DsTrace.Write("grants", shown
                ? "补发完成：气泡「" + DoneBubbleText + "」"
                : "补发完成：气泡未发（模型未就绪）");
        }

        Finish();
    }

    private static void Finish()
    {
        _state = null;
        _slot = null;
        _flushed = true;
    }
}
