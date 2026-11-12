using System;
using System.Collections.Generic;

namespace SurvivalLog.DailySurprise;

/// <summary>45 事件目录（spec §8 全清单，数据冻结 2026-11-10；台词/物品 ID/数量/buff 引用逐字照抄）。
/// 结构契约：
/// - Id 段 99200~99244 顺排，顺序=N1~N5, G1~G8, R1~R6, E1~E5, L1~L3, B1~B6, M1~M5, H1~H4, D1~D3；
///   EventId=0 保留为落空哨兵（LuckEngine），目录禁用 0。
/// - Rarity=0 基层下标（Task 3 契约）：好运 0..3=普/稀/史/传，霉运 0..3=轻/中/重/灾厄，
///   平淡 0..4=N1..N5——<see cref="LuckEngine.PlainLayerWeights"/>{35,20,15,15,15} 按目录顺序消费，
///   故平淡事件必须严格按 N1..N5 排列（EventCatalogTests.PlainOrderIsN1toN5 钉死）。
/// - Weight：平淡层=所在 N 层权重（35/20/15/15/15，与引擎常量逐位一致），其余层内等权=1。
/// - 属性 AttrChange.AttrId=AttrName 枚举值（dump.cs 2026-11-05:57466 起）：
///   Satiety=1/Morale=2/Stamina=3/Health=4/Vitality=5。
/// - 物品/耐久/偷窃数值均为基准值，奖励/损失倍率缩放归 Task 8/9 执行方（spec §5.5）。</summary>
public static class EventCatalog
{
    public const int FirstEventId = 99200;
    public const int LastEventId = 99244;

    /// <summary>选项子事件 Id 段（Task 10）：事件段 99200~99244 已满，L1×3+H3×2 子事件顺延
    /// 99245~99249（与 RewardSet 99250~99299 数值重叠不冲突——不同表）。</summary>
    public const int FirstSonEventId = 99245;
    public const int LastSonEventId = 99249;

    /// <summary>事件 Id 速查常量（cfg DebugForceEventId / 接线层 / 测试引用）。</summary>
    public static class Ids
    {
        // 平淡（99200~99204）
        public const int N1 = 99200; public const int N2 = 99201; public const int N3 = 99202;
        public const int N4 = 99203; public const int N5 = 99204;
        // 好运·普通（99205~99212）
        public const int G1 = 99205; public const int G2 = 99206; public const int G3 = 99207;
        public const int G4 = 99208; public const int G5 = 99209; public const int G6 = 99210;
        public const int G7 = 99211; public const int G8 = 99212;
        // 好运·稀有（99213~99218）
        public const int R1 = 99213; public const int R2 = 99214; public const int R3 = 99215;
        public const int R4 = 99216; public const int R5 = 99217; public const int R6 = 99218;
        // 好运·史诗（99219~99223）
        public const int E1 = 99219; public const int E2 = 99220; public const int E3 = 99221;
        public const int E4 = 99222; public const int E5 = 99223;
        // 好运·传说（99224~99226）
        public const int L1 = 99224; public const int L2 = 99225; public const int L3 = 99226;
        // 霉运·轻度（99227~99232）
        public const int B1 = 99227; public const int B2 = 99228; public const int B3 = 99229;
        public const int B4 = 99230; public const int B5 = 99231; public const int B6 = 99232;
        // 霉运·中度（99233~99237）
        public const int M1 = 99233; public const int M2 = 99234; public const int M3 = 99235;
        public const int M4 = 99236; public const int M5 = 99237;
        // 霉运·重度（99238~99241）
        public const int H1 = 99238; public const int H2 = 99239; public const int H3 = 99240;
        public const int H4 = 99241;
        // 霉运·灾厄（99242~99244）
        public const int D1 = 99242; public const int D2 = 99243; public const int D3 = 99244;
    }

    /// <summary>45 事件（构造顺序即消费顺序，见类注）。</summary>
    public static readonly IReadOnlyList<EventDef> Events = Build();

    /// <summary>5 个选项子事件（Task 10 接线；Id 段见 <see cref="FirstSonEventId"/>）。
    /// 顺序契约=按父事件 Choices 同序排列（父行 ChoseID/Default 与子事件 RewardSetId→
    /// 选项 RewardSet 行映射据此派生，InjectorMappingTests 钉死）。台词=结果卡一句文案
    /// （spec §8 风格自拟）；奖励不在此声明——子事件行 RewardSetId 指向父事件对应
    /// Choice.Reward 的选项 RewardSet 行（99295~99299），由原生声明式发奖。</summary>
    public static readonly IReadOnlyList<SonEvent> SonEvents = new List<SonEvent>
    {
        new() { Id = 99245, ParentId = Ids.L1, Line = "你搬走了最沉的一层——能吃最久的那层。" },
        new() { Id = 99246, ParentId = Ids.L1, Line = "纱布、药剂、吗啡。这一层最安静，也最救命。" },
        new() { Id = 99247, ParentId = Ids.L1, Line = "金属碰撞出踏实的声音。往后的日子靠它们钉牢。" },
        new() { Id = 99248, ParentId = Ids.H3, Line = "巷口拐角，你扑住了那袋零件，胳膊擦掉一块皮。" },
        new() { Id = 99249, ParentId = Ids.H3, Line = "脚步声消失在街角。就当是交给这座城的保管费。" },
    };

    /// <summary>目录内下标（按 Id；注入映射的 RewardSet 编号用）；不在目录=-1。</summary>
    public static int EventsIndexOf(EventDef e)
    {
        if (e == null) return -1;
        for (var i = 0; i < Events.Count; i++)
            if (Events[i].Id == e.Id) return i;
        return -1;
    }

    /// <summary>永久负面达上限（或 AllowPermanentNegative=false）时的替补映射（spec §5.4）：
    /// H1 心结→H4 重病、D1 代价→D2 雷击、D3 低语→H2 管道爆裂。</summary>
    public static readonly IReadOnlyDictionary<int, int> Substitutes = new Dictionary<int, int>
    {
        [Ids.H1] = Ids.H4,
        [Ids.D1] = Ids.D2,
        [Ids.D3] = Ids.H2,
    };

    /// <summary>占永久负面上限名额的事件（spec §5.4：H1/D1/D3 各计 1 次；H3 不占）。</summary>
    public static readonly IReadOnlyList<int> PermNegativeIds = new[] { Ids.H1, Ids.D1, Ids.D3 };

    /// <summary>DurabilityPercentRange 的 OpParam 编码（H2/D2 用；Task 9 PunishmentOps 消费）：
    /// OpParam = MinCount*100000 + MaxCount*1000 + Percent（台数下限/上限 1~99，百分数 1~999）。
    /// 例：H2 管道爆裂 3~5 台、每台−25% → 305025；D2 雷击 2~4 台、每台−40% → 204040。
    /// 损失倍率对台数/百分比的缩放在执行侧进行（§5.5），本编码只承载基准值。</summary>
    public static double EncodeDurabilityRange(int minCount, int maxCount, int percent)
    {
        if (minCount is < 1 or > 99) throw new ArgumentOutOfRangeException(nameof(minCount));
        if (maxCount is < 1 or > 99 || maxCount < minCount)
            throw new ArgumentOutOfRangeException(nameof(maxCount));
        if (percent is < 1 or > 999) throw new ArgumentOutOfRangeException(nameof(percent));
        return minCount * 100000d + maxCount * 1000d + percent;
    }

    /// <summary><see cref="EncodeDurabilityRange"/> 的逆运算（纯整数位段截取）。</summary>
    public static void DecodeDurabilityRange(double opParam, out int minCount, out int maxCount, out int percent)
    {
        var v = (long)opParam;
        minCount = (int)(v / 100000);
        maxCount = (int)(v / 1000) % 100;
        percent = (int)(v % 1000);
    }

    // ———————————————————————————————— 数据 ————————————————————————————————

    private static IReadOnlyList<EventDef> Build()
    {
        return new List<EventDef>
        {
            // ———— §8.1 平平无奇（N1 权重 35，N2 20，N3/N4/N5 各 15）————
            // 平淡事件 Weight=所在 N 层权重（引擎 PlainLayerWeights 按本目录顺序消费）。
            Ev(Ids.N1, LuckKind.Plain, 0, "什么都没发生", "今天，什么也没发生。你还活着，这很好。", weight: 35),
            Ev(Ids.N2, LuckKind.Plain, 1, "窗台的鸟", "一只鸟在窗台站了一会儿，然后飞走了。它没留下什么。",
                Rs(attrs: Attrs(Attr(2, 2))), weight: 20),   // 心态+2（AttrName.Morale=2）
            Ev(Ids.N3, LuckKind.Plain, 2, "乌云像乌龟", "你盯着天上的云看了十分钟，确实很像乌龟。",
                Rs(attrs: Attrs(Attr(2, 1))), weight: 15),   // 心态+1
            Ev(Ids.N4, LuckKind.Plain, 3, "邻居的狗", "邻居家的狗叫了一整夜。你也说不上讨厌它。",
                Rs(attrs: Attrs(Attr(3, -2))), weight: 15),  // 精力−2（AttrName.Stamina=3）
            Ev(Ids.N5, LuckKind.Plain, 4, "日历", "你撕掉一页日历。纸上什么都没写。", weight: 15),

            // ———— §8.2 好运·普通 G1~G8 等权 ————
            Ev(Ids.G1, LuckKind.Good, 0, "救灾空投", "天上掉下来一个箱子，降落伞没打开，但箱子的质量出乎意料。",
                Rs(items: Items((2101, 4), (1, 6)))),                       // 90压缩饼干×4、饼干×6
            Ev(Ids.G2, LuckKind.Good, 0, "过期药品回收站", "药店的后门没锁，大部分药还在保质期内。",
                Rs(items: Items((2400, 2), (2406, 2)))),                   // 急救绷带×2、退烧药×2
            Ev(Ids.G3, LuckKind.Good, 0, "水果快递", "一辆侧翻的三轮车，水果滚了一地，车主不知去向。",
                Rs(items: Items((2539, 3), (3, 5)))),                       // 西瓜×3、糖×5
            Ev(Ids.G4, LuckKind.Good, 0, "咖啡的香气", "你在废墟里闻到了咖啡味。豆子是好的，运气也是。",
                Rs(items: Items((7, 4)), buffs: Buffs(11004))),             // 咖啡×4 + 精神振奋
            Ev(Ids.G5, LuckKind.Good, 0, "罐头雨", "杂货店的货架整排倒向了你这一边。",
                Rs(items: Items((11003, 1)))),                              // 食品大包裹×1
            Ev(Ids.G6, LuckKind.Good, 0, "路过的好人", "一个独行者留下了半袋零件：\"我用不上了。\"",
                Rs(items: Items((13001, 2)))),                              // 随机材料包X8×2
            Ev(Ids.G7, LuckKind.Good, 0, "能量饮料墙", "自动售货机被撬开了，笑的人是你。",
                Rs(items: Items((2131, 6)), buffs: Buffs(20020))),          // 能量饮料×6 + 能量饮料buff
            Ev(Ids.G8, LuckKind.Good, 0, "小确幸", "你在旧外套口袋里摸到了糖。今天不错。",
                Rs(items: Items((3, 10)), buffs: Buffs(2))),                // 糖×10 + 神清气爽

            // ———— §8.3 好运·稀有 R1~R6 等权 ————
            Ev(Ids.R1, LuckKind.Good, 1, "建材车队遗撒", "一辆卡车在路口散了架，货是真的。",
                Rs(items: Items((20362, 12), (20373, 6)))),                 // 铁制零件×12、不锈钢板×6
            // R2 家电下乡：洗衣机/微波炉/电视机包裹 随机×1 → 候选池 + RandomPickOne（Task 8 只随机发 1 件）
            Ev(Ids.R2, LuckKind.Good, 1, "家电下乡", "包裹完好无损，连说明书都在。",
                Rs(items: Pool(14057, 14059, 14062), randomPickOne: true)),
            Ev(Ids.R3, LuckKind.Good, 1, "撤离医疗点", "军方留下的储备箱，锁被撬开过——被你。",
                Rs(items: Items((2401, 2), (2405, 3), (2916, 1)))),         // 医疗包×2、抗生素×3、吗啡×1
            Ev(Ids.R4, LuckKind.Good, 1, "好日子", "一觉醒来，你觉得这个世界还没那么糟。",
                Rs(buffs: Buffs(11003, 1009))),                             // 意外惊喜+愿望满足（§6.1 原生）
            Ev(Ids.R5, LuckKind.Good, 1, "猎人的馈赠", "门口挂着一条鹿腿，没有字条。",
                Rs(items: Items((2323, 2), (2324, 1)))),                    // 鹿腿×2、巨型火鸡×1
            Ev(Ids.R6, LuckKind.Good, 1, "油料小丰收", "加油站的地下储罐里，居然还有余量。",
                Rs(items: Items((40000, 15), (8001, 8)))),                  // 柴油×15、卡式气瓶×8

            // ———— §8.4 好运·史诗 E1~E5 等权 ————
            // E1 前人的遗产：制造/种植/烹饪/陷阱完整手稿 随机×1
            Ev(Ids.E1, LuckKind.Good, 2, "前人的遗产", "铁盒里是一份手稿，字迹工整得像另一个世界的东西。",
                Rs(items: Pool(3034, 3035, 3036, 3037), randomPickOne: true)),
            // E2 命运眷顾：按档位/倍率选Ⅰ~Ⅴ（永久）→ PermBuffByTier=true、Buffs 留空；
            // 变体选择规则（spec §5.3）归 Task 8 RewardMath.PickPermBuff，蓝图见 BuffBlueprints.FortuneIds
            Ev(Ids.E2, LuckKind.Good, 2, "命运眷顾", "你昨晚做了个梦，梦里有人把一切都教给了你。",
                Rs(permBuffByTier: true)),
            // E3 旧世界的珍品：银币/相机/指北针/AR眼镜 随机×1
            Ev(Ids.E3, LuckKind.Good, 2, "旧世界的珍品", "抽屉深处，躺着一件真正值钱的东西。",
                Rs(items: Pool(11033, 11038, 11032, 11035), randomPickOne: true)),
            // E4 冰链自由：冰柜/双门冰箱/电烤箱包裹 随机×1
            Ev(Ids.E4, LuckKind.Good, 2, "冰链自由", "冷冻车侧翻在门口，冷气还在往外冒。",
                Rs(items: Pool(14056, 14034, 14097), randomPickOne: true)),
            Ev(Ids.E5, LuckKind.Good, 2, "大日子", "难得的一天：吃得好、睡得香、心里踏实。",
                Rs(buffs: Buffs(90, 302810))),                              // 从容避难+休养生息（§6.1 原生）

            // ———— §8.5 好运·传说 L1~L3 等权 ————
            // L1 【选项】神秘补给箱：三选一（原生选项 UI）。顶层不发奖励，各选项自带 Items。
            // 压缩饼干=2101「90压缩饼干」（spec §5.2 体感表对 G1 同称"压缩饼干"；食品大包裹=11003）
            Ev(Ids.L1, LuckKind.Good, 3, "神秘补给箱", "军绿色箱子，封条完好。上面写着你只能带走一层。",
                Rs(choices: new List<Choice>
                {
                    new() { Label = "食物层", Reward = Rs(items: Items((2101, 8), (11003, 2))) },
                    new() { Label = "药品层", Reward = Rs(items: Items((2401, 3), (2405, 3), (2916, 2))) },
                    new() { Label = "材料层", Reward = Rs(items: Items((20362, 30), (20373, 15))) },
                })),
            // L2 无人机残骸：机器人模块 随机×1
            Ev(Ids.L2, LuckKind.Good, 3, "无人机残骸", "军用运输机在这里掉了一整个货盘。",
                Rs(items: Pool(27004, 27007, 27010), randomPickOne: true)),
            // L3 传说之日：命运眷顾（同 E2 规则）+ 医疗包×2 + 半扇猪肋排×1
            Ev(Ids.L3, LuckKind.Good, 3, "传说之日", "所有好事一起发生了。概率上说不通，但生活就是这样。",
                Rs(items: Items((2401, 2), (2319, 1)), permBuffByTier: true)),

            // ———— §8.6 霉运·轻度 B1~B6 等权（buff 一律引用克隆行新 Id（BuffBlueprints 99280 段）或 §6.1 原生行）————
            Ev(Ids.B1, LuckKind.Bad, 0, "蚊子战争", "昨晚房里有一只蚊子。你输了。",
                Rs(buffs: Buffs(99282))),   // 蚊虫叮咬（克隆11002：精力上限−5，12h）
            Ev(Ids.B2, LuckKind.Bad, 0, "落枕", "睡姿没问题，枕头有问题。",
                Rs(buffs: Buffs(11001))),   // 肌肉酸痛（原生）：精力上限−5，12h
            Ev(Ids.B3, LuckKind.Bad, 0, "隔夜菜", "你明明闻过了，它闻起来是好的。",
                Rs(buffs: Buffs(40030))),   // 轻度腹泻（原生）：进食饱腹效果−30%，24h
            Ev(Ids.B4, LuckKind.Bad, 0, "扭到脚", "台阶还是那个台阶，脚不是那只脚了。",
                Rs(buffs: Buffs(99283))),   // 扭伤（克隆40060：移速−15%，12h）
            Ev(Ids.B5, LuckKind.Bad, 0, "吃太快", "好久没吃过热饭了，你没等它凉。",
                Rs(buffs: Buffs(1102))),    // 胃痛（原生）：每3h心态−3、生命−2，24h
            Ev(Ids.B6, LuckKind.Bad, 0, "又是一夜", "数了四千只羊之后，你放弃了。",
                Rs(buffs: Buffs(99284))),   // 睡不着（克隆11008：精力获得效率−30%，24h）

            // ———— §8.7 霉运·中度 M1~M5 等权 ————
            // M1 老鼠的牙：随机1台【电器】耐久−15%（资格=有电器，§5.4）
            Ev(Ids.M1, LuckKind.Bad, 1, "老鼠的牙", "老鼠咬穿了电线包皮。它没事，冰箱有事。",
                Rs(op: SpecialOp.DurabilityPercentRandom, opParam: 15), Eligibility.HasAppliance),
            Ev(Ids.M2, LuckKind.Bad, 1, "换季感冒", "打了三个喷嚏之后，你知道完蛋了。",
                Rs(buffs: Buffs(1116, 1110))),   // 风寒+发烧（原生）
            // M3 冰雹：全屋家具耐久−5%（资格=有家具）
            Ev(Ids.M3, LuckKind.Bad, 1, "冰雹", "冰雹砸在屋顶上，像有人在楼上跳。",
                Rs(op: SpecialOp.DurabilityPercentAll, opParam: 5), Eligibility.HasFurnitureAtLeast1),
            // M4 变质：背包生鲜随机腐坏 1~2 件（资格=有生鲜）
            Ev(Ids.M4, LuckKind.Bad, 1, "变质", "你打开罐头，罐头先看了你一眼。",
                Rs(op: SpecialOp.RotFood, opParam: 2), Eligibility.HasFreshFood),
            Ev(Ids.M5, LuckKind.Bad, 1, "野蘑菇", "你发誓那本图鉴上是这么画的。",
                Rs(buffs: Buffs(1105))),    // 中毒（原生）

            // ———— §8.8 霉运·重度 H1~H4 等权 ————
            // H1 心结【占永久上限】替补=H4：克隆24112 新 Id 99280（心态/精力获得效率各−0.5%，永久）
            Ev(Ids.H1, LuckKind.Bad, 2, "心结", "半夜醒来，你想起了末日前的家。",
                Rs(buffs: Buffs(99280))),
            // H2 管道爆裂：3~5台家具耐久−25%（资格=有家具；OpParam 编码见 EncodeDurabilityRange 注）
            Ev(Ids.H2, LuckKind.Bad, 2, "管道爆裂", "水管在墙里闷响了一夜，早上墙鼓了包。",
                Rs(op: SpecialOp.DurabilityPercentRange, opParam: EncodeDurabilityRange(3, 5, 25)),
                Eligibility.HasFurnitureAtLeast1),
            // H3 【选项】贼影（Task 10 接线定案，DESIGN.md §10）：顶层 StealParts 被 Choices 短路
            // （RewardSpec.RunsTopLevelOp=false，代码不执行）；零件去留按分支声明式结算——
            // 追=AddBuff 1111（受伤流血，BuffTargetType/BuffTargetID=1/0）："追回"=零件本就未失、
            //   不另发物品（50/50 表达不了：RandomGroup 只做物品池随机，取证 DESIGN §10；
            //   若发 ItemChange×10 = 点追净 +10 可刷，Controller 裁定 2026-11-12 否决）；
            // 算了=ItemCost 20362×10（零件只在点"算了"时离开背包）。
            // 资格=铁制零件≥10（§5.4：否则整事件视为无资格）。【不占永久上限】
            Ev(Ids.H3, LuckKind.Bad, 2, "贼影", "地上有陌生的脚印，你的零件箱轻了。",
                Rs(op: SpecialOp.StealParts, opParam: 10, choices: new List<Choice>
                {
                    new() { Label = "追上去", Reward = Rs(buffs: Buffs(1111)) },  // 受伤流血（原生）
                    new() { Label = "算了", Reward = Rs(itemCosts: Items((20362, 10))) }, // 失去铁制零件×10
                }), Eligibility.HasParts10),
            Ev(Ids.H4, LuckKind.Bad, 2, "重病", "不是感冒。这次不是感冒。",
                Rs(buffs: Buffs(1112))),    // 重度感染（原生）：每1h生命−4，24h

            // ———— §8.9 霉运·灾厄 D1~D3 等权（档1/2 不出现；受永久负面上限约束）————
            // D1 代价【占永久上限】替补=D2：随机1项基础属性永久−10（×损失倍率）。
            // AllowPermanentNegative=false 时的行运低潮替代路径（按倍率选Ⅱ~Ⅴ）归 Task 9（RewardMath.PickSlump）。
            Ev(Ids.D1, LuckKind.Bad, 3, "代价", "世界收走了它借给你的东西。",
                Rs(op: SpecialOp.AttrPermRandom, opParam: 10)),
            // D2 雷击：2~4台家具耐久−40%（不摧毁；可重复出现，无独立次数上限，§5.4）
            Ev(Ids.D2, LuckKind.Bad, 3, "雷击", "雷劈中了避雷针，也劈中了旁边的它。",
                Rs(op: SpecialOp.DurabilityPercentRange, opParam: EncodeDurabilityRange(2, 4, 40)),
                Eligibility.HasFurnitureAtLeast1),
            // D3 低语【占永久上限】替补=H2：心结（克隆24112 新 Id 99280）+ 失眠阴影（克隆80007 新 Id 99281，
            // 精力上限−20，永久）——两个均为永久 debuff，但整事件只计 1 次永久名额（§5.4）
            Ev(Ids.D3, LuckKind.Bad, 3, "低语", "你在井底听见有人叫你的名字。之后的日子不太对劲。",
                Rs(buffs: Buffs(99280, 99281))),
        };
    }

    // ———————————————————————————————— 构造小助手 ————————————————————————————————

    private static EventDef Ev(int id, LuckKind kind, int rarity, string title, string dialogue,
        RewardSpec reward = null, Eligibility elig = Eligibility.None, int weight = 1) => new()
    {
        Id = id,
        Key = KeyOf(id),
        Kind = kind,
        Rarity = rarity,
        Title = title,
        Dialogue = dialogue,
        Icon = "ds_" + KeyOf(id) + ".webp",   // spec §8 配图命名 v2（4:3 横版 WebP，Task 11 IconDrop 落盘通道）
        Reward = reward ?? Rs(),
        Elig = elig,
        Weight = weight,
    };

    /// <summary>Id → Key（n1..n5/g1..g8/r1..r6/e1..e5/l1..l3/b1..b6/m1..m5/h1..h4/d1..d3）：
    /// 与 brief ID 分配一一对应，测试 Events_KeysFollowSectionAllocation 钉死。</summary>
    private static string KeyOf(int id)
    {
        var (prefix, start) = id switch
        {
            >= 99200 and < 99205 => ('n', 99200),
            >= 99205 and < 99213 => ('g', 99205),
            >= 99213 and < 99219 => ('r', 99213),
            >= 99219 and < 99224 => ('e', 99219),
            >= 99224 and < 99227 => ('l', 99224),
            >= 99227 and < 99233 => ('b', 99227),
            >= 99233 and < 99238 => ('m', 99233),
            >= 99238 and < 99242 => ('h', 99238),
            >= 99242 and < 99245 => ('d', 99242),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "不在 45 事件 Id 段内"),
        };
        return prefix + (id - start + 1).ToString();
    }

    /// <summary>RewardSpec 构造（未给字段一律空列表/None——下游可免空判）。</summary>
    private static RewardSpec Rs(
        List<ItemGain> items = null, List<int> buffs = null, List<AttrChange> attrs = null,
        SpecialOp op = SpecialOp.None, double opParam = 0, List<Choice> choices = null,
        bool permBuffByTier = false, bool randomPickOne = false,
        List<ItemGain> itemCosts = null) => new()
    {
        Items = items ?? new List<ItemGain>(),
        Buffs = buffs ?? new List<int>(),
        Attrs = attrs ?? new List<AttrChange>(),
        Op = op,
        OpParam = opParam,
        Choices = choices ?? new List<Choice>(),
        PermBuffByTier = permBuffByTier,
        RandomPickOne = randomPickOne,
        ItemCosts = itemCosts ?? new List<ItemGain>(),
    };

    private static List<ItemGain> Items(params (int id, int count)[] gains) =>
        new(Array.ConvertAll(gains, g => new ItemGain { ItemId = g.id, Count = g.count }));

    private static List<ItemGain> Pool(params int[] candidateIds) =>
        new(Array.ConvertAll(candidateIds, id => new ItemGain { ItemId = id, Count = 1 }));

    private static List<int> Buffs(params int[] ids) => new(ids);

    private static List<AttrChange> Attrs(params AttrChange[] changes) => new(changes);

    private static AttrChange Attr(int attrId, double delta) => new() { AttrId = attrId, Delta = delta };
}
