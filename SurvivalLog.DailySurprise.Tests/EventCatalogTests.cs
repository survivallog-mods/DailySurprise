using System.Collections.Generic;
using System.Linq;
using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>EventCatalog/BuffBlueprints 数据校验测试（Task 4 Step 1，先写后实现）。
/// 数据权威源=spec §8（45 事件）与 §6.2/§6.3（buff 方案）；本套件钉死结构契约
/// （数量/Id 段/层级分布/顺序/引用完整性/编码），台词逐字一致由实现注释+报告逐条核对背书，
/// 抽样台词在 <see cref="Titles_Dialogues_SpotCheckVerbatim"/> 钉死。</summary>
public sealed class EventCatalogTests
{
    // —— 常量：Id 段（spec §7.3 与 Task 4 brief 的 ID 分配）——
    private const int FirstId = 99200;

    private static IReadOnlyList<EventDef> Events => EventCatalog.Events;

    private static EventDef ById(int id) => Xunit.Assert.Single(Events, e => e.Id == id);

    private static RewardSpec RewardOf(int id) => ById(id).Reward;

    // —— spec §8 出现过的全部物品 ID（gamekb Config_Item 2026-11-05 逐个实证在册，
    //    括号内为 ItemName_Local；实现/测试共用这一份白名单）——
    private static readonly HashSet<int> LegalItemIds = new()
    {
        1,     // 饼干
        7,     // 咖啡
        2101,  // 90压缩饼干
        2130,  // 可乐（2026-11-22 换入；糖 3=官方占位物品移除）
        2131,  // 能量饮料
        2319,  // 半扇猪肋排
        2323,  // 鹿腿
        2324,  // 巨型火鸡
        2400,  // 急救绷带
        2401,  // 军用医疗包
        2405,  // 抗生素
        2406,  // 退烧药
        2539,  // 西瓜
        2916,  // 吗啡注射液
        8001,  // 卡式气瓶
        11003, // 食品大包裹
        11032, // 军用指北针
        11033, // 古代银币
        11035, // AR眼镜
        11038, // 相机
        13001, // 随机材料包X8
        14034, // 双门冰箱包裹
        14056, // 冰柜包裹
        14057, // 洗衣机包裹
        14059, // 微波炉包裹
        14062, // 电视机包裹
        14097, // 电烤箱包裹
        20362, // 铁制零件
        20373, // 不锈钢板
        27004, // 超大电池组
        27007, // 极速快充模块
        27010, // 高速轮组
        3034,  // 制造的完整手稿
        3035,  // 种植的完整手稿
        3036,  // 烹饪的完整手稿
        3037,  // 陷阱的完整手稿
        40000, // 柴油
    };

    // —— §6.1 直接使用的原生 buff Id（15 行，逐字照抄 spec 表）——
    private static readonly HashSet<int> NativeBuffIds = new()
    {
        11004, 20020, 2, 11003, 1009, 90, 302810, 11001, 40030, 1102,
        1116, 1110, 1105, 1111, 1112,
    };

    // —— 事件数量与 Id 段 ——

    [Fact]
    public void Events_CountIs45()
    {
        Assert.Equal(45, Events.Count);
    }

    [Fact]
    public void Events_IdsUniqueAndSequentialInSegment()
    {
        // brief ID 分配：N 99200-99204、G 99205-99212、R 99213-99218、E 99219-99223、
        // L 99224-99226、B 99227-99232、M 99233-99237、H 99238-99241、D 99242-99244（顺排）
        Assert.Equal(Enumerable.Range(FirstId, 45), Events.Select(e => e.Id));
        Assert.Equal(45, Events.Select(e => e.Id).Distinct().Count());   // 唯一
    }

    [Fact]
    public void Events_KeysFollowSectionAllocation()
    {
        // 分段前缀 + 段内序号 = Key；【选项】标记（L1/H3）不是标题的一部分
        var expected = new Dictionary<int, string>();
        void Add(int start, int count, string prefix)
        {
            for (var i = 1; i <= count; i++) expected[start + i - 1] = prefix + i;
        }
        Add(99200, 5, "n"); Add(99205, 8, "g"); Add(99213, 6, "r"); Add(99219, 5, "e");
        Add(99224, 3, "l"); Add(99227, 6, "b"); Add(99233, 5, "m"); Add(99238, 4, "h");
        Add(99242, 3, "d");
        Assert.Equal(45, expected.Count);
        Assert.All(Events, e => Assert.Equal(expected[e.Id], e.Key));
    }

    [Fact]
    public void Events_KeysUniqueAndCoreFieldsNonEmpty()
    {
        Assert.Equal(45, Events.Select(e => e.Key).Distinct().Count());
        Assert.All(Events, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Title));
            Assert.False(string.IsNullOrWhiteSpace(e.Dialogue));
            Assert.False(string.IsNullOrWhiteSpace(e.Icon));
            Assert.NotNull(e.Reward);
            Assert.NotNull(e.Reward.Items);
            Assert.NotNull(e.Reward.Buffs);
            Assert.NotNull(e.Reward.Attrs);
            Assert.NotNull(e.Reward.Choices);
        });
    }

    // —— 层级分布（Task 3 契约：Rarity=0 基层下标）——

    [Fact]
    public void Events_TierCounts()
    {
        Assert.Equal(5, Events.Count(e => e.Kind == LuckKind.Plain));
        Assert.Equal(22, Events.Count(e => e.Kind == LuckKind.Good));
        Assert.Equal(18, Events.Count(e => e.Kind == LuckKind.Bad));

        // 好运内 普/稀/史/传 = 8/6/5/3；霉运内 轻/中/重/灾厄 = 6/5/4/3
        Assert.Equal(8, Events.Count(e => e.Kind == LuckKind.Good && e.Rarity == 0));
        Assert.Equal(6, Events.Count(e => e.Kind == LuckKind.Good && e.Rarity == 1));
        Assert.Equal(5, Events.Count(e => e.Kind == LuckKind.Good && e.Rarity == 2));
        Assert.Equal(3, Events.Count(e => e.Kind == LuckKind.Good && e.Rarity == 3));
        Assert.Equal(6, Events.Count(e => e.Kind == LuckKind.Bad && e.Rarity == 0));
        Assert.Equal(5, Events.Count(e => e.Kind == LuckKind.Bad && e.Rarity == 1));
        Assert.Equal(4, Events.Count(e => e.Kind == LuckKind.Bad && e.Rarity == 2));
        Assert.Equal(3, Events.Count(e => e.Kind == LuckKind.Bad && e.Rarity == 3));
    }

    // —— 平淡层：顺序（引擎 PlainLayerWeights 按目录顺序消费）与权重 ——

    [Fact]
    public void Events_PlainOrderIsN1toN5()
    {
        Assert.All(Events.Take(5), e => Assert.Equal(LuckKind.Plain, e.Kind)); // 平淡段在最前
        var plain = Events.Where(e => e.Kind == LuckKind.Plain).ToList();
        Assert.Equal(5, plain.Count);
        // 严格按 N1,N2,N3,N4,N5 排列（下标=Rarity 0..4，引擎按目录顺序消费层权重）
        Assert.Equal(Enumerable.Range(0, 5), plain.Select(e => e.Rarity));
        Assert.Equal(Enumerable.Range(FirstId, 5), plain.Select(e => e.Id));
        Assert.Equal(new[] { "n1", "n2", "n3", "n4", "n5" }, plain.Select(e => e.Key));
    }

    [Fact]
    public void Events_AllPlainWeightsSum100_AndMatchEngineConstant()
    {
        var plain = Events.Where(e => e.Kind == LuckKind.Plain).ToList();
        Assert.Equal(100d, plain.Sum(e => (double)e.Weight));
        // 逐位与引擎常量一致（35/20/15/15/15）——事件 Weight 即所在 N 层权重的文档化
        Assert.Equal(LuckEngine.PlainLayerWeights, plain.Select(e => (double)e.Weight).ToArray());
    }

    [Fact]
    public void Events_NonPlainWeightsAllEqualOne()
    {
        // spec §8：平淡层外各层内等权
        Assert.All(Events.Where(e => e.Kind != LuckKind.Plain), e => Assert.Equal(1, e.Weight));
    }

    // —— 引用完整性 ——

    [Fact]
    public void Events_ReferencedItemsExist()
    {
        foreach (var e in Events)
        {
            foreach (var g in e.Reward.Items)
                Assert.True(LegalItemIds.Contains(g.ItemId),
                    $"事件 {e.Key}({e.Id}) 引用了白名单外物品 {g.ItemId}");
            foreach (var c in e.Reward.Choices)
            {
                foreach (var g in c.Reward.Items)
                    Assert.True(LegalItemIds.Contains(g.ItemId),
                        $"事件 {e.Key}({e.Id}) 选项「{c.Label}」引用了白名单外物品 {g.ItemId}");
                foreach (var g in c.Reward.ItemCosts)
                    Assert.True(LegalItemIds.Contains(g.ItemId),
                        $"事件 {e.Key}({e.Id}) 选项「{c.Label}」ItemCost 引用了白名单外物品 {g.ItemId}");
            }
        }
    }

    [Fact]
    public void Events_ItemCountsAllPositive()
    {
        foreach (var e in Events)
        {
            foreach (var g in e.Reward.Items)
                Assert.True(g.Count > 0, $"{e.Key}: {g.ItemId} 数量须为正");
            foreach (var c in e.Reward.Choices)
            {
                foreach (var g in c.Reward.Items)
                    Assert.True(g.Count > 0, $"{e.Key}/{c.Label}: {g.ItemId} 数量须为正");
                foreach (var g in c.Reward.ItemCosts)
                    Assert.True(g.Count > 0, $"{e.Key}/{c.Label}: ItemCost {g.ItemId} 数量须为正");
            }
        }
    }

    [Fact]
    public void Events_BuffsReferBlueprintOrNative()
    {
        var blueprintIds = BuffBlueprints.All.Select(b => b.NewId).ToHashSet();
        var legal = blueprintIds.Concat(NativeBuffIds).ToHashSet();
        foreach (var e in Events)
        {
            foreach (var b in e.Reward.Buffs)
                Assert.True(legal.Contains(b), $"事件 {e.Key}({e.Id}) 引用了非法 buff {b}");
            foreach (var c in e.Reward.Choices)
                foreach (var b in c.Reward.Buffs)
                    Assert.True(legal.Contains(b), $"事件 {e.Key}({e.Id}) 选项「{c.Label}」引用了非法 buff {b}");
        }
    }

    // —— buff 引用逐条钉死（§6.1 原生行 + §6.2 克隆新 Id）——

    [Fact]
    public void Events_BuffUsagePerEventMatchSpec()
    {
        Assert.Equal(new[] { 11004 }, RewardOf(99208).Buffs);        // G4 精神振奋（原生）
        Assert.Equal(new[] { 20020 }, RewardOf(99211).Buffs);        // G7 能量饮料（原生，隐藏副作用彩蛋）
        Assert.Equal(new[] { 2 }, RewardOf(99212).Buffs);            // G8 神清气爽（原生）
        Assert.Equal(new[] { 11003, 1009 }, RewardOf(99216).Buffs);  // R4 意外惊喜+愿望满足（原生）
        Assert.Equal(new[] { 90, 302810 }, RewardOf(99223).Buffs);   // E5 从容避难+休养生息（原生）
        Assert.Equal(new[] { 99282 }, RewardOf(99227).Buffs);        // B1 蚊虫叮咬（克隆11002 新Id）
        Assert.Equal(new[] { 11001 }, RewardOf(99228).Buffs);        // B2 肌肉酸痛（原生）
        Assert.Equal(new[] { 40030 }, RewardOf(99229).Buffs);        // B3 轻度腹泻（原生）
        Assert.Equal(new[] { 99283 }, RewardOf(99230).Buffs);        // B4 扭伤（克隆40060 新Id）
        Assert.Equal(new[] { 1102 }, RewardOf(99231).Buffs);         // B5 胃痛（原生）
        Assert.Equal(new[] { 99284 }, RewardOf(99232).Buffs);        // B6 睡不着（克隆11008 新Id）
        Assert.Equal(new[] { 1116, 1110 }, RewardOf(99234).Buffs);   // M2 风寒+发烧（原生）
        Assert.Equal(new[] { 1105 }, RewardOf(99237).Buffs);         // M5 中毒（原生）
        Assert.Equal(new[] { 99280 }, RewardOf(99238).Buffs);        // H1 心结（克隆24112 新Id）
        Assert.Equal(new[] { 1112 }, RewardOf(99241).Buffs);         // H4 重度感染（原生）
        Assert.Equal(new[] { 99280, 99281 }, RewardOf(99244).Buffs); // D3 心结克隆+失眠阴影（克隆24112/80007）
    }

    [Fact]
    public void Events_CloneSourceIdsNeverReferencedDirectly()
    {
        // §6.2 五个克隆源 Id 不出现在任何 Buffs（一律用 99280 段新 Id）
        var used = Events.SelectMany(e => e.Reward.Buffs)
            .Concat(Events.SelectMany(e => e.Reward.Choices.SelectMany(c => c.Reward.Buffs)))
            .ToHashSet();
        foreach (var source in new[] { 24112, 80007, 11002, 40060, 11008 })
            Assert.False(used.Contains(source), $"克隆源 {source} 不应被直接引用");
    }

    // —— 物品奖励数量（spec §8 逐条钉死）——

    [Fact]
    public void Events_ItemRewardsMatchSpec()
    {
        Expect.Items(RewardOf(99205), (2101, 4), (1, 6));              // G1 90压缩饼干×4、饼干×6
        Expect.Items(RewardOf(99206), (2400, 2), (2406, 2));           // G2 急救绷带×2、退烧药×2
        Expect.Items(RewardOf(99207), (2539, 3), (2130, 5));              // G3 西瓜×3、糖×5
        Expect.Items(RewardOf(99208), (7, 4));                         // G4 咖啡×4
        Expect.Items(RewardOf(99209), (11003, 1));                     // G5 食品大包裹×1
        Expect.Items(RewardOf(99210), (13001, 2));                     // G6 随机材料包X8×2
        Expect.Items(RewardOf(99211), (2131, 6));                      // G7 能量饮料×6
        Expect.Items(RewardOf(99212), (2130, 2));                        // G8 糖×10
        Expect.Items(RewardOf(99213), (20362, 12), (20373, 6));        // R1 铁制零件×12、不锈钢板×6
        Expect.Items(RewardOf(99215), (2401, 2), (2405, 3), (2916, 1));// R3 医疗包×2、抗生素×3、吗啡×1
        Expect.Items(RewardOf(99217), (2323, 2), (2324, 1));           // R5 鹿腿×2、巨型火鸡×1
        Expect.Items(RewardOf(99218), (40000, 15), (8001, 8));         // R6 柴油×15、卡式气瓶×8
        Expect.Items(RewardOf(99226), (2401, 2), (2319, 1));           // L3 医疗包×2、半扇猪肋排×1
    }

    // —— 随机×1 候选池（R2/E1/E3/E4/L2）——

    [Fact]
    public void Events_RandomPickOne_PoolsAndFlagMatchSpec()
    {
        // spec §8 五个「随机×1」事件：Items=候选池（各 Count=1）+ RandomPickOne=true；
        // 只随机发 1 件的语义由 Task 8 RewardExecutor 消费 RandomPickOne 实现
        Expect.Pool(RewardOf(99214), 14057, 14059, 14062);        // R2 洗衣机/微波炉/电视机包裹
        Expect.Pool(RewardOf(99219), 3034, 3035, 3036, 3037);     // E1 完整手稿四选一
        Expect.Pool(RewardOf(99221), 11033, 11038, 11032, 11035); // E3 银币/相机/指北针/AR眼镜
        Expect.Pool(RewardOf(99222), 14056, 14034, 14097);        // E4 冰柜/双门冰箱/电烤箱包裹
        Expect.Pool(RewardOf(99225), 27004, 27007, 27010);        // L2 机器人模块三选一

        // 其余 40 个事件：全部发放语义
        var randomIds = new HashSet<int> { 99214, 99219, 99221, 99222, 99225 };
        Assert.All(Events.Where(e => !randomIds.Contains(e.Id)),
            e => Assert.False(e.Reward.RandomPickOne));
    }

    // —— 属性变化（N 层；AttrName 枚举值=1/2/3/4/5，dump.cs:57466 起）——

    [Fact]
    public void Events_PlainAttrChangesMatchSpec()
    {
        Assert.Empty(RewardOf(99200).Attrs);              // N1 无效果
        Expect.Attr(RewardOf(99201), (2, 2d));            // N2 心态+2（AttrName.Morale=2）
        Expect.Attr(RewardOf(99202), (2, 1d));            // N3 心态+1
        Expect.Attr(RewardOf(99203), (3, -2d));           // N4 精力-2（AttrName.Stamina=3）
        Assert.Empty(RewardOf(99204).Attrs);              // N5 无效果
    }

    [Fact]
    public void Events_AttrChangesOnlyInPlainLayer()
    {
        Assert.All(Events.Where(e => e.Kind != LuckKind.Plain), e => Assert.Empty(e.Reward.Attrs));
    }

    // —— 特殊操作与资格门（§8 效果列 + §5.4 前置条件）——

    [Fact]
    public void Events_SpecialOpsMatchSpec()
    {
        var m1 = ById(99233); // M1 老鼠的牙：随机1处防御设施/门窗耐久-15%
        Assert.Equal(SpecialOp.DurabilityPercentRandom, m1.Reward.Op);
        Assert.Equal(15d, m1.Reward.OpParam);

        var m3 = ById(99235); // M3 冰雹：全屋家具耐久-5%
        Assert.Equal(SpecialOp.DurabilityPercentAll, m3.Reward.Op);
        Assert.Equal(5d, m3.Reward.OpParam);

        var m4 = ById(99236); // M4 变质：背包生鲜随机腐坏1~2件
        Assert.Equal(SpecialOp.RotFood, m4.Reward.Op);
        Assert.Equal(2d, m4.Reward.OpParam);

        var d1 = ById(99242); // D1 代价：随机1项基础属性永久-10
        Assert.Equal(SpecialOp.AttrPermRandom, d1.Reward.Op);
        Assert.Equal(10d, d1.Reward.OpParam);

        var h3 = ById(99240); // H3 贼影：失去铁制零件×10
        Assert.Equal(SpecialOp.StealParts, h3.Reward.Op);
        Assert.Equal(10d, h3.Reward.OpParam);
    }

    [Fact]
    public void Events_EligibilityOnlyForAssetDependentEvents()
    {
        // spec §5.4：M1/M3/M4/H2/D2/H3 依赖玩家资产（家具/生鲜/零件），其余 None
        var expected = new Dictionary<int, Eligibility>
        {
            [99233] = Eligibility.HasFurnitureAtLeast1, // M1 有耐久组件家具（2026-11-22 电器门退役，见 EventCatalog M1 注）
            [99235] = Eligibility.HasFurnitureAtLeast1,  // M3 有家具
            [99236] = Eligibility.HasFreshFood,          // M4 背包有生鲜
            [99239] = Eligibility.HasFurnitureAtLeast1,  // H2 有家具
            [99243] = Eligibility.HasFurnitureAtLeast1,  // D2 有家具
            [99240] = Eligibility.HasParts10,            // H3 铁制零件≥10
        };
        Assert.Equal(6, expected.Count);
        Assert.All(Events, e =>
        {
            if (expected.TryGetValue(e.Id, out var elig))
                Assert.Equal(elig, e.Elig);
            else
                Assert.Equal(Eligibility.None, e.Elig);
        });
    }

    // —— H2/D2 耐久区间编码（OpParam 编码契约，Task 9 PunishmentOps 消费）——

    [Fact]
    public void Events_H2D2_DurabilityRangeEncoded()
    {
        // H2 管道爆裂：3~5台、每台-25%；D2 雷击：2~4台、每台-40%（不摧毁）
        var h2 = ById(99239);
        Assert.Equal(SpecialOp.DurabilityPercentRange, h2.Reward.Op);
        Assert.Equal(EventCatalog.EncodeDurabilityRange(3, 5, 25), h2.Reward.OpParam);
        var d2 = ById(99243);
        Assert.Equal(SpecialOp.DurabilityPercentRange, d2.Reward.Op);
        Assert.Equal(EventCatalog.EncodeDurabilityRange(2, 4, 40), d2.Reward.OpParam);

        EventCatalog.DecodeDurabilityRange(h2.Reward.OpParam, out var min, out var max, out var pct);
        Assert.Equal((3, 5, 25), (min, max, pct));
        EventCatalog.DecodeDurabilityRange(d2.Reward.OpParam, out min, out max, out pct);
        Assert.Equal((2, 4, 40), (min, max, pct));
    }

    [Fact]
    public void DurabilityRangeCodec_RoundTrips()
    {
        foreach (var (min, max, pct) in new[] { (1, 1, 5), (2, 4, 40), (3, 5, 25), (1, 99, 999) })
        {
            var encoded = EventCatalog.EncodeDurabilityRange(min, max, pct);
            EventCatalog.DecodeDurabilityRange(encoded, out var a, out var b, out var c);
            Assert.Equal((min, max, pct), (a, b, c));
        }
    }

    // —— 选项事件 ——

    [Fact]
    public void Events_L1_ThreeChoicesWithOwnItems()
    {
        var l1 = ById(99224); // L1 神秘补给箱
        Assert.Empty(l1.Reward.Items);   // 奖励在选项里，顶层不发
        Assert.Equal(SpecialOp.None, l1.Reward.Op);
        var choices = l1.Reward.Choices;
        Assert.Equal(3, choices.Count);
        Assert.Equal("食物层", choices[0].Label);
        Expect.Items(choices[0].Reward, (2101, 8), (11003, 2));          // 压缩饼干×8+食品大包裹×2
        Assert.Equal("药品层", choices[1].Label);
        Expect.Items(choices[1].Reward, (2401, 3), (2405, 3), (2916, 2));// 医疗包×3+抗生素×3+吗啡×2
        Assert.Equal("材料层", choices[2].Label);
        Expect.Items(choices[2].Reward, (20362, 30), (20373, 15));       // 铁制零件×30+不锈钢板×15
        Assert.All(choices, c => Assert.False(c.Reward.RandomPickOne));  // 玩家三选一，非随机
    }

    [Fact]
    public void Events_H3_TwoChoices_ChaseBleeds_GiveUpCostsParts()
    {
        var h3 = ById(99240); // H3 贼影
        var choices = h3.Reward.Choices;
        Assert.Equal(2, choices.Count);
        Assert.Equal("追上去", choices[0].Label);
        // Task 10 定案（DESIGN.md §10）：50/50 表达不了（RandomGroup 只做物品池随机）——
        // 追=受伤流血(1111) 声明式（AddBuff+BuffTargetType/BuffTargetID=1/0）；
        // "追回"=零件未失、不另发物品（防点追净 +10 刷零件，Controller 裁定 2026-11-12）
        Assert.Equal(new[] { 1111 }, choices[0].Reward.Buffs);
        Assert.Empty(choices[0].Reward.Items);
        Assert.Empty(choices[0].Reward.ItemCosts);
        Assert.Equal(SpecialOp.None, choices[0].Reward.Op);
        // 算了=ItemCost 铁制零件×10 声明式：零件只在点"算了"时离开背包（顶层 StealParts 被 Choices 短路）
        Assert.Equal("算了", choices[1].Label);
        Assert.Empty(choices[1].Reward.Buffs);
        Assert.Empty(choices[1].Reward.Items);
        Assert.Equal(SpecialOp.None, choices[1].Reward.Op);
        var cost = Assert.Single(choices[1].Reward.ItemCosts);
        Assert.Equal(20362, cost.ItemId);
        Assert.Equal(10, cost.Count);
    }

    [Fact]
    public void Events_ChoicesOnlyOnL1AndH3()
    {
        // spec §8 仅 L1/H3 标【选项】
        var withChoices = Events.Where(e => e.Reward.Choices.Count > 0).Select(e => e.Id).ToList();
        Assert.Equal(new[] { 99224, 99240 }, withChoices);
    }

    // —— 永久负面与替补（§5.4）——

    [Fact]
    public void Events_PermNegativeIdsAreH1D1D3()
    {
        // spec §5.4：H1/D1/D3 各计 1 次永久负面（H3 不占）
        Assert.Equal(new[] { 99238, 99242, 99244 }, EventCatalog.PermNegativeIds);
    }

    [Fact]
    public void Events_Substitutes_PointToExistingNonPerm()
    {
        var subs = EventCatalog.Substitutes;
        Assert.Equal(3, subs.Count);
        Assert.Equal(99241, subs[99238]); // H1 心结 → H4 重病
        Assert.Equal(99243, subs[99242]); // D1 代价 → D2 雷击
        Assert.Equal(99239, subs[99244]); // D3 低语 → H2 管道爆裂

        var ids = Events.Select(e => e.Id).ToHashSet();
        var perm = EventCatalog.PermNegativeIds.ToHashSet();
        foreach (var (from, to) in subs)
        {
            Assert.True(ids.Contains(from), $"替补源 {from} 必须在目录中");
            Assert.True(ids.Contains(to), $"替补目标 {to} 必须在目录中");
            Assert.False(perm.Contains(to), $"替补目标 {to} 不得再占永久名额");
            Assert.True(perm.Contains(from), $"替补源 {from} 必须是永久负面事件");
        }
    }

    // —— E2/L3 命运眷顾（PermBuffByTier，变体选择归 Task 8 RewardMath）——

    [Fact]
    public void Events_E2L3_HavePermBuffByTierAndEmptyBuffs()
    {
        var e2 = ById(99220); // E2 命运眷顾（E 段 99219~99223：E1=99219…E2=99220）
        Assert.True(e2.Reward.PermBuffByTier);
        Assert.Empty(e2.Reward.Buffs);
        Assert.Empty(e2.Reward.Items);
        Assert.Equal(SpecialOp.None, e2.Reward.Op);

        var l3 = ById(99226); // L3 传说之日
        Assert.True(l3.Reward.PermBuffByTier);
        Assert.Empty(l3.Reward.Buffs);
    }

    [Fact]
    public void Events_PermBuffByTierOnlyOnE2L3()
    {
        var flagged = Events.Where(e => e.Reward.PermBuffByTier).Select(e => e.Id).ToList();
        Assert.Equal(new[] { 99220, 99226 }, flagged);
    }

    // —— 台词/标题抽样钉死（spec §8 逐字）——

    [Theory]
    [InlineData(99200, "什么都没发生", "今天，什么也没发生。你还活着，这很好。")]
    [InlineData(99205, "救灾空投", "天上掉下来一个箱子，降落伞没打开，但箱子的质量出乎意料。")]
    [InlineData(99224, "神秘补给箱", "军绿色箱子，封条完好。上面写着你只能带走一层。")]
    [InlineData(99240, "贼影", "地上有陌生的脚印，你的零件箱轻了。")]
    [InlineData(99244, "低语", "你在井底听见有人叫你的名字。之后的日子不太对劲。")]
    public void Titles_Dialogues_SpotCheckVerbatim(int id, string title, string dialogue)
    {
        Assert.Equal(title, ById(id).Title);
        Assert.Equal(dialogue, ById(id).Dialogue);
    }

    // —— BuffBlueprints：数量/Id/源 ——

    [Fact]
    public void Blueprints_CountIs14_IdsUniqueFrom99280()
    {
        Assert.Equal(14, BuffBlueprints.All.Count);
        Assert.Equal(Enumerable.Range(99280, 14), BuffBlueprints.All.Select(b => b.NewId));
        Assert.All(BuffBlueprints.All, b =>
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Name));
            Assert.False(string.IsNullOrWhiteSpace(b.Des));
            Assert.NotNull(b.FieldOverrides);
            Assert.NotEmpty(b.FieldOverrides);
        });
    }

    [Fact]
    public void Blueprints_CloneSourcesMatchSpec()
    {
        // §6.2 五源（24112/80007/11002/40060/11008）+ §6.3 两源（90 命运眷顾、24112 行运低潮）
        var sources = BuffBlueprints.All.Select(b => b.CloneSourceId).ToHashSet();
        Assert.True(sources.SetEquals(new HashSet<int> { 24112, 80007, 11002, 40060, 11008, 90 }),
            "克隆源集合 = §6.2 五源 ∪ {90}");

        // §6.2 五克隆=前 5 条（NewId 99280~99284），源与名称对号
        Assert.Equal((99280, 24112, "心结"), (BuffBlueprints.All[0].NewId, BuffBlueprints.All[0].CloneSourceId, BuffBlueprints.All[0].Name));
        Assert.Equal((99281, 80007, "失眠阴影"), (BuffBlueprints.All[1].NewId, BuffBlueprints.All[1].CloneSourceId, BuffBlueprints.All[1].Name));
        Assert.Equal((99282, 11002, "蚊虫叮咬"), (BuffBlueprints.All[2].NewId, BuffBlueprints.All[2].CloneSourceId, BuffBlueprints.All[2].Name));
        Assert.Equal((99283, 40060, "扭伤"), (BuffBlueprints.All[3].NewId, BuffBlueprints.All[3].CloneSourceId, BuffBlueprints.All[3].Name));
        Assert.Equal((99284, 11008, "睡不着"), (BuffBlueprints.All[4].NewId, BuffBlueprints.All[4].CloneSourceId, BuffBlueprints.All[4].Name));
    }

    [Fact]
    public void Blueprints_AllSetOverlayZero()
    {
        // spec §4：本项目用到的全部 buff 一律 Overlay=0（时长叠加、强度不变）
        Assert.All(BuffBlueprints.All, b => Assert.Equal(0, b.FieldOverrides["Overlay"]));
    }

    [Fact]
    public void Blueprints_ClonedFive_MatchSpec62()
    {
        static object Ov(int newId, string key) =>
            Xunit.Assert.Single(BuffBlueprints.All, b => b.NewId == newId).FieldOverrides[key];

        // 心结：照抄（心态/精力获得效率各-0.5% = 源行 Effect 200024112 值，不动数值），永久
        Assert.Equal(-1, Ov(99280, "BuffDuring"));
        // 失眠阴影：照抄（精力上限-20 = 源行 Effect 200080007 值），永久
        Assert.Equal(-1, Ov(99281, "BuffDuring"));
        // 蚊虫叮咬：修数值 精力上限-5、12h（源行 Effect 200011002 全零，描述写了没做）
        Assert.Equal(-5, Ov(99282, "BuffStaminaMax"));
        Assert.Equal(43200, Ov(99282, "BuffDuring"));
        // 扭伤：修数值 移速-15%、12h（弃 BagOverweight 标签层，走标准 BuffSpeed）
        Assert.Equal(-0.15, Ov(99283, "BuffSpeed"));
        Assert.Equal(43200, Ov(99283, "BuffDuring"));
        // 睡不着：修数值 精力获得效率-30%、24h（对冲官方反写成福利的 BuffStaminaDel）
        Assert.Equal(-0.3, Ov(99284, "BuffStaminaAdd"));
        Assert.Equal(0, Ov(99284, "BuffStaminaDel"));
        Assert.Equal(86400, Ov(99284, "BuffDuring"));
    }

    [Fact]
    public void Blueprints_Fortune_VariantsMatchSpec63()
    {
        // 命运眷顾Ⅰ~Ⅴ：每2h心态+20/15/10/7/5，永久（克隆源=从容避难 90）
        var moraleByVariant = new[] { 0, 20, 15, 10, 7, 5 }; // 下标=变体号
        for (var v = 1; v <= 5; v++)
        {
            var b = ByBlueprint(BuffBlueprints.FortuneIds[v]);
            Assert.Equal(90, b.CloneSourceId);
            Assert.Equal($"命运眷顾{Roman(v)}", b.Name);
            Assert.Equal(-1, b.FieldOverrides["BuffDuring"]);
            Assert.Equal(7200, b.FieldOverrides["BuffInterval"]);               // 每2小时结算
            Assert.Equal(moraleByVariant[v], b.FieldOverrides["BuffMoraleInterval"]);
        }
    }

    [Fact]
    public void Blueprints_Slump_VariantsMatchSpec63()
    {
        // 行运低潮Ⅱ~Ⅴ：五维获得效率各-1/-2/-3/-4%，永久（克隆源=心结 24112，扩到五维）
        var addKeys = new[] { "BuffSatietyAdd", "BuffMoraleAdd", "BuffStaminaAdd", "BuffHealthAdd", "BuffVitalityAdd" };
        for (var v = 2; v <= 5; v++)
        {
            var b = ByBlueprint(BuffBlueprints.SlumpIds[v]);
            Assert.Equal(24112, b.CloneSourceId);
            Assert.Equal($"行运低潮{Roman(v)}", b.Name);
            Assert.Equal(-1, b.FieldOverrides["BuffDuring"]);
            foreach (var k in addKeys)
                Assert.Equal(-0.01 * (v - 1), (double)b.FieldOverrides[k]);
        }
    }

    [Fact]
    public void Blueprints_FortuneSlumpIds_IndexIsVariantNumber()
    {
        // int[]，下标即变体号：FortuneIds[1..5]；SlumpIds[2..5]（0/1 位不用——行运低潮无Ⅰ变体）
        Assert.Equal(6, BuffBlueprints.FortuneIds.Length);
        Assert.Equal(new[] { 0, 99285, 99286, 99287, 99288, 99289 }, BuffBlueprints.FortuneIds);
        Assert.Equal(6, BuffBlueprints.SlumpIds.Length);
        Assert.Equal(new[] { 0, 0, 99290, 99291, 99292, 99293 }, BuffBlueprints.SlumpIds);
    }

    [Fact]
    public void Blueprints_TextOverridesCarryLocalFields()
    {
        // 文案换皮走 Name_Local/Des_Local 裸键（Task 6 注入器拍平到两表）
        Assert.All(BuffBlueprints.All, b =>
        {
            Assert.Equal(b.Name, b.FieldOverrides["Name_Local"]);
            Assert.Equal(b.Des, b.FieldOverrides["Des_Local"]);
        });
    }

    private static BuffBlueprint ByBlueprint(int newId) =>
        Xunit.Assert.Single(BuffBlueprints.All, b => b.NewId == newId);

    private static string Roman(int v) => v switch { 1 => "Ⅰ", 2 => "Ⅱ", 3 => "Ⅲ", 4 => "Ⅳ", 5 => "Ⅴ", _ => "?" };

    // —— 复数断言小助手（本文件私有）——
    private static class Expect
    {
        public static void Items(RewardSpec r, params (int id, int count)[] expected)
        {
            var got = r.Items.Select(g => (g.ItemId, g.Count)).ToList();
            Assert.Equal(expected.ToList(), got);
        }

        public static void Pool(RewardSpec r, params int[] candidateIds)
        {
            Assert.True(r.RandomPickOne);
            Assert.Equal(candidateIds.ToList(), r.Items.Select(g => g.ItemId).ToList());
            Assert.All(r.Items, g => Assert.Equal(1, g.Count));
        }

        public static void Attr(RewardSpec r, params (int attrId, double delta)[] expected)
        {
            var got = r.Attrs.Select(a => (a.AttrId, a.Delta)).ToList();
            Assert.Equal(expected.ToList(), got);
        }
    }
}
