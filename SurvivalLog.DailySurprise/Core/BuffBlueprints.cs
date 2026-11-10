using System.Collections.Generic;

namespace SurvivalLog.DailySurprise;

/// <summary>buff 蓝图行：Task 6 注入器消费（读 CloneSourceId 官方 Config_Buff 行及其 EffectConfigId
/// 指向的 Config_Effect 行 → 套 FieldOverrides → 以 NewId 写入两表）。
/// FieldOverrides 键=Config_Buff / Config_Effect 字段名（裸名，两表字段名不重叠，可混装一典），
/// 值=object（int/double/string），如 "BuffDuring"（Buff 表）、"BuffInterval"/"BuffMoraleInterval"
/// （Effect 表）、"Name_Local"/"Des_Local"（Buff 表文案换皮）。
/// 官方源行取证：gamekb 2026-11-05 Config_Buff/Config_Effect（见各行注）。</summary>
public sealed class BuffBlueprint
{
    public int NewId;
    public int CloneSourceId;
    public string Name;
    public string Des;
    public Dictionary<string, object> FieldOverrides;
}

/// <summary>14 条 buff 蓝图（spec §6.2 五克隆 + §6.3 九自注入；NewId 99280~99293 顺排）。
/// 全部 Overlay=0（spec §4：时长叠加、强度不变；永久 buff 重复获得=无变化）。
/// 命运眷顾Ⅰ~Ⅴ/行运低潮Ⅱ~Ⅴ 的档位映射（spec §5.3：预设档N→同号变体；仅自定义档按倍率映射）
/// 归 Task 8 RewardMath.PickPermBuff/PickSlump，蓝图只供数据。</summary>
public static class BuffBlueprints
{
    public const int FirstBlueprintId = 99280;

    /// <summary>§6.2 五克隆（99280~99284）+ §6.3 九自注入（命运眷顾Ⅰ~Ⅴ=99285~99289，行运低潮Ⅱ~Ⅴ=99290~99293）。</summary>
    public static readonly IReadOnlyList<BuffBlueprint> All = BuildAll();

    /// <summary>命运眷顾变体 Id，下标即变体号（[1]=Ⅰ … [5]=Ⅴ；[0] 不用）。</summary>
    public static readonly int[] FortuneIds = { 0, 99285, 99286, 99287, 99288, 99289 };

    /// <summary>行运低潮变体 Id，下标即变体号（[2]=Ⅱ … [5]=Ⅴ；[0]/[1] 不用——spec §6.3 无Ⅰ变体）。</summary>
    public static readonly int[] SlumpIds = { 0, 0, 99290, 99291, 99292, 99293 };

    private static IReadOnlyList<BuffBlueprint> BuildAll()
    {
        var list = new List<BuffBlueprint>
        {
            // ———— §6.2 克隆行（复制官方行：机制照抄或修数值，文案换成配合本 mod 剧情）————

            // 源 24112「心结」：照抄 心态/精力获得效率各−0.5%（源 Effect 200024112：BuffMoraleAdd=BuffStaminaAdd=−0.005），
            // 永久（源 BuffDuring=−1）。仅换文案——原文案"失去她"在本 mod 语境穿帮。用途：H1、D3。
            new BuffBlueprint
            {
                NewId = 99280, CloneSourceId = 24112, Name = "心结",
                Des = "半夜想起末日前的事，心里堵得慌：心态与精力获得效率各-0.5%，永久生效",
                FieldOverrides = new Dictionary<string, object>
                {
                    ["Name_Local"] = "心结",
                    ["Des_Local"] = "半夜想起末日前的事，心里堵得慌：心态与精力获得效率各-0.5%，永久生效",
                    ["BuffDuring"] = -1,   // 永久（与源行一致，显式钉死）
                    ["Overlay"] = 0,
                    // Effect 数值显式钉死（Task 6 顺手修①：照抄克隆效果，防 gamekb 源行 200024112 漂移）
                    ["BuffMoraleAdd"] = -0.005,
                    ["BuffStaminaAdd"] = -0.005,
                },
            },

            // 源 80007「骚扰失眠」：照抄 精力上限−20（源 Effect 200080007：BuffStaminaMax=−20），
            // 永久（源 BuffDuring=−1）。仅换文案——原文案"被门外的人骚扰"穿帮。用途：D3 第二个。
            new BuffBlueprint
            {
                NewId = 99281, CloneSourceId = 80007, Name = "失眠阴影",
                Des = "那晚之后你总觉得睡不踏实：精力上限-20，永久生效",
                FieldOverrides = new Dictionary<string, object>
                {
                    ["Name_Local"] = "失眠阴影",
                    ["Des_Local"] = "那晚之后你总觉得睡不踏实：精力上限-20，永久生效",
                    ["BuffDuring"] = -1,   // 永久（与源行一致，显式钉死）
                    ["Overlay"] = 0,
                    // Effect 数值显式钉死（Task 6 顺手修①：防源行 200080007 漂移）
                    ["BuffStaminaMax"] = -20,
                },
            },

            // 源 11002「脖子酸痛」→ 蚊虫叮咬：修数值 精力上限−5、12h——源 Effect 200011002 数值全零
            // （官方"描述写了没做"），补 BuffStaminaMax=−5；时长=源行 43200（12h，其 Des 误写 24 小时）。
            // 用途：B1。
            new BuffBlueprint
            {
                NewId = 99282, CloneSourceId = 11002, Name = "蚊虫叮咬",
                Des = "昨晚房里有一只蚊子，你输了：精力上限-5，持续12小时",
                FieldOverrides = new Dictionary<string, object>
                {
                    ["Name_Local"] = "蚊虫叮咬",
                    ["Des_Local"] = "昨晚房里有一只蚊子，你输了：精力上限-5，持续12小时",
                    ["BuffStaminaMax"] = -5,  // Effect：精力上限-5（源 Effect 全零，本行补上）
                    ["BuffDuring"] = 43200,   // 12h
                    ["Overlay"] = 0,
                },
            },

            // 源 40060「行动不便」→ 扭伤：修数值 移速−15%、12h——源 Effect 200040060 是
            // ConfigName="Buff/BagOverweight"（BuffInterval=3600/FloatValue0=−0.5，强度走标签层不可控）；
            // 改走标准 BuffSpeed（同 11004 精神振奋 移速+30% 的字段），并归零 BagOverweight 残值/周期。
            // 用途：B4。
            new BuffBlueprint
            {
                NewId = 99283, CloneSourceId = 40060, Name = "扭伤",
                Des = "脚踝不对劲：移动速度-15%，持续12小时",
                FieldOverrides = new Dictionary<string, object>
                {
                    ["Name_Local"] = "扭伤",
                    ["Des_Local"] = "脚踝不对劲：移动速度-15%，持续12小时",
                    ["ConfigName"] = "Buff/Buff_Default", // Effect：弃 BagOverweight 标签层机制
                    ["BuffSpeed"] = -0.15,                // Effect：移速-15%（比例小数，§4）
                    ["FloatValue0"] = 0,                  // Effect：清零源行 BagOverweight 残值
                    ["BuffInterval"] = 0,                 // Effect：无周期结算
                    ["BuffDuring"] = 43200,               // 12h
                    ["Overlay"] = 0,
                },
            },

            // 源 11008「长夜未眠」→ 睡不着：修数值 精力获得效率−30%、24h——官方机制反写成福利
            // （源 Effect 200011008：BuffStaminaDel=−0.3 = 精力消耗减少30%），对冲为 Del=0 + Add=−0.3
            // （获得效率扣减，同 24112 心结的 Add 字段族）；顺带 IsGood 翻正为 debuff。用途：B6。
            new BuffBlueprint
            {
                NewId = 99284, CloneSourceId = 11008, Name = "睡不着",
                Des = "数了四千只羊也没用：精力获得效率-30%，持续24小时",
                FieldOverrides = new Dictionary<string, object>
                {
                    ["Name_Local"] = "睡不着",
                    ["Des_Local"] = "数了四千只羊也没用：精力获得效率-30%，持续24小时",
                    ["IsGood"] = false,          // Buff：源行 IsGood=true（福利色），debuff 翻正
                    ["BuffStaminaDel"] = 0,      // Effect：对冲源行 -0.3 的"消耗减少"福利
                    ["BuffStaminaAdd"] = -0.3,   // Effect：精力获得效率-30%
                    ["BuffDuring"] = 86400,      // 24h
                    ["Overlay"] = 0,
                },
            },

            // ———— §6.3 自注入行（全新行；克隆源仅为行形态起点，数值全由 FieldOverrides 定义）————

            // 命运眷顾Ⅰ~Ⅴ：每2h心态 +20/15/10/7/5，永久。克隆源=90「从容避难」
            // （源 Effect 200000090：BuffInterval=7200/BuffMoraleInterval=2，即"每2h心态+2"——机制同构，
            // 改 Interval 数值即可）。授予：预设档N→同号变体N（§5.3）。
            Fortune(99285, 1, 20, "梦里有个人把一切都教给了你：每2小时心态+20，永久生效"),
            Fortune(99286, 2, 15, "梦里有个人把一切都教给了你：每2小时心态+15，永久生效"),
            Fortune(99287, 3, 10, "梦里有个人把一切都教给了你：每2小时心态+10，永久生效"),
            Fortune(99288, 4, 7, "梦里有个人把一切都教给了你：每2小时心态+7，永久生效"),
            Fortune(99289, 5, 5, "梦里有个人把一切都教给了你：每2小时心态+5，永久生效"),

            // 行运低潮Ⅱ~Ⅴ：五维获得效率各 −1/−2/−3/−4%，永久。克隆源=24112「心结」
            // （两维 Add 扩成五维 Add）。授予：仅 D1 的"永久负面关闭"替代路径与自定义档（§6.3/§5.3）。
            Slump(99290, 2, "诸事不顺的一天开始了：五维获得效率各-1%，永久生效"),
            Slump(99291, 3, "诸事不顺的一天开始了：五维获得效率各-2%，永久生效"),
            Slump(99292, 4, "诸事不顺的一天开始了：五维获得效率各-3%，永久生效"),
            Slump(99293, 5, "诸事不顺的一天开始了：五维获得效率各-4%，永久生效"),
        };
        return list;
    }

    private static BuffBlueprint Fortune(int newId, int variant, int moralePer2h, string des) => new()
    {
        NewId = newId,
        CloneSourceId = 90,
        Name = "命运眷顾" + Roman(variant),
        Des = des,
        FieldOverrides = new Dictionary<string, object>
        {
            ["Name_Local"] = "命运眷顾" + Roman(variant),
            ["Des_Local"] = des,
            ["BuffDuring"] = -1,                    // 永久（源行 86400，改永久）
            ["Overlay"] = 0,
            ["BuffInterval"] = 7200,                // Effect：每2小时结算（与源行一致，显式钉死）
            ["BuffMoraleInterval"] = moralePer2h,   // Effect：每周期心态+20/15/10/7/5
        },
    };

    private static BuffBlueprint Slump(int newId, int variant, string des)
    {
        var add = -0.01 * (variant - 1);   // Ⅱ=−0.01、Ⅲ=−0.02、Ⅳ=−0.03、Ⅴ=−0.04（比例小数，§4）
        return new BuffBlueprint
        {
            NewId = newId,
            CloneSourceId = 24112,
            Name = "行运低潮" + Roman(variant),
            Des = des,
            FieldOverrides = new Dictionary<string, object>
            {
                ["Name_Local"] = "行运低潮" + Roman(variant),
                ["Des_Local"] = des,
                ["BuffDuring"] = -1,   // 永久（与源行一致，显式钉死）
                ["Overlay"] = 0,
                // Effect：五维获得效率扣减（§4 *Add=比例小数；源行只有 Morale/Stamina 两维，扩成五维）
                ["BuffSatietyAdd"] = add,
                ["BuffMoraleAdd"] = add,
                ["BuffStaminaAdd"] = add,
                ["BuffHealthAdd"] = add,
                ["BuffVitalityAdd"] = add,
            },
        };
    }

    private static string Roman(int v) => v switch
    {
        1 => "Ⅰ", 2 => "Ⅱ", 3 => "Ⅲ", 4 => "Ⅳ", 5 => "Ⅴ", _ => "?",
    };
}
