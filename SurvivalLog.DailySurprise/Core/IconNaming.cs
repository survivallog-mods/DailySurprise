using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SurvivalLog.DailySurprise;

/// <summary>Icon 命名纯函数（Task 11）：45 事件配图的命名契约与 Art\ 目录（EmbeddedResource
/// 通配 Art\*.webp 的物理面）对账。命名链三段一致（规格 v2：4:3 横版 1536×1152 WebP q90，
/// 对齐官方原生事件图）：
/// 事件 Key（g1）→ 裸文件名（ds_g1.webp，<see cref="EventDef.Icon"/>）→ 表 Icon 字段
/// （T18 原生约定 Assets/RuntimeAssets/Texture/Icon/ds_g1——ResolveIconDir 剥前缀转
/// ../../Res/Icon/ds_g1 + 扩展名自动重试；<see cref="InjectorMapping.IconFieldOf"/> 合成）→
/// 内嵌资源名（SurvivalLog.DailySurprise.Art.ds_g1.webp，csproj RootNamespace+目录路径，
/// EndlessGuard Resources.eg_arrow_tower.png 同源规则）→ 落盘文件（ds_g1.webp）。
/// 消费方：Game/Art/IconDrop（ResourcePrefix 扫资源、ResourceNameOf 反解文件名）、
/// IconNamingTests（契约钉死）、发版闸门 release.ps1（MissingResources 差集校验）。</summary>
public static class IconNaming
{
    /// <summary>内嵌资源名前缀（csproj EmbeddedResource Include="Art/**" + RootNamespace）。</summary>
    public const string ResourcePrefix = "SurvivalLog.DailySurprise.Art.";

    private static readonly Regex IconFileRegex = new("^ds_[a-z0-9]+\\.webp$", RegexOptions.Compiled);
    private static readonly Regex IconPathRegex = new("^\\.\\./\\.\\./Res/Icon/ds_[a-z0-9]+\\.webp$", RegexOptions.Compiled);
    private static readonly Regex NativeIconPathRegex = new("^Assets/RuntimeAssets/Texture/Icon/ds_[a-z0-9]+$", RegexOptions.Compiled);

    /// <summary>裸文件名 → 内嵌资源名（IconDrop 扫描名单与 Art\ 目录文件的对账桥）。</summary>
    public static string ResourceNameOf(string fileName) => ResourcePrefix + fileName;

    /// <summary>资源名 → 裸文件名；非法形态（前缀/扩展名不符）返回 null。</summary>
    public static string FileNameOf(string resourceName)
    {
        if (string.IsNullOrEmpty(resourceName)
            || !resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)
            || !resourceName.EndsWith(".webp", StringComparison.Ordinal))
            return null;
        var file = resourceName.Substring(ResourcePrefix.Length);
        return IconFileRegex.IsMatch(file) ? file : null;
    }

    /// <summary>45 事件期望资源名（目录顺序、按 Key 派生——Icon 字段自身合规性另由
    /// <see cref="IconIssues"/> 钉死，两不相混：期望集永远从 Key 出发）。</summary>
    public static IReadOnlyList<string> ExpectedResourceNames(IEnumerable<EventDef> events) =>
        events.Select(e => ResourcePrefix + "ds_" + e.Key + ".webp").ToList();

    /// <summary>Icon 字段契约校验（45 全量应为空）：裸名形态 ^ds_[a-z0-9]+\.webp$、
    /// 文件名与自身 Key 一致（ds_g1.webp↔Key="g1"）、合成表字段形态（T18 原生约定
    /// ^Assets/RuntimeAssets/Texture/Icon/ds_[a-z0-9]+$；备选开关下为旧面板相对路径形态）、无重复。
    /// 每条问题一行，含 Key+Id 定位。</summary>
    public static IReadOnlyList<string> IconIssues(IEnumerable<EventDef> events)
    {
        var issues = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (e == null) { issues.Add("事件定义为 null"); continue; }
            var where = e.Key + "(" + e.Id + ")";
            if (string.IsNullOrEmpty(e.Icon))
            {
                issues.Add(where + ": Icon 为空");
                continue;
            }
            if (!IconFileRegex.IsMatch(e.Icon))
                issues.Add(where + ": Icon \"" + e.Icon + "\" 不匹配 ^ds_[a-z0-9]+\\.webp$");
            var expected = "ds_" + e.Key + ".webp";
            if (e.Icon != expected)
                issues.Add(where + ": Icon \"" + e.Icon + "\" 与 Key 不一致（应为 " + expected + "）");
            var path = InjectorMapping.IconFieldOf(e.Icon);
            var ok = InjectorMapping.UseLegacyIconPath ? IconPathRegex.IsMatch(path) : NativeIconPathRegex.IsMatch(path);
            if (!ok)
                issues.Add(where + ": 合成表字段 \"" + path + "\" 不匹配当前注入格式（"
                    + (InjectorMapping.UseLegacyIconPath ? "^../../Res/Icon/ds_x.webp$" : "^Assets/RuntimeAssets/Texture/Icon/ds_x$")
                    + "）");
            if (!seen.Add(e.Icon))
                issues.Add(where + ": Icon 重复 \"" + e.Icon + "\"");
        }
        return issues;
    }

    /// <summary>Art\ 资源名单对账——只报"越集"（不报缺集，缺图=正式图到位前的预期态）：
    /// 形态非法（前缀/扩展名/文件名正则不符）或不在 45 事件期望集内（拼错 Key、多余文件）。</summary>
    public static IReadOnlyList<string> StrayResourceIssues(
        IEnumerable<EventDef> events, IEnumerable<string> actualResourceNames)
    {
        var expected = new HashSet<string>(ExpectedResourceNames(events), StringComparer.Ordinal);
        var issues = new List<string>();
        foreach (var name in actualResourceNames)
        {
            var file = FileNameOf(name);
            if (file == null)
            {
                issues.Add("资源名形态非法 \"" + name + "\"（应为 " + ResourcePrefix + "ds_<key>.webp）");
                continue;
            }
            if (!expected.Contains(name))
                issues.Add("Art\\ 越集文件 \"" + file + "\"（不在 45 事件 Key 集内，改名或删除）");
        }
        return issues;
    }

    /// <summary>差集=期望资源名 − 实际名单（目录顺序）。缺图=正式图到位前的预期态
    /// （IconNamingTests 只报告不判红；发版闸门 release.ps1 判空——45 张齐方可发版）。</summary>
    public static IReadOnlyList<string> MissingResources(
        IEnumerable<EventDef> events, IEnumerable<string> actualResourceNames)
    {
        var actual = new HashSet<string>(actualResourceNames ?? Array.Empty<string>(), StringComparer.Ordinal);
        return ExpectedResourceNames(events).Where(n => !actual.Contains(n)).ToList();
    }
}
