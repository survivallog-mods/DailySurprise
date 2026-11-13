using System;
using System.Collections.Generic;

namespace SurvivalLog.DailySurprise;

/// <summary>注册决策纯函数（Task 17 根因修复）：把本 mod 的 50 行（45 父 + 5 子事件，Id 99200~99249）
/// 注册进 DynamicEventManager.Configs 私有快照的 Id 清单/去重决策/DynamicEventConfig 字段映射。
/// 权威依据=gamekb 2026-11-12_26615062 反汇编（IL2CPP 写入层 Game/Trigger/ConfigRegistrar.cs 头注
/// 有完整取证）：原生快照由 StartBattle_AfterAgent 构建循环逐行装配 DynamicEventConfig——本类
/// MapConfig 逐字段镜像该循环，保证注册行与"原生循环未被行过滤拦下时产出的行"逐字段一致。</summary>
public static class RegistrarPlan
{
    /// <summary>待注册 Id 清单（父事件 99200~99244 在前、子事件 99245~99249 在后，与 TableInjector
    /// 的 BuildEventIds 同源同序；动态取自目录而非硬编码）。</summary>
    public static IReadOnlyList<int> EventIds()
    {
        var ids = new List<int>(EventCatalog.Events.Count + EventCatalog.SonEvents.Count);
        foreach (var e in EventCatalog.Events) ids.Add(e.Id);
        foreach (var s in EventCatalog.SonEvents) ids.Add(s.Id);
        return ids;
    }

    /// <summary>去重/缺行决策结果（三项互斥且覆盖传入全集）。</summary>
    public sealed class Decision
    {
        /// <summary>Configs 缺且表行在册 → 待注册。</summary>
        public List<int> ToAdd = new();

        /// <summary>Configs 已含（Initialize 后反复调用的幂等跳过集）。</summary>
        public List<int> AlreadyPresent = new();

        /// <summary>表行缺失（时序：注入未完成）→ 本轮不注册，下轮（ticker/postfix）再试。</summary>
        public List<int> RowMissing = new();
    }

    /// <summary>去重决策：Configs 已含则跳过（幂等——Configs 在每次 StartBattle_Early 重建、
    /// 本注册在每次构建后重放，双保险下重复 Add 会抛重复键异常，决策层必须先查）。</summary>
    public static Decision Decide(IEnumerable<int> ids, Func<int, bool> inConfigs, Func<int, bool> rowInTable)
    {
        if (ids == null) throw new ArgumentNullException(nameof(ids));
        if (inConfigs == null) throw new ArgumentNullException(nameof(inConfigs));
        if (rowInTable == null) throw new ArgumentNullException(nameof(rowInTable));
        var d = new Decision();
        foreach (var id in ids)
        {
            if (inConfigs(id)) d.AlreadyPresent.Add(id);
            else if (rowInTable(id)) d.ToAdd.Add(id);
            else d.RowMissing.Add(id);
        }
        return d;
    }

    /// <summary>Config_Event 行值快照（注册时从活行对象读取；测试侧从 InjectorMapping 行字典构造）。
    /// DayRate 不属于 DynamicEventConfig 字段，仅供 <see cref="IsRollerImmune"/> 门控。</summary>
    public sealed class RowShape
    {
        public int Id;
        public int Type;
        public int TimeMin;
        public int TimeMax;
        public bool IsSon;
        public int ChoseNameCount;
        public int TaskTime;
        public int TaskConditionSetId;
        public string GroupKey;
        public int GroupDayCd;
        public float DayRate;
    }

    /// <summary>DynamicEventConfig 目标字段值（IL2CPP 写入层逐字段搬运；PlayerSelectId 为共享引用
    /// 不经本 shape，与原生循环一致）。</summary>
    public sealed class ConfigShape
    {
        public int EventId;
        public int EventType;
        public int TimeMin;
        public int TimeMax;
        public bool IsChild;
        public bool IsChose;
        public int TaskTime;
        public int TaskConditionSetId;
        public string GroupKey;
        public int GroupDayCd;
    }

    /// <summary>行值 → DynamicEventConfig 字段映射（逐字段镜像原生构建循环，反汇编 0x26E3F90 循环体）：
    /// EventId←ID、EventType←Type（枚举同编号：1=Choice/2=Result/3=Task，dump:58002）、
    /// TimeMin/TimeMax←同名、IsChild←IsSon、IsChose←ChoseName 非 null 且 Count&gt;0
    ///（注意是 ChoseName@0xE8，非 ChoseID@0xF8）、TaskTime←TaskTime（原生乘 (1+主角属性411) 任务时长
    /// 加成——本目录全部行 TaskTime=0，乘积恒 0，故直拷）、TaskConditionSetId←同名、
    /// GroupKey/GroupDayCD←同名。</summary>
    public static ConfigShape MapConfig(RowShape row)
    {
        if (row == null) throw new ArgumentNullException(nameof(row));
        return new ConfigShape
        {
            EventId = row.Id,
            EventType = row.Type,
            TimeMin = row.TimeMin,
            TimeMax = row.TimeMax,
            IsChild = row.IsSon,
            IsChose = row.ChoseNameCount > 0,
            TaskTime = row.TaskTime,
            TaskConditionSetId = row.TaskConditionSetId,
            GroupKey = row.GroupKey ?? "",
            GroupDayCd = row.GroupDayCd,
        };
    }

    /// <summary>原生随机池免疫门控：注册进 Configs 后 TriggerRandomEvent 逐时扫描全部非子事件，
    /// 唯一免疫源=ProbabilityTestCondition(DayRate)——p==0 时幸运加成被显式跳过、
    /// 0 &gt; RandomFloat(0,1) 恒 false（反汇编 0x27360E0）。DayRate≠0 的行会进原生随机池
    /// 自发弹卡（无奖励、随机小时），注册层必须拒绝。</summary>
    public static bool IsRollerImmune(float dayRate) => dayRate == 0f;

    /// <summary>缺席原因诊断（registrar.txt 诊断行的结论字段——回答"为什么表里有行快照却没有"）：</summary>
    public static string AbsenceReason(bool rowInTable, bool blockedDemoTwo, bool blockedEndless)
    {
        if (!rowInTable) return "时序：行未注入表";
        if (blockedDemoTwo && blockedEndless) return "过滤：DemoTwoMode+EndlessMode 双拦截";
        if (blockedDemoTwo) return "过滤：DemoTwoMode 拦截（原生构建循环跳行）";
        if (blockedEndless) return "过滤：EndlessMode 拦截（原生构建循环跳行）";
        return "时序：快照构建先于注入（行未被过滤但构建时不在表）";
    }
}
