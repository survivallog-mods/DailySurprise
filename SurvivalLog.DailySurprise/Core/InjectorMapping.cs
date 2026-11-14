using System;
using System.Collections.Generic;
using System.Linq;

namespace SurvivalLog.DailySurprise;

/// <summary>注入映射纯函数（Task 6）：EventDef/RewardSpec/BuffBlueprint → 表行字段字典。
/// 字典键=Config_* 属性名（裸名），值=int/double/string/List&lt;int&gt;/List&lt;string&gt;；
/// IL2CPP 写入层（Game/TableInjector）只做"字典→对象属性"搬运，不掺数据决策。
/// 字段名与形态权威=gamekb 2026-11-05_26454866 dump.cs：
/// - Config_Event：35790 起 67 可写属性（ChoseName/ChoseName_Local=List&lt;string&gt;、ChoseID=List&lt;int&gt;、
///   StartRewardSetId=RewardSetId=int，35833/35838/35844 行）；
/// - Config_RewardSet：46003 起 44 可写属性；AttriKeyChange/AttriValueChange=平行 List&lt;int&gt;
///   （键=AttrName 枚举 1..5、值=整数增量可负——原生行 6011=[1]/[8]、6013=[1,3]/[-15,-15] 实证）；
/// - Config_Buff：28302 起 24 可写属性（BuffDuring=float:28321、Overlay=int:28322、
///   EffectConfigId=int:28328；原生行 EffectConfigId=200000000+buffId 惯例）；
/// - Config_Effect：33687 起（BuffInterval=float:33718、BuffMoraleInterval=float:33720、
///   BuffStaminaMax=int:33730、*Add/*Del=float:33733-33742、BuffSpeed=float:33765、
///   FloatValue0=float:33782）。
/// ID 段（Task 6 brief；Task 10 增子事件段；T18 增 RandomGroup 段）：RewardSet 基础行=99250+目录序
/// （99250..99294）、选项行 99295..99299（L1×3+H3×2 目录序顺排，Task 10 起声明式发奖——子事件行
/// RewardSetId 指向）、子事件行 99245..99249（事件段 99200..99244 顺延；与 RewardSet 99250..99299
/// 数值重叠不冲突——不同表）、Effect 行=99290+蓝图序（99290..99303，Effect 独立表与 buff 99280..99293
/// 数值重叠不冲突——不同表）、RandomGroup 行=99250+池序（99250..99254，独立表——原生表 Id 101~9169+
/// 73001，99250 段空闲；与 RewardSet 同号不同表，Effect/buff 重叠同先例）。
/// RewardSet 行内容口径（T18 奖励原生上卡：全部载荷声明式，注入时按当前难度烘焙；T19 挂法修正）：
/// - 直发行 RewardSetId=基础行 Id（T19 结果阶段挂法：原生 Result 型（Type=2）1260 行零 StartRewardSetId
///   挂载、831 行挂 RewardSetId——直发型（Type=2/PopUpType=1/非子）676 行中 327 行此挂法（10200=6049 等）；
///   T18 的 Start 挂法是原生零先例形态（反汇编：Start 奖励四入口 ShouldDeferStartReward/
///   CreateDynamicEventArgs/IsStartRewardTask/RunStartRewardOnTriggerExit 均 Type==3 门）且实机强制触发链
///   NRE，废弃归 0；RewardSetId 挂法走 FSM DoResult 结算→SetResultUI 清单→确认发放）；
///   选项父行 RewardSetId=0（顶层不发，各选项子事件 RewardSetId 声明式）。
/// - 固定 Items → ItemChange（RewardEncoding：itemId 重复=件数，数量已 ×rewardMult 烘焙）；
///   RandomPickOne（R2/E1/E3/E4/L2）→ RandomGroup 声明式（组行 GroupType=1 物品池，原生 39 行同构）：
///   RewardSet.RandomGroupId=组行、RandomGroupCount=1（随机抽 1 条目）、组行 CountList=各候选烘焙件数
///   （CountList=每 id 件数，原生行 220/7001 实证；与旧代码路径"随机选 1 条×倍率"等价）；
///   Buffs → AddBuff+BuffTargetType/BuffTargetID=1/0（原生行 6014/6106 实证配对）；
///   PermBuffByTier（E2/L3）→ 注入时按当前档 RewardMath.PickPermBuff 烘焙变体 Id 进 AddBuff；
///   Attrs → AttriKeyChange/AttriValueChange（整数增量；小数增量抛错走代码路径）；
///   Op 系（耐久/腐坏/偷窃/永久属性）→ 不承载（RewardSetManager 无对应声明式执行器，代码路径保留）。
/// - 选项行（declarative，Task 10）：子事件 RewardSetId 声明式发奖——Items→ItemChange、
///   Buffs→AddBuff+BuffTargetType/BuffTargetID=1/0、ItemCosts→ItemCost（itemId 重复=件数，原生行 68252 同编码）。</summary>
public static class InjectorMapping
{
    // —— ID 段与标记常量（Game 层哨兵/计数与测试共用）——
    public const int FirstRewardSetId = 99250;
    public const int OptionRewardSetFirstId = 99250 + 45;   // 99295：L1×3 + H3×2 = 99295..99299
    public const int FirstEffectId = 99290;
    public const int FirstRandomGroupId = 99250;            // 99250..99254：R2/E1/E3/E4/L2 五池（独立表段）
    public const string SourceTag = "DailySurprise";         // 正式命名（前 DailySurprise_Spike，Task 2 spike 遗留）

    /// <summary>表 Icon 字段原生约定前缀（T18）：Reducer_Web_EventChoice.ResolveIconDir（dump.cs:183295）
    /// 剥 "Assets/RuntimeAssets/Texture/" 转 WebUI 相对路径 ../../Res/&lt;子路径&gt;，webui-core.js:836
    /// RES_IMG_EXTS 自动重试扩展名（.webp 优先）命中 IconDrop 落盘文件；无扩展名=原生行主流形态
    /// （Config_Event 2101 行中 2056 行无扩展名）。完整值=IconPrefix+裸名去扩展名（见 IconFieldOf）。</summary>
    public const string IconPrefix = "Assets/RuntimeAssets/Texture/Icon/";

    /// <summary>备选格式（T18 实机预案）：ResolveIconDir 行为超出推断（长前缀仍不显示）时切
    /// UseLegacyIconPath=true——Icon 直接填面板 CSS 相对路径（EventChoice.html 自身样式即用
    /// ../../Res/ 前缀，面板侧可解析）。编译期小开关（static readonly 而非 const：防常量传播
    /// 产生不可达代码警告——测试零警告红线），不暴露 cfg。</summary>
    internal static readonly bool UseLegacyIconPath = false;
    internal const string LegacyIconPrefix = "../../Res/Icon/";

    /// <summary>裸文件名（ds_g1.webp）→ 表 Icon 字段值：原生约定=IconPrefix+去扩展名；
    /// 备选=LegacyIconPrefix+原样（带 .webp）。EventRow/SonEventRow/IconNaming 校验共用。</summary>
    public static string IconFieldOf(string bareIcon)
    {
        if (string.IsNullOrEmpty(bareIcon)) return UseLegacyIconPath ? LegacyIconPrefix : IconPrefix;
        if (!UseLegacyIconPath)
        {
            var dot = bareIcon.LastIndexOf('.');
            return IconPrefix + (dot > 0 ? bareIcon[..dot] : bareIcon);
        }
        return LegacyIconPrefix + bareIcon;
    }

    /// <summary>事件基础 RewardSet 行 Id = 99250 + 目录序（N1=99250 … D3=99294）。</summary>
    public static int BaseRewardSetIdOf(EventDef e)
    {
        var idx = EventCatalog.EventsIndexOf(e);
        if (idx < 0) throw new ArgumentOutOfRangeException(nameof(e), e.Id, "非 EventCatalog 目录事件");
        return FirstRewardSetId + idx;
    }

    /// <summary>Effect 行 Id = 99290 +（NewId−99280）：蓝图序独立编号（Effect 表内 99290..99303）。</summary>
    public static int EffectIdOf(BuffBlueprint b)
    {
        if (b == null) throw new ArgumentNullException(nameof(b));
        return FirstEffectId + (b.NewId - BuffBlueprints.FirstBlueprintId);
    }

    // ———————————————— Event 行（父行 + 子事件行） ————————————————

    /// <summary>选项 RewardSet 行 Id 分配走查（单一编号源，Task 10）：按目录序自 99295 连续编号，
    /// 返回 父事件 Id → 选项行 Id 列表（与 Choices 同序）。RewardSetsOf/AllRewardSets 的选项行
    /// 与 SonEventRow 的 RewardSetId 都消费本走查——错位=实机点选发错奖，测试钉死一致性。</summary>
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<int>> OptionIdsByParent = BuildOptionIds();

    private static IReadOnlyDictionary<int, IReadOnlyList<int>> BuildOptionIds()
    {
        var map = new Dictionary<int, IReadOnlyList<int>>();
        var next = OptionRewardSetFirstId;
        for (var i = 0; i < EventCatalog.Events.Count; i++)
        {
            var ev = EventCatalog.Events[i];
            if (ev.Reward is not { Choices.Count: > 0 }) continue;
            var ids = new List<int>();
            foreach (var _ in ev.Reward.Choices) ids.Add(next++);
            map[ev.Id] = ids;
        }
        return map;
    }

    /// <summary>父事件的子事件 Id 列表（EventCatalog.SonEvents 过滤，与 Choices 同序）；
    /// 数量与 Choices 不一致=目录/子事件表失配 → fail fast（防 ChoseName/ChoseID 错位接线）。</summary>
    private static List<int> SonIdsOf(int parentId)
    {
        var sons = new List<int>();
        foreach (var s in EventCatalog.SonEvents)
            if (s.ParentId == parentId) sons.Add(s.Id);
        foreach (var ev in EventCatalog.Events)
            if (ev.Id == parentId && ev.Reward is { Choices.Count: > 0 } && sons.Count != ev.Reward.Choices.Count)
                throw new InvalidOperationException("SonEvents 与 " + parentId + " 的 Choices 数量失配（接线错位风险）");
        return sons;
    }

    /// <summary>EventDef → Config_Event 全字段字典（67 键，形态样式抄原生行 10011/103）。
    /// 选项事件（Choices 非空）用 103 选择样式并接线子事件（Task 10，原生 103/10010 形态）：
    /// ChoseID=子事件 Id 列表、Default=末选项子事件（倒计时归零走它）。
    /// 直发行 RewardSetId=基础 RewardSet 行 Id（T19 结果阶段挂法：原生 Result 型（Type=2）1260 行零
    /// StartRewardSetId、831 行挂 RewardSetId——直发型（Type=2/PopUpType=1/非子）327 行同构（10200=6049
    /// 等）；Start 奖励原生机制全套 Type==3 门（Task 型专属），Type=2 挂 Start 是零先例形态且实机
    /// 强制触发链 NRE（T18），废弃归 0）；选项父行 RewardSetId=0（顶层不发，选项效果归子事件）。</summary>
    public static Dictionary<string, object> EventRow(EventDef e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        var choice = e.Reward is { Choices.Count: > 0 };
        var labels = choice ? e.Reward.Choices.Select(c => c.Label).ToList() : new List<string>();
        var sons = choice ? SonIdsOf(e.Id) : new List<int>();
        return EventRowCore(
            id: e.Id, name: e.Title, des: e.Dialogue, icon: IconFieldOf(e.Icon),
            demoTwoMode: choice ? 3 : 2, type: choice ? 1 : 2, popUpType: choice ? 3 : 1,
            dayCd: choice ? 3 : 1, canRepeat: true, countdown: choice ? 60 : 0, isSon: false,
            labels: labels, choseIds: sons, def: sons.Count > 0 ? sons[sons.Count - 1] : 0,
            startRewardSetId: 0, rewardSetId: choice ? 0 : BaseRewardSetIdOf(e));
    }

    /// <summary>SonEvent → Config_Event 全字段字典（67 键；模板=原生 10011/104：Type=2 Result /
    /// PopUpType=1 / IsSon=true / DemoTwoMode 随父(103 样式=3) / DayCD=0 / CanRepeat=false）。
    /// Name=「父标题-选项名」（原生「暴雨侵袭-用毛巾堵漏」同构）；EventDes/EventDesTaskEnd=结果台词
    /// 同文（10011 模板）；RewardSetId=对应选项 RewardSet 行（声明式发奖，Task 10 Controller 裁定）；
    /// 图沿用父事件图（原生 10011 同款复用）。</summary>
    public static Dictionary<string, object> SonEventRow(SonEvent s)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        EventDef parent = null;
        foreach (var e in EventCatalog.Events)
            if (e.Id == s.ParentId) { parent = e; break; }
        if (parent == null || parent.Reward is not { Choices.Count: > 0 })
            throw new ArgumentOutOfRangeException(nameof(s), s.ParentId, "子事件 " + s.Id + " 的父事件不在目录或非选项事件");

        var index = 0;   // 父 Choices 内的序（SonEvents 顺序契约：同 Choices 序）
        foreach (var x in EventCatalog.SonEvents)
        {
            if (x.ParentId != s.ParentId) continue;
            if (x.Id == s.Id) break;
            index++;
        }
        if (index >= parent.Reward.Choices.Count)
            throw new InvalidOperationException("子事件 " + s.Id + " 超出父 " + s.ParentId + " 的 Choices 数量");
        var label = parent.Reward.Choices[index].Label;
        var name = parent.Title + "-" + label;

        return EventRowCore(
            id: s.Id, name: name, des: s.Line, icon: IconFieldOf(parent.Icon),
            demoTwoMode: 3, type: 2, popUpType: 1,
            dayCd: 0, canRepeat: false, countdown: 0, isSon: true,
            labels: new List<string>(), choseIds: new List<int>(), def: 0,
            startRewardSetId: 0, rewardSetId: OptionIdsByParent[s.ParentId][index]);
    }

    /// <summary>Config_Event 行公共骨架（67 键全拍平）：父行/子事件行共用，差异字段经参数注入。
    /// 共有口径：EndlessMode=1 / LogType=1 / ShowType=1 / DataMin=1 / DataMax=999 / TimeMin=1 /
    /// TimeMax=24 / TasteRequire=-1 / OKDes=我知道了 / 条件门清零（Task 2 定案）/
    /// 发奖统一走 RewardSetId（T19 结果阶段挂法：直发父行与子事件行同构，原生 Result 型 831 行
    /// 同款）、StartRewardSetId 恒 0（T18 Start 挂法废弃——强制触发链 NRE）。</summary>
    private static Dictionary<string, object> EventRowCore(
        int id, string name, string des, string icon,
        int demoTwoMode, int type, int popUpType, int dayCd, bool canRepeat, int countdown, bool isSon,
        List<string> labels, List<int> choseIds, int def, int startRewardSetId, int rewardSetId)
    {
        return new Dictionary<string, object>
        {
            ["ID"] = id,
            ["Name"] = name,                       // 裸中文直写（GetLocalText 键 miss 原样透传，Task 2 实锤）
            ["Name_Local"] = name,
            ["PlayerSelectId"] = new List<int>(),  // 不限角色（模板 10011/103 均未设）
            ["DemoTwoMode"] = demoTwoMode,         // 10011=2、103=3（子事件随父）
            ["EndlessMode"] = 1,                   // 两模板均 1
            ["Type"] = type,                       // 1=Choice(103)、2=Result(10011)
            ["EventLogID"] = 0,
            ["LogType"] = 1,                       // 两模板均 1（DynamicEventLogType.Normal）
            ["ShowType"] = 1,
            ["PopUpType"] = popUpType,             // 103=3（选项卡）、10011=1（结果卡）
            ["FurnitureID"] = new List<int>(),
            ["KeepTime"] = 0,
            ["IsSon"] = isSon,                     // 父行 false（目录事件均独立）；子事件行 true
            ["EventDes"] = des,
            ["EventDes_Local"] = des,
            ["Info"] = "", ["Info_Local"] = "",
            ["DoneInfo"] = "", ["DoneInfo_Local"] = "",
            ["EventDesTaskEnd"] = des,             // 10011 模板：TaskEnd 文案=EventDes 同文
            ["EventDesTaskEnd_Local"] = des,
            ["Icon"] = icon,
            ["IconEnd"] = icon,                    // 10011/103 均与 Icon 同值
            ["IconRole"] = new List<string>(),
            ["DataMin"] = 1,                       // 两模板均 1/999
            ["DataMax"] = 999,
            ["DayRate"] = 0f,
            ["TimeMin"] = 1,                       // 两模板均 1/24
            ["TimeMax"] = 24,
            ["CanRepeat"] = canRepeat,             // 父行 true（两模板均）；子事件行 false（104 形态）
            ["DayCD"] = dayCd,                     // 103 DayCD=3；10011 DayCD=1；子事件 0（104 形态）
            ["HourCD"] = 0,
            ["StartThink"] = "", ["StartThink_Local"] = "",
            ["ChoseName"] = new List<string>(labels),
            ["ChoseName_Local"] = new List<string>(labels),
            ["ChoseID"] = new List<int>(choseIds), // Task 10：选项事件=子事件 Id 列表（与 Choices 同序）
            ["Default"] = def,                     // 选项事件=末选项子事件（原生 103/10010 同构）；其余 0
            ["Countdown"] = countdown,             // 103 Countdown=60（倒计时归零走 Default）
            ["ActionRequire"] = new List<int>(), ["ActionTime"] = new List<int>(),
            ["ItemRequire"] = new List<int>(), ["ItemTime"] = new List<int>(),
            ["TaskTime"] = 0,                      // 刻意偏离模板：任务时限不设（Task 2 定案）
            ["OKDes"] = "我知道了",                // 两模板 OKDes_Local 同文
            ["OKDes_Local"] = "我知道了",
            ["OKDoneDes"] = "", ["OKDoneDes_Local"] = "",
            ["DoneThink"] = "", ["DoneThink_Local"] = "",
            ["TriggerFmod"] = "", ["EndFmod"] = "", // 不抄模板音效（与台词无关）
            ["ConditionSetId"] = 0,                 // 刻意清零门控（Task 2 定案：强制路径只添噪声）
            ["NeedConditionSetId"] = 0,
            ["TaskConditionSetId"] = 0,
            ["StartRewardSetId"] = startRewardSetId, // 恒 0（T19：T18 的 Start 挂法在强制触发链解析
                                                    // RewardSet 抛 NRE，废弃；原生 Result 型 831 行同款归 0）
            ["RewardSetId"] = rewardSetId,          // T19 结果阶段挂法：直发父行=基础行 Id（FSM DoResult
                                                    // 结算+SetResultUI 清单+确认发放）；子事件行=选项行（声明式）
            ["ExpiredRewardSetId"] = 0,
            ["SMSContactId"] = 0,
            ["SMSContent"] = "", ["SMSContent_Local"] = "",
            ["TasteRequire"] = -1,                  // 两模板均 -1（无口味门槛）
            ["GroupKey"] = "", ["GroupDayCD"] = 0,
            ["EndingLine"] = 0, ["MaxTimes"] = 0,
        };
    }

    // ———————————————— RewardSet 行 ————————————————

    /// <summary>单事件全部 RewardSet 行：基础行（1 行）+ 每选项 1 行（L1×3、H3×2，声明式）。
    /// 选项行 Id 消费 <see cref="OptionIdsByParent"/> 单一走查（与 AllRewardSets 结果一致）。
    /// tier/rewardMult=T18 烘焙参数（物品件数/PermBuff 变体按当前难度注入时定格，热更重注入随换）。</summary>
    public static IReadOnlyList<(int Id, Dictionary<string, object> Row)> RewardSetsOf(
        EventDef e, int tier = 3, double rewardMult = 1.0)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        for (var i = 0; i < EventCatalog.Events.Count; i++)
        {
            var ev = EventCatalog.Events[i];
            if (ev.Id != e.Id) continue;
            var rows = new List<(int, Dictionary<string, object>)>
            {
                (FirstRewardSetId + i, RewardSetRow(ev.Reward, FirstRewardSetId + i, tier, rewardMult,
                    randomGroupId: RandomGroupIdOf(ev))),
            };
            if (ev.Reward is { Choices.Count: > 0 })
            {
                var ids = OptionIdsByParent[ev.Id];
                for (var j = 0; j < ev.Reward.Choices.Count; j++)
                    rows.Add((ids[j], RewardSetRow(ev.Reward.Choices[j].Reward, ids[j], tier, rewardMult,
                        declarative: true)));
            }
            return rows;
        }
        throw new ArgumentOutOfRangeException(nameof(e), e.Id, "非 EventCatalog 目录事件");
    }

    /// <summary>全目录 RewardSet 行（45 基础 + 5 选项 = 50 行，Id=99250..99299 连续不重；
    /// 选项行声明式，Task 10）。tier/rewardMult=T18 烘焙参数。</summary>
    public static IReadOnlyList<(int Id, Dictionary<string, object> Row)> AllRewardSets(
        int tier = 3, double rewardMult = 1.0)
    {
        var rows = new List<(int, Dictionary<string, object>)>();
        for (var i = 0; i < EventCatalog.Events.Count; i++)
        {
            var ev = EventCatalog.Events[i];
            var baseId = FirstRewardSetId + i;
            rows.Add((baseId, RewardSetRow(ev.Reward, baseId, tier, rewardMult,
                randomGroupId: RandomGroupIdOf(ev))));
            if (ev.Reward is not { Choices.Count: > 0 }) continue;
            var ids = OptionIdsByParent[ev.Id];
            for (var j = 0; j < ev.Reward.Choices.Count; j++)
                rows.Add((ids[j], RewardSetRow(ev.Reward.Choices[j].Reward, ids[j], tier, rewardMult,
                    declarative: true)));
        }
        return rows;
    }

    /// <summary>RandomPickOne 池序走查（单一编号源）：R2/E1/E3/E4/L2 按目录序 → 组行 Id
    /// 99250..99254；非池事件=0（无组）。</summary>
    public static int RandomGroupIdOf(EventDef e)
    {
        if (e == null) return 0;
        var pool = 0;
        foreach (var ev in EventCatalog.Events)
        {
            if (ev.Reward is not { RandomPickOne: true }) continue;
            if (ev.Id == e.Id) return FirstRandomGroupId + pool;
            pool++;
        }
        return 0;
    }

    /// <summary>RewardSpec → Config_RewardSet 全字段字典（44 键）。内容口径见类注（T18 全载荷声明式）；
    /// 未承载字段=显式安全缺省（空列表/0，官方消费方不读出未定义态——Task 2 定案）。
    /// tier/rewardMult=烘焙参数（Items 件数 ×mult 下限 1；PermBuffByTier 变体按档定格）；
    /// randomGroupId=RandomPickOne 池的组行 Id（RewardSetsOf/AllRewardSets 走查注入；直调缺省 0=无组）；
    /// declarative=true（选项行，Task 10）：ItemCosts→ItemCost 由原生声明式执行。</summary>
    public static Dictionary<string, object> RewardSetRow(RewardSpec reward, int id,
        int tier = 3, double rewardMult = 1.0, bool declarative = false, int randomGroupId = 0)
    {
        if (reward == null) throw new ArgumentNullException(nameof(reward));
        List<int> items;
        var randomGroupIdOut = 0;
        var randomGroupCount = 0;
        if (reward.RandomPickOne)
        {
            // 随机×1 声明式（T18）：RewardSet 只挂组引用，条目与烘焙件数在组行（RandomGroupRows）
            items = new List<int>();
            randomGroupIdOut = randomGroupId;
            randomGroupCount = randomGroupId > 0 ? 1 : 0;
        }
        else
        {
            items = RewardEncoding.ItemChangeOf(reward, rewardMult);   // itemId 重复=件数（行 6420 语义，数量已烘焙）
        }
        var itemCost = declarative ? RewardEncoding.ItemCostOf(reward) : new List<int>();
        var addBuff = new List<int>(reward.Buffs ?? new List<int>());
        if (reward.PermBuffByTier)
            addBuff.Add(RewardMath.PickPermBuff(tier, rewardMult));   // E2/L3 变体注入时烘焙（热更重注入随换）
        var buffTargetType = new List<int>();        // 原生行 6014/6106 实证：每 buff 配 BuffTargetType=1/BuffTargetID=0
        var buffTargetId = new List<int>();
        for (var i = 0; i < addBuff.Count; i++) { buffTargetType.Add(1); buffTargetId.Add(0); }
        var keys = new List<int>();
        var values = new List<int>();
        foreach (var a in reward.Attrs)
        {
            if (a == null) continue;
            if (a.Delta != Math.Floor(a.Delta))
                throw new InvalidOperationException(
                    "AttriValueChange=List<int> 只承载整数增量（原生行 6011/6013 编码）；小数/永久属性增量走 Task 8/9 代码路径。attr=" + a.AttrId);
            keys.Add(a.AttrId);
            values.Add((int)a.Delta);
        }
        return new Dictionary<string, object>
        {
            ["ID"] = id,
            ["Source"] = SourceTag,
            ["AttriKeyChange"] = keys,
            ["AttriValueChange"] = values,
            ["SurvivalPoint"] = 0, ["BeliefPoint"] = 0,
            ["ItemChange"] = items,
            ["ItemPackage"] = new List<int>(), ["ItemCost"] = itemCost,
            ["ClearFurnitureBag"] = new List<int>(),
            ["FurniturelD"] = new List<int>(), ["FurnitureHP"] = new List<int>(),
            ["BuffTargetType"] = buffTargetType, ["BuffTargetID"] = buffTargetId,
            ["AddBuff"] = addBuff, ["DelBuff"] = new List<int>(),
            ["ChangeStatus"] = new List<int>(),
            ["AddTag"] = new List<string>(), ["DelTag"] = new List<string>(),
            ["ForbidTag"] = new List<string>(), ["ForbidTime"] = new List<int>(),
            ["AddSymbol"] = new List<string>(), ["DelSymbol"] = new List<string>(),
            ["UnlockSystem"] = new List<int>(), ["ShowProductionList"] = new List<int>(),
            ["ShowRecipeList"] = new List<int>(),
            ["UnlockProductionCraft"] = new List<int>(), ["UnlockCookingCraft"] = new List<int>(),
            ["GetAchievemenet"] = 0,
            ["ActiveEvent"] = new List<int>(), ["ActiveStory"] = new List<int>(),
            ["MoneyAdd"] = 0,
            ["ShowGuidanceFurnitureID"] = new List<int>(), ["HideGuidanceFurnitureID"] = new List<int>(),
            ["RandomGroupId"] = randomGroupIdOut, ["RandomGroupCount"] = randomGroupCount,
            ["ExposureValue"] = 0, ["EventExp"] = 0, ["ExpType"] = "",
            ["ClearFurnitureBagSatiety"] = 0,
            ["MetNpcIds"] = new List<int>(), ["RemoveEvent"] = new List<int>(),
            ["CounterKey"] = "", ["CounterAdd"] = 0,
        };
    }

    // ———————————————— RandomGroup 行（T18 随机池声明式） ————————————————

    /// <summary>全部 RandomGroup 行（5 池：R2/E1/E3/E4/L2 目录序 → 99250..99254）。
    /// 编码取证（gamekb 2026-11-12 tables）：GroupType=1 物品池；IdList=候选 itemId（重复=权重，
    /// 原生行 107）；CountList=平行每 id 件数（原生行 220 [..,5,..]、7001 全 5 实证）；
    /// DiscoverLvList=探索等级门槛（本 mod 池无门槛=空，原生行 106 同款空）。
    /// rewardMult 烘焙进 CountList（候选 Count=1 → round(mult) 下限 1——与旧代码路径
    /// "PickRandomOne 选中一条再 ScaleCount" 等价语义）。</summary>
    public static IReadOnlyList<(int Id, Dictionary<string, object> Row)> RandomGroupRows(double rewardMult = 1.0)
    {
        var rows = new List<(int, Dictionary<string, object>)>();
        for (var i = 0; i < EventCatalog.Events.Count; i++)
        {
            var ev = EventCatalog.Events[i];
            if (ev.Reward is not { RandomPickOne: true }) continue;
            var id = FirstRandomGroupId + rows.Count;
            rows.Add((id, RandomGroupRow(ev, id, rewardMult)));
        }
        return rows;
    }

    /// <summary>EventDef → Config_RandomGroup 全字段字典（7 可写属性，dump.cs:45795 起：
    /// ID/RandomGroupName(_Local)/GroupType/IdList/CountList/DiscoverLvList）。
    /// 名称中文直写（键与 _Local 同值——GetLocalText 键 miss 原样透传惯例，防官方键穿帮）。</summary>
    public static Dictionary<string, object> RandomGroupRow(EventDef e, int id, double rewardMult = 1.0)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        if (e.Reward is not { RandomPickOne: true })
            throw new ArgumentOutOfRangeException(nameof(e), e.Id, "非 RandomPickOne 池事件");
        var ids = new List<int>();
        var counts = new List<int>();
        foreach (var g in e.Reward.Items)
        {
            if (g == null || g.Count <= 0) continue;
            ids.Add(g.ItemId);
            counts.Add(RewardMath.ScaleCount(g.Count, rewardMult));   // 候选基准 1 件 → 烘焙件数
        }
        var name = "喜从天降·" + e.Title + "随机池";
        return new Dictionary<string, object>
        {
            ["ID"] = id,
            ["RandomGroupName"] = name,
            ["RandomGroupName_Local"] = name,
            ["GroupType"] = 1,                        // 物品池（原生 331/382 行同型）
            ["IdList"] = ids,
            ["CountList"] = counts,
            ["DiscoverLvList"] = new List<int>(),     // 无探索等级门槛
        };
    }

    // ———————————————— Buff/Effect 蓝图拆表 ————————————————

    /// <summary>Config_Buff 可写属性全集（dump.cs:28302-28328 逐字）：FieldOverrides 键命中此处→写 buff 行。</summary>
    private static readonly HashSet<string> BuffFieldNames = new()
    {
        "ID", "Name", "Name_Local", "IsGood", "BuffDisplayType", "BuffType", "IsHide", "Mask",
        "Des", "Des_Local", "TagList", "EventTag", "LogPriority", "BuffText", "BuffText_Local",
        "BuffRandomText", "BuffDuring", "Overlay", "IsAutonomy", "InitLayerCount", "AddLayerCount",
        "MaxLayerCount", "ShowConfigId", "EffectConfigId",
    };

    /// <summary>Config_Effect 属性名（dump.cs:33687 起属性集，覆盖蓝图 FieldOverrides 用到的字段族）：
    /// 命中此处→写 effect 行。</summary>
    private static readonly HashSet<string> EffectFieldNames = new()
    {
        "ID", "ConfigName", "ExposureValue", "PonitEnd",
        "ItemSatietyStart", "ItemMoraleStart", "ItemStaminaStart", "ItemHealthStart", "ItemVitalityStart",
        "ItemAttrInterval", "ItemSatietyInterval", "ItemMoraleInterval", "ItemStaminaInterval",
        "ItemHealthInterval", "ItemVitalityInterval", "ItemEndBuff01", "ItemEndBuff02",
        "ItemSatietyEnd", "ItemMoraleEnd", "ItemStaminaEnd", "ItemHealthEnd", "ItemVitalityEnd",
        "EBuffTargetType", "EBuffTargetID", "EAddBuff", "EDelBuff", "EDisableFurniture",
        "BuffInterval", "BuffSatietyInterval", "BuffMoraleInterval", "BuffStaminaInterval",
        "BuffHealthInterval", "BuffVitalityInterval", "BuffAction", "BuffActionProbability",
        "BuffEndBuff01", "BuffEndBuff02",
        "BuffSatietyMax", "BuffMoraleMax", "BuffStaminaMax", "BuffHealthMax", "BuffVitalityMax",
        "BuffSatietyAdd", "BuffMoraleAdd", "BuffStaminaAdd", "BuffHealthAdd", "BuffVitalityAdd",
        "BuffSatietyDel", "BuffMoraleDel", "BuffStaminaDel", "BuffHealthDel", "BuffVitalityDel",
        "BuffSpeed", "BuffSatietyStart", "BuffMoraleStart", "BuffStaminaStart", "BuffHealthStart",
        "BuffVitalityStart", "BuffSatietyEnd", "BuffMoraleEnd", "BuffStaminaEnd", "BuffHealthEnd",
        "BuffVitalityEnd",
        "IntValue0", "IntValue1", "IntValue2", "IntValue3", "IntValue4", "IntValue5",
        "FloatValue0", "FloatValue1", "FloatValue2", "FloatValue3", "FloatValue4", "FloatValue5",
        "StrValue0", "StrValue1", "StrValue2", "StrValue3", "StrValue4", "StrValue5",
    };

    /// <summary>BuffBlueprint →（buff 行覆盖字典, effect 行覆盖字典）：FieldOverrides 按字段名归属拆表
    /// （键在 <see cref="BuffFieldNames"/>→buff；在 <see cref="EffectFieldNames"/>→effect；都不在→抛错
    /// fail fast 防拼写静默串表）。另补三枚映射钉：buff.ID=NewId、buff.EffectConfigId=新 effect Id
    /// （不沿用源行 200000000+id 段，指向 99290 段本 mod 行）、effect.ID=同值；
    /// 以及 Name/Des 中文直写（键与 _Local 同值——Task 2 实锤 GetLocalText 键 miss 原样透传，
    /// 防克隆残留 "Buff_Name_11002" 官方键命中官方文案穿帮）。
    /// 覆盖字典之外的字段由 IL2CPP 层从 CloneSourceId 源行逐属性拷贝（见 Game/TableInjector）。</summary>
    public static (Dictionary<string, object> Buff, Dictionary<string, object> Effect) BuffRows(BuffBlueprint b)
    {
        if (b == null) throw new ArgumentNullException(nameof(b));
        var buff = new Dictionary<string, object>();
        var effect = new Dictionary<string, object>();
        foreach (var kv in b.FieldOverrides)
        {
            if (BuffFieldNames.Contains(kv.Key)) buff[kv.Key] = kv.Value;
            else if (EffectFieldNames.Contains(kv.Key)) effect[kv.Key] = kv.Value;
            else throw new InvalidOperationException(
                "BuffBlueprint(" + b.NewId + ") FieldOverrides 字段 \"" + kv.Key
                + "\" 既不属于 Config_Buff 也不属于 Config_Effect 属性集（dump.cs 28302/33687 起）");
        }
        var effectId = EffectIdOf(b);
        buff["ID"] = b.NewId;
        buff["Name"] = b.Name;
        buff["Des"] = b.Des;
        buff["EffectConfigId"] = effectId;
        effect["ID"] = effectId;
        return (buff, effect);
    }
}
