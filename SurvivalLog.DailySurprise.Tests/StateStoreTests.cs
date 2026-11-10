using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>StateStore 分槽存取/原子写/损坏容错规格测试（Task 5，spec §5.1 RollState 持久化）。
/// 槽目录用临时目录（ds-test-&lt;guid&gt;）互不串扰，Dispose 统一清理；
/// RoundTrip 逐字段断言覆盖 RewardSpec 全形状（Items/Buffs/Attrs/Op/OpParam/Choices 嵌套/PermBuffByTier/RandomPickOne）。
/// 序列化契约一并钉死：公开字段、忽略 null、枚举转字符串、UTF-8 无 BOM、tmp+Move 原子替换。</summary>
public sealed class StateStoreTests : IDisposable
{
    private readonly List<string> _slots = new();

    /// <summary>新建互不冲突的槽目录路径（只记路径不建目录——Load 面对不存在的目录也须返回默认值）。</summary>
    private string NewSlot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ds-test-" + Guid.NewGuid());
        _slots.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var d in _slots)
            if (Directory.Exists(d))
                Directory.Delete(d, recursive: true);
    }

    /// <summary>全默认断言：五计数器归零 + PendingRewards 空列表非 null。</summary>
    private static void AssertDefault(RollState s)
    {
        Assert.Equal(0, s.LastTriggeredDay);
        Assert.Equal(0, s.BadStreak);
        Assert.Equal(0, s.PermNegativeUsed);
        Assert.Equal(0, s.YesterdayEventId);
        Assert.Equal(0, s.DisasterCount);
        Assert.NotNull(s.PendingRewards);
        Assert.Empty(s.PendingRewards);
    }

    // —— 缺文件 / 损坏 ——

    [Fact]
    public void Load_MissingFile_ReturnsDefault()
    {
        var slot = NewSlot();   // 目录与文件都不存在

        var s = StateStore.Load(slot, out var hadCorruption);

        Assert.False(hadCorruption);   // 首启缺文件是正常态，不算损坏
        AssertDefault(s);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefault()
    {
        var slot = NewSlot();
        Directory.CreateDirectory(slot);
        File.WriteAllText(Path.Combine(slot, StateStore.FileName), "{ 这不是JSON!!! ");

        var s = StateStore.Load(slot, out var hadCorruption);

        Assert.True(hadCorruption);   // 损坏事实可检测
        AssertDefault(s);
    }

    [Fact]
    public void Load_NullLiteralJson_ReturnsDefaultWithCorruption()
    {
        var slot = NewSlot();
        Directory.CreateDirectory(slot);
        File.WriteAllText(Path.Combine(slot, StateStore.FileName), "null");   // 合法 JSON 但字面量 null

        var s = StateStore.Load(slot, out var hadCorruption);

        Assert.True(hadCorruption);   // 反序列化得 null 视同损坏，不能静默当默认
        AssertDefault(s);
    }

    // —— 往返 ——

    [Fact]
    public void SaveLoad_RoundTrip()
    {
        var slot = NewSlot();
        var state = new RollState
        {
            LastTriggeredDay = 12,
            BadStreak = 3,
            PermNegativeUsed = 1,
            YesterdayEventId = 99224,
            DisasterCount = 2,
            PendingRewards = new List<PendingReward>
            {
                new()
                {
                    Reason = "l1-choices-deferred",
                    Spec = new RewardSpec
                    {
                        Items = new List<ItemGain> { new() { ItemId = 2401, Count = 2 }, new() { ItemId = 2319, Count = 1 } },
                        Buffs = new List<int> { 90, 302810 },
                        Attrs = new List<AttrChange> { new() { AttrId = 2, Delta = 2.5 } },
                        Op = SpecialOp.StealParts,
                        OpParam = 10,
                        Choices = new List<Choice>
                        {
                            new() { Label = "追上去", Reward = new RewardSpec { Buffs = new List<int> { 1111 } } },
                            new() { Label = "算了", Reward = new RewardSpec() },
                        },
                        PermBuffByTier = true,
                        RandomPickOne = true,
                    },
                },
                new() { Reason = "", Spec = new RewardSpec() },   // 空挂账条目也往返不丢
            },
        };

        StateStore.Save(state, slot);
        var loaded = StateStore.Load(slot, out var hadCorruption);

        Assert.False(hadCorruption);
        // 五计数器逐字段
        Assert.Equal(12, loaded.LastTriggeredDay);
        Assert.Equal(3, loaded.BadStreak);
        Assert.Equal(1, loaded.PermNegativeUsed);
        Assert.Equal(99224, loaded.YesterdayEventId);
        Assert.Equal(2, loaded.DisasterCount);

        // PendingRewards[0]：RewardSpec 全形状逐字段
        Assert.Equal(2, loaded.PendingRewards.Count);
        var r0 = loaded.PendingRewards[0];
        Assert.Equal("l1-choices-deferred", r0.Reason);
        var spec = r0.Spec;
        Assert.NotNull(spec);
        Assert.Equal(2, spec.Items.Count);
        Assert.Equal(2401, spec.Items[0].ItemId);
        Assert.Equal(2, spec.Items[0].Count);
        Assert.Equal(2319, spec.Items[1].ItemId);
        Assert.Equal(1, spec.Items[1].Count);
        Assert.Equal(new[] { 90, 302810 }, spec.Buffs);
        Assert.Single(spec.Attrs);
        Assert.Equal(2, spec.Attrs[0].AttrId);
        Assert.Equal(2.5, spec.Attrs[0].Delta);
        Assert.Equal(SpecialOp.StealParts, spec.Op);
        Assert.Equal(10, spec.OpParam);
        Assert.True(spec.PermBuffByTier);
        Assert.True(spec.RandomPickOne);
        // Choices 嵌套逐字段（含子 RewardSpec）
        Assert.Equal(2, spec.Choices.Count);
        Assert.Equal("追上去", spec.Choices[0].Label);
        Assert.Equal(new[] { 1111 }, spec.Choices[0].Reward.Buffs);
        Assert.Equal("算了", spec.Choices[1].Label);
        Assert.NotNull(spec.Choices[1].Reward);   // 空 RewardSpec 往返不丢引用形状

        // PendingRewards[1]：空条目
        Assert.Equal("", loaded.PendingRewards[1].Reason);
        Assert.NotNull(loaded.PendingRewards[1].Spec);
    }

    [Fact]
    public void RoundTrip_NullListFields()
    {
        var slot = NewSlot();
        var state = new RollState
        {
            PendingRewards = new List<PendingReward>
            {
                new() { Reason = "r", Spec = new RewardSpec { Items = null, Buffs = null, Attrs = null, Choices = null } },
            },
        };

        StateStore.Save(state, slot);   // 忽略 null：写不炸
        var s = StateStore.Load(slot, out var hadCorruption);   // 读不炸

        Assert.False(hadCorruption);
        Assert.Single(s.PendingRewards);
        Assert.Equal("r", s.PendingRewards[0].Reason);
        var spec = s.PendingRewards[0].Spec;
        Assert.NotNull(spec);
        Assert.Equal(SpecialOp.None, spec.Op);   // 值类型字段默认值保留
        // null 列表被忽略写→读回 null（钉死：空判责任在执行方 Task 8，RewardEncoding 同口径）
        Assert.Null(spec.Items);
        Assert.Null(spec.Buffs);
        Assert.Null(spec.Attrs);
        Assert.Null(spec.Choices);
    }

    // —— 分槽隔离 / 原子覆盖 ——

    [Fact]
    public void SaveLoad_SlotIsolation()
    {
        var slotA = NewSlot();
        var slotB = NewSlot();
        StateStore.Save(new RollState
        {
            BadStreak = 7,
            YesterdayEventId = 99244,
            PendingRewards = { new() { Reason = "a", Spec = new RewardSpec() } },
        }, slotA);

        var b = StateStore.Load(slotB, out var hadCorruption);

        Assert.False(hadCorruption);
        AssertDefault(b);   // 槽 B 读到的是自己的默认值，不是槽 A 的账
        Assert.False(File.Exists(Path.Combine(slotB, StateStore.FileName)));   // 读槽不落盘
    }

    [Fact]
    public void Save_IsAtomicAndOverwrites()
    {
        var slot = NewSlot();
        StateStore.Save(new RollState { BadStreak = 1, DisasterCount = 4 }, slot);
        StateStore.Save(new RollState { BadStreak = 9, YesterdayEventId = 99200 }, slot);

        var s = StateStore.Load(slot, out var hadCorruption);

        Assert.False(hadCorruption);
        Assert.Equal(9, s.BadStreak);        // 第二次完整覆盖第一次
        Assert.Equal(99200, s.YesterdayEventId);
        Assert.Equal(0, s.DisasterCount);    // 旧值不残留
        Assert.NotNull(s.PendingRewards);    // 覆盖写不破坏"空列表非 null"约定
        Assert.Empty(s.PendingRewards);

        // tmp 已被 Move 消费，槽内只留 state.json（原子替换不留中间产物）
        Assert.Equal(new[] { StateStore.FileName },
            Directory.GetFiles(slot).Select(Path.GetFileName).OrderBy(x => x).ToArray());
    }

    // —— 落盘格式契约 ——

    [Fact]
    public void Save_FileFormat_Utf8NoBomEnumAsStringNullOmitted()
    {
        var slot = NewSlot();
        StateStore.Save(new RollState
        {
            PendingRewards = { new() { Reason = "m4", Spec = new RewardSpec { Op = SpecialOp.RotFood, OpParam = 2, Items = null } } },
        }, slot);

        var bytes = File.ReadAllBytes(Path.Combine(slot, StateStore.FileName));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);   // UTF-8 无 BOM
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"RotFood\"", text);      // 枚举转字符串（可读/防数值漂移）
        Assert.DoesNotContain("\"Items\"", text);  // null 列表不落盘（忽略 null）
    }
}
