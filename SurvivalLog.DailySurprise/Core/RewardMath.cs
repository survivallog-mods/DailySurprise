using System;
using System.Collections.Generic;

namespace SurvivalLog.DailySurprise;

/// <summary>奖励/惩罚数学纯函数（Task 8 spec §5.5 倍率轴 + §5.3 变体映射 + §5.2 挂账；
/// Task 9 加损失轴 ScaleLoss 与 D1 分流 PlanPermAttrLoss）。
/// Game 层 RewardExecutor/PunishmentOps 与 Game/Trigger 补发链共同消费；零 BepInEx/il2cpp 引用（Tests 源链接钉死）。
/// 变体 Id 权威=<see cref="BuffBlueprints.FortuneIds"/>[1..5]=99285~99289（命运眷顾Ⅰ~Ⅴ）、
/// <see cref="BuffBlueprints.SlumpIds"/>[2..5]=99290~99293（行运低潮Ⅱ~Ⅴ）。</summary>
public static class RewardMath
{
    // ———————————————— 数量缩放 ————————————————

    /// <summary>奖励数量缩放（spec §5.5）：round(base×mult) 四舍五入（AwayFromZero，中途值 2.5→3）、
    /// 下限 1（×0.1 档也至少发 1 件——"喜从天降"不发 0 件）。mult≤0 防御性同落 1。</summary>
    public static int ScaleCount(int baseCount, double mult)
    {
        if (baseCount <= 0) return 0;   // 基准 0/负=数据错误，不发明（编码层 RewardEncoding 同口径跳过）
        var scaled = Math.Round(baseCount * mult, MidpointRounding.AwayFromZero);
        return Math.Max(1, (int)scaled);
    }

    // ———————————————— 损失量缩放（Task 9 惩罚侧） ————————————————

    /// <summary>损失量缩放（spec §5.5 损失轴；Task 9 产出接口逐字）：baseValue×lossMult
    /// 四舍五入到 0.1（<see cref="MidpointRounding.AwayFromZero"/>：26.25→26.3，非银行家舍入）。
    /// PunishmentOps 消费（耐久百分比 pct 的缩放）；baseValue/lossMult≤0 防御性归 0
    ///（负损失=反向增益，安全向 0；倍率 0 不会出现在 Resolve 产出，补发档缺省 0 同归 0）。</summary>
    public static double ScaleLoss(double baseValue, double lossMult)
    {
        if (baseValue <= 0 || lossMult <= 0) return 0;
        return Math.Round(baseValue * lossMult, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>D1 代价的属性池（AttrName 上限系五维，dump.cs:57466）：
    /// MaxSatiety=101 / MaxMorale=102 / MaxStamina=103 / MaxHealth=104 / MaxVitality=105。</summary>
    public static readonly int[] PermAttrPool = { 101, 102, 103, 104, 105 };

    /// <summary>PlanPermAttrLoss 的计划形态：UseAttrLoss=true → 扣 AttrId by Loss（负数，
    /// 强化层 AddStrengtheningValue 永久生效）；false → 施加 SlumpBuffId 行运低潮变体
    ///（spec §8.9 D1 备用路径）。未携带字段恒 0（调用方不得消费）。</summary>
    public sealed class PermAttrPlan
    {
        public bool UseAttrLoss;
        public int AttrId;
        public int Loss;
        public int SlumpBuffId;
    }

    /// <summary>D1 代价分流计划（纯函数，Task 9）：<paramref name="allowPermanentNegative"/>=false
    /// → 不扣属性，替补行运低潮（<see cref="PickSlump"/> 按损失倍率选 Ⅱ~Ⅴ；rand 不被消费）；
    /// true → 池内种子化随机一项属性，扣量 = −round(10×lossMult)（AwayFromZero，17.5→18，至少 −1）。
    /// 自然触发链的关档替补由 LuckEngine（PermNegativeCap=0→D2）先行兜住；本分流兜掷骰后、
    /// 确认前开关被关掉的路径。</summary>
    public static PermAttrPlan PlanPermAttrLoss(bool allowPermanentNegative, double lossMult, Random rand)
    {
        if (!allowPermanentNegative)
            return new PermAttrPlan { UseAttrLoss = false, SlumpBuffId = PickSlump(lossMult) };

        var attrId = PermAttrPool[rand != null ? rand.Next(PermAttrPool.Length) : 0];
        var loss = -(int)Math.Round(10 * Math.Max(0, lossMult), MidpointRounding.AwayFromZero);
        return new PermAttrPlan { UseAttrLoss = true, AttrId = attrId, Loss = Math.Min(loss, -1) };
    }

    // ———————————————— 背包拒收挂账 ————————————————

    /// <summary>拒收判定（spec §5.2 延迟发放）：bagAccepted=false → 挂账 PendingReward；
    /// true → null（不挂账）。
    /// **入队 spec=已缩放形状**（2026-11-10 定案）：调用方（RewardExecutor）先按倍率缩放 Items 再传入，
    /// 本函数只透传不定改——补发（进档后 PendingFlusher）按 RewardMult=1.0 原样再 Grant，不二次缩放。</summary>
    public static PendingReward DecideQueue(bool bagAccepted, RewardSpec spec, string reason)
    {
        if (bagAccepted) return null;
        return new PendingReward { Spec = spec, Reason = reason };
    }

    // ———————————————— 变体选择 ————————————————

    /// <summary>命运眷顾变体（spec §5.3）：预设档 1~5 → FortuneIds[同号]（不看倍率）；
    /// 仅自定义档 tier==6 按奖励倍率映射 ≥1.75→Ⅰ / ≥1.25→Ⅱ / ≥0.875→Ⅲ / ≥0.625→Ⅳ / 否则Ⅴ。
    /// tier 出界（DifficultyTable.Resolve 不会产出）→ 抛错 fail fast（调用方 try/catch 兜底）。</summary>
    public static int PickPermBuff(int tier, double rewardMult)
    {
        if (tier is >= 1 and <= 5) return BuffBlueprints.FortuneIds[tier];
        if (tier == 6)
        {
            if (rewardMult >= 1.75) return BuffBlueprints.FortuneIds[1];
            if (rewardMult >= 1.25) return BuffBlueprints.FortuneIds[2];
            if (rewardMult >= 0.875) return BuffBlueprints.FortuneIds[3];
            if (rewardMult >= 0.625) return BuffBlueprints.FortuneIds[4];
            return BuffBlueprints.FortuneIds[5];
        }
        throw new ArgumentOutOfRangeException(nameof(tier), tier, "难度档应为 1~6（Resolve 回退后不会出界）");
    }

    /// <summary>行运低潮变体（spec §5.3/§6.3，仅自定义档 D1 替代路径用，Task 9 消费）：
    /// 按损失倍率 ≥1.75→SlumpIds[5] / ≥1.25→[4] / ≥0.75→[3] / 否则[2]（无Ⅰ变体）。</summary>
    public static int PickSlump(double lossMult)
    {
        if (lossMult >= 1.75) return BuffBlueprints.SlumpIds[5];
        if (lossMult >= 1.25) return BuffBlueprints.SlumpIds[4];
        if (lossMult >= 0.75) return BuffBlueprints.SlumpIds[3];
        return BuffBlueprints.SlumpIds[2];
    }

    // ———————————————— 随机择一 ————————————————

    /// <summary>RandomPickOne 候选池均匀随机取 1 条（spec §8 R2/E1/E3/E4/L2「随机×1」）：
    /// 只消费 rand.Next(count) 一次（种子化可测）；池空/null → null（调用方按无物品处理）。
    /// 返回池内原引用（Count=1 语义的候选条目），缩放归调用方（选中后再 ScaleCount）。</summary>
    public static ItemGain PickRandomOne(IReadOnlyList<ItemGain> pool, Random rand)
    {
        if (pool == null || pool.Count == 0 || rand == null) return null;
        return pool[rand.Next(pool.Count)];
    }

    // ———————————————— 补发后合并 ————————————————

    /// <summary>补发落盘合并（fix round 1：ChannelReady 等待窗口内的丢更新防护）：
    /// 以**磁盘重载的最新 state** 为准，只把 <see cref="RollState.PendingRewards"/> 替换为补发结果
    /// （remaining=仍拒收的保留队列，null 归一空列表），其余字段（每日记账族）原样保留——
    /// 窗口期内 DailyFlow 写入的新记账与新挂账不被陈旧快照回滚。
    /// 纯函数：返回新 RollState，不改写入参（remaining 列表按引用收编）。
    /// Game 层 PendingFlusher 消费；配套职责：发放列表=重载后的最新列表（窗口期新增条目同轮发放）。</summary>
    public static RollState ApplyFlushResult(RollState latestFromDisk, List<PendingReward> remaining)
    {
        if (latestFromDisk == null) throw new ArgumentNullException(nameof(latestFromDisk));
        return new RollState
        {
            LastTriggeredDay = latestFromDisk.LastTriggeredDay,
            BadStreak = latestFromDisk.BadStreak,
            PermNegativeUsed = latestFromDisk.PermNegativeUsed,
            YesterdayEventId = latestFromDisk.YesterdayEventId,
            DisasterCount = latestFromDisk.DisasterCount,
            PendingRewards = remaining ?? new List<PendingReward>(),
        };
    }
}
