using System;
using SurvivalLog.ModShared;

namespace SurvivalLog.DailySurprise;

/// <summary>事件资格查询（EventDef.Eligibility → 游戏状态；spec §5.4 资格过滤）。
/// **失败方向=无资格**（全部 try/catch，异常/不可达 → false）：
/// - 判"无资格"只影响事件候选集——霉运层全空会降层到轻度（B1~B6 恒有资格），玩家少挨点，安全；
/// - 判"有资格"则可能对空屋放耐久事件空转（执行层无事可做），不安全。故取前者。
/// 查询失败写节流 trace\elig.txt 供排查（如进档空窗期管理器未建，属正常噪声）。
/// gamekb 取证（2026-11-05_26454866 dump.cs）：
/// - AgentManager.GetAllFurnitures() dump.cs:379587；FurnitureDurabilityComponent dump.cs:374068；
///   组件直达 AgentTools.GetAgentComponent&lt;T&gt;(BaseAgent)（EndlessGuard/CabinetEverywhere 在役模式）；
/// - 电器标记 Config_Furniture.IsElectrical（bool，dump.cs:36922）+ ElectricalType（dump.cs:36924），
///   表数据 137 行电器（冷冻柜/冰箱/跑步机…，Config_Furniture.json）；
/// - 生鲜=Config_Item.Life&gt;0（dump.cs:40276）且 It_Rot_Product_Id 非空（dump.cs:40280，可腐坏成
///   15501 腐烂物；表数据 2700 行）；未腐坏=ItemData.TimeLeft&gt;0（dump.cs:424047）；
/// - 铁制零件=Config_Item 20362（表数据「铁制零件」，Life=-1 不可腐）；
///   计数走 ItemManager.GetLeadingRoleItemCount(int)（游戏原生"主角持有数"口径）；
/// - 主角背包 ownerId=Redux State_Data_Player.PlayerId（CabinetEverywhere v13 校准点 T14 在役结论；
///   State_Data_Player dump.cs:151260，dataTree 经 ReduxUISystem.reduxStoreLayer 取）。</summary>
internal static class EligibilityChecker
{
    /// <summary>H3 贼影的零件门槛（spec §5.4：铁制零件≥10 否则整事件无资格）。</summary>
    internal const int PartsItemId = 20362;
    internal const int PartsRequired = 10;

    /// <summary>目录事件的资格判定入口（DailyFlow 的 eligible 谓词按 EventId 反查 Elig 再进来）。</summary>
    internal static bool IsEligible(Eligibility elig)
    {
        try
        {
            switch (elig)
            {
                case Eligibility.None:
                    return true;   // 无门：恒有资格（轻度层 B1~B6 全在此，保证霉运永不落空）
                case Eligibility.HasFurnitureAtLeast1:
                    return HasAnyFurniture();
                case Eligibility.HasFreshFood:
                    return HasFreshFood();
                case Eligibility.HasParts10:
                    return HasParts();
                default:
                    return false;   // 未知门（未来枚举扩员）：安全向=无资格
            }
        }
        catch (Exception e)
        {
            DsTrace.WriteThrottled("elig", elig + " 查询异常（按无资格）: " + e.Message);
            return false;
        }
    }

    /// <summary>HasFurnitureAtLeast1（M1/M3/H2/D2 资格）：屋里有任意门窗/防御设施——
    /// cfgId ∈ Config_TowerDevice 表（打击面同 PunishmentOps.DurabilityPool 口径，2026-11-23
    /// scan 实测：普通家具也挂隐形耐久组件但无可见意义，资格与打击面同收窄）。表不可达=无资格。</summary>
    private static bool HasAnyFurniture()
    {
        var towerDict = BagTable.Cm()?._Config_TowerDevice_Dict;
        if (towerDict == null) return false;
        var list = WorldFurnitures();
        if (list == null) return false;
        var scanned = 0;
        foreach (var f in list)
        {
            if (f == null) continue;
            if (++scanned > 1000) break;
            try { if (towerDict.ContainsKey(f.AgentConfigId)) return true; }
            catch { }
        }
        return false;
    }

    /// <summary>HasFreshFood（M4 变质）：主角背包存在可腐坏的食物（Config_Item.Life&gt;0 且
    /// It_Rot_Product_Id 非空）且尚未腐坏（ItemData.TimeLeft&gt;0）。</summary>
    private static bool HasFreshFood()
    {
        var im = World()?._ItemManager;
        if (im == null) return false;
        var cm = BagTable.Cm();
        if (cm == null) return false;

        var owner = PlayerBagOwnerId();
        if (owner <= 0) return false;   // 背包 owner 不可达 → 无资格（安全向）

        var items = im.GetItemDataList(owner);
        if (items == null) return false;
        var scanned = 0;
        foreach (var it in items)
        {
            if (it == null) continue;
            if (++scanned > 2000) break;   // 病态长表保险丝
            try
            {
                var cfg = cm.Get_Config_Item(it.ItemConfigId);
                if (cfg == null || cfg.Life <= 0) continue;
                if (cfg.It_Rot_Product_Id == null || cfg.It_Rot_Product_Id.Count == 0) continue;
                if (it.TimeLeft > 0) return true;   // 食物还在保质期内
            }
            catch (Exception e)
            {
                DsTrace.WriteThrottled("elig", "HasFreshFood 逐件查询异常（跳过该件）: " + e.Message);
            }
        }
        return false;
    }

    /// <summary>HasParts10（H3 贼影）：主角持有铁制零件(20362)≥10（游戏原生主角持有计数）。</summary>
    private static bool HasParts() =>
        World()?._ItemManager?.GetLeadingRoleItemCount(PartsItemId) >= PartsRequired;

    // —— 游戏世界只读访问（EndlessGuard/SaveMgr 同款 BaseSingleton 防护；不可达=null）——

    internal static GameCore.HotUpdate.Battle.Logic.BattleLogicWorld World()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.IsInstanceCreated)
                return null;
            return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.Instance;
        }
        catch { return null; }
    }

    private static Il2CppSystem.Collections.Generic.List<GameCore.HotUpdate.Battle.Logic.Furniture> WorldFurnitures()
    {
        try { return World()?._AgentManager?.GetAllFurnitures(); }
        catch { return null; }
    }

    /// <summary>主角背包 ownerId：Redux dataTree[State_Data_Player].PlayerId
    /// （CabinetEverywhere 校准点 T14：PlayerId=背包 owner；读不到=0 → 无资格）。
    /// Task 8 起 RewardExecutor 发物品同源复用（资格计数与发放同一 owner 口径）。</summary>
    internal static long PlayerBagOwnerId()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated)
                return 0;
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            var dataTree = rus?.reduxStoreLayer?.dataTree;
            if (dataTree == null) return 0;
            return dataTree.TryGetValue(
                       Il2CppInterop.Runtime.Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.State_Data_Player)),
                       out var raw)
                ? raw?.TryCast<GameCore.HotUpdate.ReduxUI.State_Data_Player>()?.PlayerId ?? 0
                : 0;
        }
        catch { return 0; }
    }
}
