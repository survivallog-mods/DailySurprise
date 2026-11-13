using System;

namespace SurvivalLog.DailySurprise;

/// <summary>存档只读访问薄层（SaveMgr SaveEnv 同款防御，按本 mod 需要裁剪）：
/// GetLastPlayFileName/GetHistoryList 在局内可能 NRE（战时 historyData 卸载，SaveMgr 2026-10-31 实机
/// 实证，游戏源 GameSaveManager.cs:421）——菜单期成功值缓存，局内回落缓存（进档必经菜单，缓存必已
/// 填充）。EnsureHistoryV2Migrated 懒守卫照抄原生 RebuildSaveList 前置（幂等）。</summary>
internal static class SaveRef
{
    private static bool _historyEnsured;
    private static string _cachedName;

    internal static GameCore.HotUpdate.GameSaveManager Gsm
        => GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.GameSaveManager>.Instance;

    private static void EnsureHistoryReady()
    {
        if (_historyEnsured) return;
        try { Gsm?.EnsureHistoryV2Migrated(); _historyEnsured = true; }
        catch (Exception e) { DsTrace.WriteThrottled("daily", "EnsureHistoryReady: " + e.Message); }
    }

    /// <summary>最近游玩档名（游戏原生约定带 .bytes 后缀）；不可达=缓存值（可能仍 null）。</summary>
    internal static string CurrentSaveName()
    {
        try
        {
            var gsm = Gsm;
            if (gsm == null) return _cachedName;
            EnsureHistoryReady();
            var name = gsm.GetLastPlayFileName();
            if (!string.IsNullOrEmpty(name)) _cachedName = name;
            return string.IsNullOrEmpty(name) ? _cachedName : name;
        }
        catch { return _cachedName; }
    }

    /// <summary>当前档基名（去 .bytes；作状态槽目录名）；无=null。</summary>
    internal static string CurrentSaveBase()
    {
        var n = CurrentSaveName();
        if (string.IsNullOrEmpty(n)) return null;
        return n.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase) ? n[..^".bytes".Length] : n;
    }

    /// <summary>该档历史 meta 的 Day（结算时 SaveGameFileCore 同步历史 meta——SaveMgr 实证）；
    /// 无匹配/异常=-1。</summary>
    internal static int HistoryDay(string saveName)
    {
        try
        {
            var gsm = Gsm;
            if (gsm == null || string.IsNullOrEmpty(saveName)) return -1;
            EnsureHistoryReady();
            var list = gsm.GetHistoryList();
            if (list == null) return -1;
            var baseName = saveName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase)
                ? saveName[..^".bytes".Length] : saveName;
            foreach (var child in list)
            {
                if (child == null || string.IsNullOrEmpty(child.FileName)) continue;
                var f = child.FileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase)
                    ? child.FileName[..^".bytes".Length] : child.FileName;
                if (f == baseName) return child.Day;
            }
            return -1;
        }
        catch (Exception e) { DsTrace.WriteThrottled("daily", "HistoryDay: " + e.Message); return -1; }
    }
}

/// <summary>兜底日翻检测（SaveMgr DayWatcher 定案模式）：轮询存档 meta 日增量；hook 60s 内触发过
/// 则让路（Patch_CaptureDaySnapshot.LastFire），OnNewDay 自身同日幂等双保险。逐类异常隔离。
/// 由 DsTicker 每 2s 调 Step（主线程节拍，与补丁同线程族——DayWatcher 走 ticker 主线程直调）。</summary>
internal static class DayWatcher
{
    private static string _watchName;
    private static int _watchDay = -1;

    internal static void Step()
    {
        try
        {
            var name = SaveRef.CurrentSaveName();
            if (name == null) { _watchName = null; _watchDay = -1; return; }
            var day = SaveRef.HistoryDay(name);
            if (name != _watchName)
            {
                // 换档/首次观测：只建立基线不触发（进档首日不补掷——结算 hook 负责当天）
                _watchName = name;
                _watchDay = day;
                return;
            }
            if (day <= 0 || _watchDay <= 0 || day <= _watchDay) { if (day > 0) _watchDay = day; return; }

            var advanced = day;
            _watchDay = day;
            if ((DateTime.Now - Patch_CaptureDaySnapshot.LastFire).TotalSeconds < 60)
                return;   // hook 刚处理过：让路（同日幂等兜底，不写行避免噪声）
            var triggerHour = DsConfig.TriggerHourValue;
            if (triggerHour > 0)
            {
                // Task 14 同步语义：小时门（HourWatcher）负责本日触发，存档 meta 兜底通道同样退位——
                // 只留一行节流 trace，防止结算后小时未到就提前触发
                DsTrace.WriteThrottled("daily", "watcher: 检测日翻 第" + advanced + "天（TriggerHour="
                    + triggerHour + ">0，小时门接管本日触发）");
                return;
            }
            DsTrace.Write("daily", "watcher: 检测日翻 第" + advanced + "天（hook 未达，兜底触发）");
            DailyFlow.OnNewDay(advanced);
        }
        catch (Exception e) { DsTrace.WriteThrottled("daily", "watcher: " + e.Message); }
    }
}
