using System;
using System.Collections.Generic;
using System.Reflection;
using SurvivalLog.ModShared;

namespace SurvivalLog.DailySurprise;

/// <summary>EnsureInjected 的结果：当前进程内本 mod 自注入行的在册行数（实际计数，非硬编码——
/// green 返回时=全量：50 事件行（45 事件 + 5 选项子事件）/ 50 RewardSet（45 基础+5 选项）/
/// 5 RandomGroup（T18 随机池）/ 14 buff / 14 effect）。
/// null（EnsureInjected 返回 null）≠ 空报告：null=表未就绪/未全绿（调用方下轮再试）。</summary>
internal sealed class InjectionReport
{
    public int Events;
    public int RewardSets;
    public int RandomGroups;
    public int BuffRows;
    public int EffectRows;
}

/// <summary>五表全量注入器（Task 2 spike 形态的全量化，数据源=EventCatalog/BuffBlueprints，
/// 行内容由 <see cref="InjectorMapping"/> 纯函数产出，本类只做"字典→对象属性"搬运与克隆拷贝）：
/// Config_Event 50 行（45 事件 + 5 选项子事件，Task 10）+ Config_RewardSet 50 行 +
/// Config_RandomGroup 5 行（T18 RandomPickOne 池）+ Config_Buff 14 行 + Config_Effect 14 行。
/// 时机/幂等/哨兵照抄 EndlessGuard ArrowTower 定案模式：
/// - Cm() 单例就绪 ≠ dict 就绪（2026-10-24 实锤），任一字典 null=装表中，下轮再试；
/// - 哨兵=全量行在册 + 每表关键行形态探针（官方 ConfigManager.Initialize 重建字典后哨兵转红，
///   ticker 自动重放；全字段无条件拍平保证重放幂等）；
/// - 烘焙指纹（T18 难度热更）：RewardSet/RandomGroup 行内容按注入时的 (tier, rewardMult) 定格
///   （物品件数/PermBuff 变体）；cfg 难度或自定义奖励倍率变化 → 指纹失配 → 下轮 ticker 强制全量
///   重放（全字段拍平重烘焙，E2/L3 变体随之更换）——无需 SettingChanged 订阅，绿态轮询自带；
/// - 行级幂等：行在则复用对象、全字段无条件拍平（半途异常重试不被判重挡死）；
/// - 全部 List 字段全新 Il2Cpp 实例逐元素填（官方消费方直读 .Count，null/共享实例有 NRE/Clear
///   连坐风险——ArrowTower 教训）。克隆行（buff/effect）=读源行属性逐项拷贝（列表亦逐元素新实例）
///   → 套 InjectorMapping 覆盖字典 → 新 Id 写入。表是进程内存态，不写任何存档。</summary>
internal static class TableInjector
{
    // —— 本 mod 托管 Id 段（哨兵在册检查 + 报告计数共用；见 InjectorMapping 头注的编号方案）——
    private static readonly int[] EventIds = BuildEventIds();
    private static readonly int[] RewardSetIds = BuildRewardSetIds();
    private static readonly int[] RandomGroupIds = BuildRandomGroupIds();
    private static readonly int[] BuffIds = BuildBuffIds();
    private static readonly int[] EffectIds = BuildEffectIds();

    private static int[] BuildEventIds()
    {
        // 45 父事件（99200~99244）+ 5 选项子事件（99245~99249，Task 10 接线）
        var ids = new int[EventCatalog.Events.Count + EventCatalog.SonEvents.Count];
        var n = 0;
        for (var i = 0; i < EventCatalog.Events.Count; i++) ids[n++] = EventCatalog.Events[i].Id;
        for (var i = 0; i < EventCatalog.SonEvents.Count; i++) ids[n++] = EventCatalog.SonEvents[i].Id;
        return ids;
    }

    private static int[] BuildRewardSetIds()
    {
        var all = InjectorMapping.AllRewardSets();
        var ids = new int[all.Count];
        for (var i = 0; i < ids.Length; i++) ids[i] = all[i].Id;
        return ids;
    }

    private static int[] BuildRandomGroupIds()
    {
        var all = InjectorMapping.RandomGroupRows();
        var ids = new int[all.Count];
        for (var i = 0; i < ids.Length; i++) ids[i] = all[i].Id;
        return ids;
    }

    private static int[] BuildBuffIds()
    {
        var ids = new int[BuffBlueprints.All.Count];
        for (var i = 0; i < ids.Length; i++) ids[i] = BuffBlueprints.All[i].NewId;
        return ids;
    }

    private static int[] BuildEffectIds()
    {
        var ids = new int[BuffBlueprints.All.Count];
        for (var i = 0; i < ids.Length; i++) ids[i] = InjectorMapping.EffectIdOf(BuffBlueprints.All[i]);
        return ids;
    }

    // —— 注入回合（episode）追踪：绿→红迁移（官方重建表/行被清）开新回合并递增计数。
    //    Inject 汇总行带回合号走 WriteThrottled——真重放（新回合=新文案）每回合必留一行，
    //    卡红死循环（同回合同文案）每 50 次才记一次，杜绝 2s/条的 inject.txt 洪水（fix#1 后果链复盘）。——
    private static bool _wasGreen;
    private static int _episode = 1;

    // —— 烘焙指纹（T18）：上次全绿注入时的 (tier, rewardMult)。RewardSet/RandomGroup 行内容
    //    按它定格；当前 cfg 算出的指纹不同（难度热更/自定义倍率改动）→ 判 stale 强制全量重放。
    //    初始 (0,0)≠任何合法产出 → 首轮必注入。元组比较=值语义。 ——
    private static (int Tier, double Mult) _bakedOn;

    /// <summary>当前烘焙参数：cfg 实时读值（Tier 档名/旧数字串兼容解析、Custom 档倍率）经
    /// DifficultyTable.Resolve 归一（非法值回退档 3）。读取异常防御回基准档 (3, 1.0)。</summary>
    private static (int Tier, double Mult) CurrentBake()
    {
        try
        {
            var p = DifficultyTable.Resolve(DsConfig.Tier, DsConfig.CustomOrDefault(), DsConfig.Warn);
            return (p.Tier, p.RewardMult);
        }
        catch { return (3, 1.0); }
    }

    /// <summary>ticker 每步入口：五表就绪且全量行在册（含形态探针）且烘焙参数未变 → 返回报告；
    /// 未就绪/烘焙指纹失配/注入后仍未全绿 → 注入（或重注入）后复查，仍不绿 → null（调用方下轮再试）。</summary>
    internal static InjectionReport EnsureInjected()
    {
        try
        {
            var cm = BagTable.Cm();
            if (cm == null) return null;
            var evDict = cm._Config_Event_Dict;
            var rsDict = cm._Config_RewardSet_Dict;
            var rgDict = cm._Config_RandomGroup_Dict;
            var buffDict = cm._Config_Buff_Dict;
            var effDict = cm._Config_Effect_Dict;
            if (evDict == null || rsDict == null || rgDict == null || buffDict == null || effDict == null)
                return null;   // 装表中，下轮再试（不动 _wasGreen——空窗不算回合切换）

            var bake = CurrentBake();
            var stale = bake != _bakedOn;   // 难度热更：已注入行是旧烘焙 → 强制重放（重烘焙数量/变体）
            var green = !stale && SentinelsGreen(evDict, rsDict, rgDict, buffDict, effDict, bake);
            if (!green && _wasGreen) _episode++;   // 上一轮绿这一轮红 = 官方重建或烘焙热更，新一轮注入开始
            _wasGreen = green;
            if (green) return Report(evDict, rsDict, rgDict, buffDict, effDict);
            Inject(evDict, rsDict, rgDict, buffDict, effDict, _episode, bake);
            if (SentinelsGreen(evDict, rsDict, rgDict, buffDict, effDict, bake))
            {
                _bakedOn = bake;
                return Report(evDict, rsDict, rgDict, buffDict, effDict);
            }
            return null;
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("inject", "EnsureInjected 异常: " + e.Message);
            return null;
        }
    }

    /// <summary>实际在册行数（托管 Id 逐个 ContainsKey 计数——不硬编码，行数口径与哨兵一致）。</summary>
    private static InjectionReport Report(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Event> evDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RewardSet> rsDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RandomGroup> rgDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Buff> buffDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Effect> effDict)
    {
        var r = new InjectionReport();
        foreach (var id in EventIds) if (evDict.ContainsKey(id)) r.Events++;
        foreach (var id in RewardSetIds) if (rsDict.ContainsKey(id)) r.RewardSets++;
        foreach (var id in RandomGroupIds) if (rgDict.ContainsKey(id)) r.RandomGroups++;
        foreach (var id in BuffIds) if (buffDict.ContainsKey(id)) r.BuffRows++;
        foreach (var id in EffectIds) if (effDict.ContainsKey(id)) r.EffectRows++;
        return r;
    }

    // —— 哨兵探针期望（fix#1：全部自 InjectorMapping/目录派生，禁止硬编码行 Id/载荷常量——
    //    曾错把 FirstRewardSetId(99250) 当 G1 行探针，实际 99250=N1 空行、G1=99255，哨兵恒红）。
    //    T18：RewardSet/RandomGroup 探针依赖烘焙参数（件数/变体随难度定格）→ 每次现算（见
    //    SentinelsGreen 内 bake 形参）；无难度依赖的探针仍为 static readonly（启动一次派生）。 ——
    private static readonly EventDef ProbeG1 = FindEvent(EventCatalog.Ids.G1);
    private static readonly EventDef ProbeL1 = FindEvent(EventCatalog.Ids.L1);
    private static readonly Dictionary<string, object> ProbeG1EventRow = InjectorMapping.EventRow(ProbeG1);
    private static readonly int ProbeG1Type = (int)ProbeG1EventRow["Type"];
    private static readonly int ProbeG1RsWired = (int)ProbeG1EventRow["RewardSetId"];        // T19 结果阶段挂法：直发父行 RewardSetId=99255（StartRewardSetId=0）
    private static readonly int ProbeG1RsId = InjectorMapping.BaseRewardSetIdOf(ProbeG1);    // =99255（RewardSet 行编号不受裁定影响）
    private static readonly int ProbeL1Type = (int)InjectorMapping.EventRow(ProbeL1)["Type"];
    private static readonly int ProbeL1ChoseCount = ProbeL1.Reward.Choices.Count;
    private static readonly EventDef ProbeR2 = FindEvent(EventCatalog.Ids.R2);               // RandomGroup 首池探针（99250）
    private static readonly int ProbeR2GroupId = InjectorMapping.FirstRandomGroupId;
    // 子事件行探针（Task 10）：L1 首子事件（99245 食物层）——IsSon/RewardSetId 声明式接线形态；
    // 期望值派生自 InjectorMapping.SonEventRow（99245→选项行 99295 由 InjectorMappingTests 钉死）
    private static readonly SonEvent ProbeSon = EventCatalog.SonEvents[0];
    private static readonly Dictionary<string, object> ProbeSonRow = InjectorMapping.SonEventRow(ProbeSon);
    private static readonly bool ProbeSonIsSon = (bool)ProbeSonRow["IsSon"];
    private static readonly int ProbeSonType = (int)ProbeSonRow["Type"];
    private static readonly int ProbeSonRsId = (int)ProbeSonRow["RewardSetId"];
    // H3「算了」选项行探针（99299）：ItemCost=20362×10 声明式扣件（零件只在点"算了"时离开背包）
    private static readonly EventDef ProbeH3 = FindEvent(EventCatalog.Ids.H3);
    private static readonly (int Id, Dictionary<string, object> Row) ProbeH3GiveUpRow =
        InjectorMapping.RewardSetsOf(ProbeH3)[2];   // [0]=基础行、[1]=追(99298)、[2]=算了(99299)——Choices 序
    private static readonly int ProbeH3GiveUpCostCount =
        ((List<int>)ProbeH3GiveUpRow.Row["ItemCost"]).Count;
    private static readonly int ProbeBuffId = BuffBlueprints.FirstBlueprintId;               // 99280
    private static readonly BuffBlueprint ProbeBuff = BuffBlueprints.All[0];
    private static readonly int ProbeBuffEffectId = InjectorMapping.EffectIdOf(ProbeBuff);
    private static readonly float ProbeEffectMoraleAdd =
        Convert.ToSingle(InjectorMapping.BuffRows(ProbeBuff).Effect["BuffMoraleAdd"]);

    /// <summary>哨兵复查：五表全量 Id 在册 + 每表关键行形态探针（期望值全部派生自 InjectorMapping，
    /// 探针数据源由 InjectorMappingTests 钉死：99250=N1 空行、99255=G1 十件——错位会在映射层测试暴露）。
    /// bake=当前烘焙参数（RewardSet/RandomGroup 探针期望随难度现算——难度热更后旧行形态失配即红）。
    /// 读行字段可抛（interop 病态）→ 按缺行处理触发重放（幂等无害）。</summary>
    private static bool SentinelsGreen(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Event> evDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RewardSet> rsDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RandomGroup> rgDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Buff> buffDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Effect> effDict,
        (int Tier, double Mult) bake)
    {
        try
        {
            foreach (var id in EventIds) if (!evDict.ContainsKey(id)) return false;
            foreach (var id in RewardSetIds) if (!rsDict.ContainsKey(id)) return false;
            foreach (var id in RandomGroupIds) if (!rgDict.ContainsKey(id)) return false;
            foreach (var id in BuffIds) if (!buffDict.ContainsKey(id)) return false;
            foreach (var id in EffectIds) if (!effDict.ContainsKey(id)) return false;

            var g1 = evDict[ProbeG1.Id];
            if (g1.Type != ProbeG1Type || g1.RewardSetId != ProbeG1RsWired) return false;

            var l1 = evDict[ProbeL1.Id];
            if (l1.Type != ProbeL1Type || l1.ChoseName_Local == null
                || l1.ChoseName_Local.Count != ProbeL1ChoseCount) return false;

            var son = evDict[ProbeSon.Id];   // Task 10：子事件行形态（IsSon/RewardSetId 声明式接线）
            if (!son.IsSon || son.Type != ProbeSonType || son.RewardSetId != ProbeSonRsId) return false;

            var rsGiveUp = rsDict[ProbeH3GiveUpRow.Id];   // 「算了」ItemCost 声明式扣件在册
            if (rsGiveUp.ItemCost == null || rsGiveUp.ItemCost.Count != ProbeH3GiveUpCostCount) return false;

            // G1 RewardSet 行探针（期望随烘焙现算：件数随难度定格；Source 恒正式标记）
            var g1RsRow = InjectorMapping.RewardSetRow(ProbeG1.Reward, ProbeG1RsId, bake.Tier, bake.Mult);
            var rsG1 = rsDict[ProbeG1RsId];
            if (rsG1.Source != (string)g1RsRow["Source"] || rsG1.ItemChange == null
                || rsG1.ItemChange.Count != ((List<int>)g1RsRow["ItemChange"]).Count) return false;

            // RandomGroup 首池探针（R2=99250）：IdList 候选数随烘焙参数派生（池成员静态、CountList 动态）
            var r2Row = InjectorMapping.RandomGroupRow(ProbeR2, ProbeR2GroupId, bake.Mult);
            var rg = rgDict[ProbeR2GroupId];
            if (rg.IdList == null || rg.IdList.Count != ((List<int>)r2Row["IdList"]).Count
                || rg.GroupType != (int)r2Row["GroupType"]) return false;

            var bH1 = buffDict[ProbeBuffId];
            if (bH1.EffectConfigId != ProbeBuffEffectId) return false;

            var effH1 = effDict[ProbeBuffEffectId];
            if (effH1.ID != ProbeBuffEffectId
                || Math.Abs(effH1.BuffMoraleAdd - ProbeEffectMoraleAdd) > 1e-6f) return false;
            return true;
        }
        catch { return false; }
    }

    private static EventDef FindEvent(int id)
    {
        foreach (var e in EventCatalog.Events) if (e.Id == id) return e;
        throw new InvalidOperationException("EventCatalog 缺少事件 " + id);
    }

    /// <summary>五表注入（分表 try/catch：一表失败不挡其余；行级失败留哨兵红、下轮重放）。
    /// bake=本次注入的烘焙参数（RewardSet 件数/PermBuff 变体、RandomGroup CountList 按它定格；
    /// EnsureInjected 绿态后记入 _bakedOn 指纹——难度热更时失配触发全量重放）。
    /// 完成后写 trace 汇总行（带 episode 回合号、WriteThrottled 节流——见 EnsureInjected 注）。</summary>
    private static void Inject(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Event> evDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RewardSet> rsDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RandomGroup> rgDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Buff> buffDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Effect> effDict,
        int episode, (int Tier, double Mult) bake)
    {
        int evNew = 0, rsNew = 0, rgNew = 0, bfNew = 0, efNew = 0;

        try
        {
            foreach (var e in EventCatalog.Events)
            {
                var row = GetOrCreate(evDict, e.Id, out var created);
                if (created) evNew++;
                ApplyDict(row, InjectorMapping.EventRow(e));
            }
            // 选项子事件行（Task 10）：父行 ChoseID 指向它们，RewardSetId 声明式发奖
            foreach (var s in EventCatalog.SonEvents)
            {
                var row = GetOrCreate(evDict, s.Id, out var created);
                if (created) evNew++;
                ApplyDict(row, InjectorMapping.SonEventRow(s));
            }
        }
        catch (Exception ex) { DsTrace.WriteThrottled("inject", "Config_Event 注入中断: " + ex.Message); }

        try
        {
            foreach (var (id, fields) in InjectorMapping.AllRewardSets(bake.Tier, bake.Mult))
            {
                var row = GetOrCreate(rsDict, id, out var created);
                if (created) rsNew++;
                ApplyDict(row, fields);
            }
        }
        catch (Exception ex) { DsTrace.WriteThrottled("inject", "Config_RewardSet 注入中断: " + ex.Message); }

        try
        {
            // RandomGroup 池行（T18）：RandomPickOne 的候选与烘焙件数（全字段拍平幂等——
            // 难度热更重放即换 CountList）
            foreach (var (id, fields) in InjectorMapping.RandomGroupRows(bake.Mult))
            {
                var row = GetOrCreate(rgDict, id, out var created);
                if (created) rgNew++;
                ApplyDict(row, fields);
            }
        }
        catch (Exception ex) { DsTrace.WriteThrottled("inject", "Config_RandomGroup 注入中断: " + ex.Message); }

        try
        {
            foreach (var b in BuffBlueprints.All)
            {
                var (buffOv, effOv) = InjectorMapping.BuffRows(b);

                // 克隆源可达性（fix#3：两侧一致 fail fast，不静默半行）：源 buff 行 → 其 EffectConfigId →
                // 源 effect 行（惯例 200000000+buffId，但以源行字段为准不硬编码）；缺任一源=该表本轮
                // 中断、哨兵留红（下轮重放，节流 trace 可见）——绝不以默认值+覆盖字典凑合成行
                if (!buffDict.TryGetValue(b.CloneSourceId, out var srcBuff) || srcBuff == null)
                    throw new InvalidOperationException("克隆源 buff " + b.CloneSourceId + " 不在册（NewId=" + b.NewId + "）");
                var srcEffectId = srcBuff.EffectConfigId;
                if (!effDict.TryGetValue(srcEffectId, out var srcEffect) || srcEffect == null)
                    throw new InvalidOperationException("克隆源 effect " + srcEffectId + "（buff " + b.CloneSourceId
                        + " 的 EffectConfigId）不在册（NewId=" + b.NewId + "）");

                var effRow = GetOrCreate(effDict, (int)effOv["ID"], out var effCreated);
                if (effCreated) efNew++;
                CopyRow(srcEffect, effRow);
                ApplyDict(effRow, effOv);

                var buffRow = GetOrCreate(buffDict, b.NewId, out var buffCreated);
                if (buffCreated) bfNew++;
                CopyRow(srcBuff, buffRow);
                ApplyDict(buffRow, buffOv);
            }
        }
        catch (Exception ex) { DsTrace.WriteThrottled("inject", "Config_Buff/Config_Effect 注入中断: " + ex.Message); }

        var report = Report(evDict, rsDict, rgDict, buffDict, effDict);
        DsTrace.WriteThrottled("inject", "[episode " + episode + "] 全量注入: events=" + report.Events + "/" + EventIds.Length
            + " rewardSets=" + report.RewardSets + "/" + RewardSetIds.Length
            + " randomGroups=" + report.RandomGroups + "/" + RandomGroupIds.Length
            + " buffs=" + report.BuffRows + "/" + BuffIds.Length
            + " effects=" + report.EffectRows + "/" + EffectIds.Length
            + "（烘焙 tier=" + bake.Tier + " mult=" + bake.Mult.ToString("0.###")
            + "，本趟新建 ev" + evNew + "/rs" + rsNew + "/rg" + rgNew + "/bf" + bfNew + "/ef" + efNew + "，其余拍平）");
    }

    /// <summary>行级幂等取行：在则复用（created=false），缺/null 则新建入册（interop 字典无共同
    /// 泛型接口，四表各写一份具体重载，逻辑逐字一致）。</summary>
    private static GameCore.HotUpdate.Config_Event GetOrCreate(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Event> dict, int id, out bool created)
    {
        if (dict.TryGetValue(id, out var row) && row != null) { created = false; return row; }
        row = new GameCore.HotUpdate.Config_Event();
        dict[id] = row;
        created = true;
        return row;
    }

    private static GameCore.HotUpdate.Config_RewardSet GetOrCreate(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RewardSet> dict, int id, out bool created)
    {
        if (dict.TryGetValue(id, out var row) && row != null) { created = false; return row; }
        row = new GameCore.HotUpdate.Config_RewardSet();
        dict[id] = row;
        created = true;
        return row;
    }

    private static GameCore.HotUpdate.Config_RandomGroup GetOrCreate(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RandomGroup> dict, int id, out bool created)
    {
        if (dict.TryGetValue(id, out var row) && row != null) { created = false; return row; }
        row = new GameCore.HotUpdate.Config_RandomGroup();
        dict[id] = row;
        created = true;
        return row;
    }

    private static GameCore.HotUpdate.Config_Buff GetOrCreate(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Buff> dict, int id, out bool created)
    {
        if (dict.TryGetValue(id, out var row) && row != null) { created = false; return row; }
        row = new GameCore.HotUpdate.Config_Buff();
        dict[id] = row;
        created = true;
        return row;
    }

    private static GameCore.HotUpdate.Config_Effect GetOrCreate(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Effect> dict, int id, out bool created)
    {
        if (dict.TryGetValue(id, out var row) && row != null) { created = false; return row; }
        row = new GameCore.HotUpdate.Config_Effect();
        dict[id] = row;
        created = true;
        return row;
    }

    // —— 字典→对象属性搬运（唯一写入通道；Event/RewardSet 全字段拍平与 buff/effect 覆盖共用）——

    /// <summary>覆盖字典写入：键=属性名（InjectorMapping 契约），值=int/double/string/List&lt;int&gt;/
    /// List&lt;string&gt;，按目标属性类型转换（int 字段 ← int、float 字段 ← int/double 等）。</summary>
    private static void ApplyDict(object row, IReadOnlyDictionary<string, object> fields)
    {
        var t = row.GetType();
        foreach (var kv in fields)
        {
            var p = t.GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite)
                throw new InvalidOperationException("属性缺失或只读: " + t.Name + "." + kv.Key);
            SetValue(row, p, kv.Value);
        }
    }

    private static void SetValue(object row, PropertyInfo p, object v)
    {
        var pt = p.PropertyType;
        if (pt == typeof(int)) p.SetValue(row, Convert.ToInt32(v));
        else if (pt == typeof(float)) p.SetValue(row, Convert.ToSingle(v));
        else if (pt == typeof(bool)) p.SetValue(row, Convert.ToBoolean(v));
        else if (pt == typeof(string)) p.SetValue(row, (string)v);
        else if (pt == typeof(Il2CppSystem.Collections.Generic.List<int>))
            p.SetValue(row, L(((List<int>)v).ToArray()));
        else if (pt == typeof(Il2CppSystem.Collections.Generic.List<string>))
            p.SetValue(row, LS(((List<string>)v).ToArray()));
        else throw new InvalidOperationException("不支持的属性类型 " + pt.FullName + "（" + row.GetType().Name + "." + p.Name + "）");
    }

    /// <summary>克隆拷贝：源行全部可写属性 → 新行（标量直拷；List 逐元素全新实例，源 null→空列表，
    /// 禁共享实例——ArrowTower 教训；MainKey 只读自动跳过）。</summary>
    private static void CopyRow(object src, object dst)
    {
        foreach (var p in src.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanWrite) continue;
            object val;
            try { val = p.GetValue(src); }
            catch { continue; }   // 病态读：保持新行缺省，不中断整行克隆
            var pt = p.PropertyType;
            if (pt == typeof(Il2CppSystem.Collections.Generic.List<int>))
            {
                var srcList = val as Il2CppSystem.Collections.Generic.List<int>;
                var list = new Il2CppSystem.Collections.Generic.List<int>();
                if (srcList != null) for (var i = 0; i < srcList.Count; i++) list.Add(srcList[i]);
                p.SetValue(dst, list);
            }
            else if (pt == typeof(Il2CppSystem.Collections.Generic.List<string>))
            {
                var srcList = val as Il2CppSystem.Collections.Generic.List<string>;
                var list = new Il2CppSystem.Collections.Generic.List<string>();
                if (srcList != null) for (var i = 0; i < srcList.Count; i++) list.Add(srcList[i]);
                p.SetValue(dst, list);
            }
            else p.SetValue(dst, val);
        }
    }

    /// <summary>Il2Cpp int 列表构造（每字段全新实例——官方消费方直读 .Count，null/共享实例有连坐风险）。</summary>
    private static Il2CppSystem.Collections.Generic.List<int> L(params int[] v)
    {
        var l = new Il2CppSystem.Collections.Generic.List<int>();
        if (v != null) foreach (var x in v) l.Add(x);
        return l;
    }

    /// <summary>Il2Cpp string 列表构造（ChoseName(_Local) 等 List&lt;string&gt; 字段用）。</summary>
    private static Il2CppSystem.Collections.Generic.List<string> LS(params string[] v)
    {
        var l = new Il2CppSystem.Collections.Generic.List<string>();
        if (v != null) foreach (var x in v) l.Add(x);
        return l;
    }
}
