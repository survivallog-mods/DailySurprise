using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SurvivalLog.DailySurprise;

/// <summary>待发放奖励挂账条目（spec §5.2 延迟发放路径）：Reason=来源说明（调试/日志用），
/// Spec=奖励声明全形状（含 Choices 嵌套，Task 8 RewardExecutor 消费）。</summary>
public sealed class PendingReward
{
    public RewardSpec Spec;
    public string Reason;
}

/// <summary>分槽状态存取（Task 5；spec §5.1 RollState 持久化）。纯托管 BCL，零 BepInEx/il2cpp 引用。
/// 槽目录由调用方传入完整路径（&lt;plugins-data&gt;\DailySurprise\saves\&lt;槽hash&gt;），文件固定 <see cref="FileName"/>；
/// Save 负责建目录（幂等）。序列化契约（StateStoreTests 钉死）：公开字段（IncludeFields——RollState/RewardSpec
/// 契约全是 public 字段）、忽略 null（嵌套 null 列表不落盘）、枚举转字符串（SpecialOp 可读且防数值漂移）、
/// UTF-8 无 BOM、缩进便于人工排查。原子写：先写 .tmp 再 File.Move(overwrite:true) 替换，崩溃不留半个存档
/// （同款 EndlessGuard DeviceXpStore 实证路径）。损坏容错：JSON 异常或 "null" 字面量 → 全默认值 +
/// hadCorruption=true（调用方决定是否上报）；缺文件 → 默认值且不算损坏（首启正常态）。</summary>
public static class StateStore
{
    public const string FileName = "state.json";

    private static readonly JsonSerializerOptions s_json = new()
    {
        IncludeFields = true,                                        // 契约类型全是 public 字段，STJ 默认只认属性
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // RewardSpec 嵌套 null 列表不落盘
        Converters = { new JsonStringEnumConverter() },              // SpecialOp 等枚举转字符串
        WriteIndented = true,                                        // 崩档可人工检视
    };

    /// <summary>原子写：建目录 → 序列化到 state.json.tmp（UTF-8 无 BOM，先落盘）→ File.Move 原子替换。
    /// 连续 Save 同槽即整体覆盖（旧值不残留），tmp 被 Move 消费不留中间产物。</summary>
    public static void Save(RollState state, string slotDir)
    {
        Directory.CreateDirectory(slotDir);
        var target = Path.Combine(slotDir, FileName);
        var tmp = target + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(fs, state, s_json);   // 流式 UTF-8 写出，天然无 BOM
            fs.Flush(flushToDisk: true);                   // 先落盘再替换，防掉电半写
        }
        File.Move(tmp, target, overwrite: true);
    }

    /// <summary>读槽：缺文件→默认值（hadCorruption=false）；JSON 损坏或 "null" 字面量→默认值（hadCorruption=true）。
    /// 旧版存档缺 PendingRewards 字段→归一为空列表（非 null，下游免空判）。</summary>
    public static RollState Load(string slotDir, out bool hadCorruption)
    {
        hadCorruption = false;
        var path = Path.Combine(slotDir, FileName);
        if (!File.Exists(path))
            return new RollState();

        RollState state;
        try
        {
            state = JsonSerializer.Deserialize<RollState>(File.ReadAllBytes(path), s_json);
        }
        catch (JsonException)
        {
            hadCorruption = true;
            return new RollState();
        }

        if (state == null)   // 合法 JSON 但字面量 null：视同损坏，静默当默认会掩盖写坏事实
        {
            hadCorruption = true;
            return new RollState();
        }

        state.PendingRewards ??= new List<PendingReward>();   // 缺字段旧档/显式 null 归一
        return state;
    }
}
