using System;
using System.Collections.Generic;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>LuckEngine.Roll 行为规格测试（spec §5.1/§5.4 八条语义逐条落测）。
/// 随机性两路覆盖：ScriptedRandom（脚本化 NextDouble 序列）锁分支行为与掷骰次数；
/// 真实 Random(42) 锁跨次确定性。稀有度层下标约定（Task 4 EventCatalog 消费）：
/// 好运/霉运=权重数组下标 0..3（普/稀/史/传、轻/中/重/灾厄），平淡=N1..N5 下标 0..4。</summary>
public sealed class LuckEngineTests
{
    private static EventDef Ev(int id, LuckKind kind, int rarity, int weight = 1) => new()
    {
        Id = id,
        Key = "t" + id,
        Kind = kind,
        Rarity = rarity,
        Weight = weight,
    };

    private static DifficultyParams P(double good = 0, double plain = 0, double bad = 0,
        double[] goodRarity = null, double[] badRarity = null, int pityDays = 0, int permCap = 0) => new()
    {
        Tier = 3,
        Good = good,
        Plain = plain,
        Bad = bad,
        GoodRarity = goodRarity ?? new[] { 100d, 0d, 0d, 0d },
        BadRarity = badRarity ?? new[] { 100d, 0d, 0d, 0d },
        RewardMult = 1,
        LossMult = 1,
        PityDays = pityDays,
        PermNegativeCap = permCap,
    };

    private static RollState St(int badStreak = 0, int yesterday = 0, int permUsed = 0) => new()
    {
        BadStreak = badStreak,
        YesterdayEventId = yesterday,
        PermNegativeUsed = permUsed,
    };

    /// <summary>脚本化随机源：按序吐出预设 NextDouble 值，耗尽即抛错（实现多掷一骰立刻暴露）。</summary>
    private sealed class ScriptedRandom : Random
    {
        private readonly double[] _values;
        private int _i;

        public ScriptedRandom(params double[] values) : base(0) => _values = values;

        public override double NextDouble() =>
            _i < _values.Length ? _values[_i++] : throw new InvalidOperationException("script exhausted");
    }

    // —— 1. 种子化确定性 ——

    [Fact]
    public void Roll_SeededRandom42_DeterministicAcrossRolls()
    {
        var p = P(good: 0.5, plain: 0.3, bad: 0.2,
            goodRarity: new[] { 70d, 22d, 7d, 1d },
            badRarity: new[] { 70d, 25d, 4.5d, 0.5d },
            pityDays: 3);
        var cands = new List<EventDef>();
        for (var r = 0; r < 4; r++) cands.Add(Ev(101 + r, LuckKind.Good, r, r + 1));
        for (var n = 0; n < 5; n++) cands.Add(Ev(201 + n, LuckKind.Plain, n, 1));
        for (var r = 0; r < 4; r++) cands.Add(Ev(301 + r, LuckKind.Bad, r, 2 * r + 1));

        var a = LuckEngine.Roll(p, St(), cands, _ => true, _ => false, id => id + 1000, new Random(42));
        var b = LuckEngine.Roll(p, St(), cands, _ => true, _ => false, id => id + 1000, new Random(42));

        Assert.NotEqual(0, a.EventId);   // 全层有货：掷骰链完整走通
        Assert.Equal(a, b);              // 同种子同序列：五字段值相等
    }

    // —— 2. 保底：连败达档位天数 → 强制好运并跳过运势掷骰 ——

    [Fact]
    public void Roll_PityForceGood_SkipsKindRoll()
    {
        // Good=0：无保底时永远掷不出好运——出现 Good 只可能是保底强制
        var p = P(good: 0, plain: 1, bad: 0, goodRarity: new[] { 60d, 40d, 0d, 0d }, pityDays: 1);
        var s = St(badStreak: 1);
        var cands = new List<EventDef>
        {
            Ev(11, LuckKind.Good, 0),
            Ev(12, LuckKind.Good, 1),
            Ev(21, LuckKind.Plain, 0),
        };

        // 0.999 若被当运势掷骰消费 → 档掷 Plain / 稀有度取 0.5 落普通层；正确行为=跳过运势骰，
        // 0.999 直接作稀有度骰（99.9 落稀有层[60,100)）
        var o = LuckEngine.Roll(p, s, cands, _ => true, _ => false, id => id, new ScriptedRandom(0.999, 0.5));

        Assert.Equal(LuckKind.Good, o.Kind);
        Assert.True(o.PityUsed);
        Assert.Equal(1, o.RarityIndex);
        Assert.Equal(12, o.EventId);
    }

    [Fact]
    public void Roll_NoPity_Tier5NeverForces()
    {
        // 档 5 厄运缠身参数（spec §5.2）：PityDays=0 → BadStreak 再高也不强制
        var p = new DifficultyParams
        {
            Tier = 5,
            Good = 0.15,
            Plain = 0.25,
            Bad = 0.60,
            GoodRarity = new[] { 84d, 13d, 2.7d, 0.3d },
            BadRarity = new[] { 45d, 30d, 19d, 6d },
            RewardMult = 0.5,
            LossMult = 2.5,
            PityDays = 0,
            PermNegativeCap = 4,
        };
        var s = St(badStreak: 99);
        var cands = new List<EventDef> { Ev(31, LuckKind.Bad, 1) };

        var o = LuckEngine.Roll(p, s, cands, _ => true, _ => false, id => id, new ScriptedRandom(0.5, 0.5, 0.0));

        Assert.Equal(LuckKind.Bad, o.Kind);   // 0.5 落霉运段 [0.40,1.00)
        Assert.False(o.PityUsed);
        Assert.Equal(1, o.RarityIndex);       // 50 落中度层 [45,75)
        Assert.Equal(31, o.EventId);
    }

    // —— 3. 防连抽：选中昨日事件 → 同候选集重掷一次，仍中则接受 ——

    [Fact]
    public void Roll_AntiRepeat_RerollsOnceAndAvoidsYesterday()
    {
        var p = P(good: 1);
        var s = St(yesterday: 101);
        var cands = new List<EventDef>
        {
            Ev(101, LuckKind.Good, 0, 50),
            Ev(102, LuckKind.Good, 0, 30),
            Ev(103, LuckKind.Good, 0, 20),
        };

        // 首掷 0.0→101（撞昨日）；重掷 0.6→60 落 102（[50,80) 段）
        var o = LuckEngine.Roll(p, s, cands, _ => true, _ => false, id => id, new ScriptedRandom(0.0, 0.0, 0.0, 0.6));

        Assert.Equal(102, o.EventId);
        Assert.Equal(LuckKind.Good, o.Kind);
        Assert.Equal(0, o.RarityIndex);
        Assert.False(o.Substituted);
    }

    [Fact]
    public void Roll_AntiRepeat_SingleEventLayer_AcceptsSameAfterOneReroll()
    {
        var p = P(good: 1);
        var s = St(yesterday: 101);
        var cands = new List<EventDef> { Ev(101, LuckKind.Good, 0) };

        var o = LuckEngine.Roll(p, s, cands, _ => true, _ => false, id => id, new ScriptedRandom(0.0, 0.0, 0.0, 0.7));

        Assert.Equal(101, o.EventId);        // 层内仅 1 事件：重掷仍中 → 接受
    }

    // —— 4. 资格过滤：无资格候选在层内选择前剔除 ——

    [Fact]
    public void Roll_EligibilityFiltered_BeforeLayerPick()
    {
        var p = P(good: 1);
        var cands = new List<EventDef>
        {
            Ev(101, LuckKind.Good, 0, 1000),
            Ev(102, LuckKind.Good, 0, 1),
        };

        // 0.0 本应选中 101（千份权重）——被剔除后才轮到 102
        var o = LuckEngine.Roll(p, St(), cands, id => id != 101, _ => false, id => id, new ScriptedRandom(0.0, 0.0, 0.0));

        Assert.Equal(102, o.EventId);
    }

    // —— 5. 霉运整层无资格 → 逐层下调（灾厄→重→中→轻），轻度层调用方保证恒有资格 ——

    [Fact]
    public void Roll_BadLayerAllIneligible_DowngradesStepByStepToLight()
    {
        var p = P(bad: 1, badRarity: new[] { 0d, 0d, 100d, 0d }, permCap: 99);   // 掷中重度(2)
        var cands = new List<EventDef>
        {
            Ev(301, LuckKind.Bad, 2),
            Ev(302, LuckKind.Bad, 2),   // 重度全无资格
            Ev(311, LuckKind.Bad, 1),   // 中度也无资格
            Ev(321, LuckKind.Bad, 0),   // 轻度恒有资格（调用方保证）
        };
        var eligible = new HashSet<int> { 321 };

        var o = LuckEngine.Roll(p, St(), cands, eligible.Contains, _ => false, id => id, new ScriptedRandom(0.0, 0.5, 0.0));

        Assert.Equal(LuckKind.Bad, o.Kind);
        Assert.Equal(0, o.RarityIndex);      // 报告实际使用的层：重度→中度→轻度
        Assert.Equal(321, o.EventId);
    }

    // —— 6. 好运/平淡层空 → 落空标记（不降层）——

    [Fact]
    public void Roll_GoodLayerEmpty_ReturnsFailureMarker()
    {
        var p = P(good: 1);   // 掷中好运普通层(0)
        var cands = new List<EventDef> { Ev(12, LuckKind.Good, 1), Ev(31, LuckKind.Bad, 0) };

        var o = LuckEngine.Roll(p, St(), cands, _ => true, _ => false, id => id, new ScriptedRandom(0.0, 0.0));

        Assert.Equal(LuckKind.Good, o.Kind);
        Assert.Equal(0, o.EventId);          // EventId=0 即落空标记
    }

    [Fact]
    public void Roll_PlainLayerEmpty_ReturnsFailureMarker()
    {
        var p = P(plain: 1);   // 掷中平淡 N1 层
        var cands = new List<EventDef> { Ev(205, LuckKind.Plain, 4) };   // 只有 N5

        var o = LuckEngine.Roll(p, St(), cands, _ => true, _ => false, id => id, new ScriptedRandom(0.0, 0.0));

        Assert.Equal(LuckKind.Plain, o.Kind);
        Assert.Equal(0, o.EventId);
    }

    // —— 7. 平淡档内 N 层权重（spec §8.1：N1=35/N2=20/N3=N4=N5=15）——

    [Fact]
    public void Roll_PlainLayerWeights_FollowSpec81()
    {
        Assert.Equal(new[] { 35d, 20d, 15d, 15d, 15d }, LuckEngine.PlainLayerWeights);

        // 防御性副本（T3 遗留 Minor 修复）：外部拿到引用也污染不了引擎常量
        var leaked = LuckEngine.PlainLayerWeights;
        leaked[0] = 999;
        Assert.Equal(new[] { 35d, 20d, 15d, 15d, 15d }, LuckEngine.PlainLayerWeights);

        var cands = new List<EventDef>
        {
            Ev(201, LuckKind.Plain, 0),
            Ev(202, LuckKind.Plain, 1),
            Ev(203, LuckKind.Plain, 2),
            Ev(204, LuckKind.Plain, 3),
            Ev(205, LuckKind.Plain, 4),
        };

        // 累计边界 35/55/70/85：取各段内点验证段归属（离边界 ≥1 单位，规避浮点临界）
        Assert.Equal(201, RollPlain(cands, 0.34));
        Assert.Equal(202, RollPlain(cands, 0.36));
        Assert.Equal(203, RollPlain(cands, 0.56));
        Assert.Equal(204, RollPlain(cands, 0.71));
        Assert.Equal(205, RollPlain(cands, 0.86));
    }

    private static int RollPlain(List<EventDef> cands, double rarityRoll) =>
        LuckEngine.Roll(P(plain: 1), St(), cands, _ => true, _ => false, id => id,
            new ScriptedRandom(0.0, rarityRoll, 0.0)).EventId;

    // —— 8. 永久负面上限：达上限 → substitute 替补（替补不再过检查）——

    [Fact]
    public void Roll_PermNegativeAtCap_SubstitutesWithoutRechecking()
    {
        var p = P(bad: 1, badRarity: new[] { 0d, 0d, 100d, 0d }, permCap: 1);
        var s = St(permUsed: 1);
        var cands = new List<EventDef> { Ev(301, LuckKind.Bad, 2) };
        var checkedIds = new List<int>();
        var substitutedIds = new List<int>();

        var o = LuckEngine.Roll(p, s, cands, _ => true,
            id => { checkedIds.Add(id); return true; },
            id => { substitutedIds.Add(id); return 302; },
            new ScriptedRandom(0.0, 0.5, 0.0));

        Assert.Equal(302, o.EventId);        // H1→H4 式替补
        Assert.True(o.Substituted);
        Assert.Equal(LuckKind.Bad, o.Kind);
        Assert.Equal(2, o.RarityIndex);
        Assert.Equal(new[] { 301 }, checkedIds);      // 只查了原事件
        Assert.Equal(new[] { 301 }, substitutedIds);  // 替补 302 不再过 isPermNegative
    }

    [Fact]
    public void Roll_PermNegativeUnderCap_KeepsOriginal()
    {
        var p = P(bad: 1, badRarity: new[] { 0d, 0d, 100d, 0d }, permCap: 1);
        var s = St(permUsed: 0);
        var cands = new List<EventDef> { Ev(301, LuckKind.Bad, 2) };

        var o = LuckEngine.Roll(p, s, cands, _ => true, _ => true, id => 302, new ScriptedRandom(0.0, 0.5, 0.0));

        Assert.Equal(301, o.EventId);
        Assert.False(o.Substituted);
    }

    [Fact]
    public void Roll_NonPermNegative_NeverCallsSubstitute()
    {
        var p = P(bad: 1, badRarity: new[] { 0d, 0d, 100d, 0d }, permCap: 0);
        var cands = new List<EventDef> { Ev(302, LuckKind.Bad, 2) };
        var substitutedIds = new List<int>();

        var o = LuckEngine.Roll(p, St(), cands, _ => true, _ => false,
            id => { substitutedIds.Add(id); return id; }, new ScriptedRandom(0.0, 0.5, 0.0));

        Assert.Equal(302, o.EventId);
        Assert.False(o.Substituted);
        Assert.Empty(substitutedIds);
    }
}
