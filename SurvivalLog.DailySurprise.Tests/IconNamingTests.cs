using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SurvivalLog.DailySurprise;
using Xunit;
using Xunit.Abstractions;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>Icon 命名契约测试（Task 11 Step 1，先写后实现；规格 v2 适配 webp）。钉死三件事：
/// ① 45 事件 Icon 字段形态——裸名匹配 ^ds_[a-z0-9]+\.webp$ 且与自身 Key 一致
/// （ds_g1.webp↔Key="g1"）；注入合成值（InjectorMapping.IconFieldOf）匹配 T18 原生约定
/// ^Assets/RuntimeAssets/Texture/EventNew/ds_[a-z0-9]+$（ResolveIconDir 剥前缀+扩展名自动重试）。
/// ② EmbeddedResource 名单（Art\ 目录扫描 → IconNaming.ResourceNameOf）与 45 事件
/// 期望资源集的差集：越集（目录外文件）=错；缺图=预期（正式图到位前只硬性要求占位
/// ds_g1.webp 在资源里——链路验证用），差集以测试输出报告，不断言 44 张。
/// 纯函数本体在 Core/IconNaming.cs（Game/Art/IconDrop 落盘消费同一套常量）。</summary>
public sealed class IconNamingTests
{
    private static readonly Regex IconFileRegex = new("^ds_[a-z0-9]+\\.webp$", RegexOptions.Compiled);
    private static readonly Regex NativeIconPathRegex = new("^Assets/RuntimeAssets/Texture/EventNew/ds_[a-z0-9]+$", RegexOptions.Compiled);
    private static readonly byte[] RiffHeader = { 0x52, 0x49, 0x46, 0x46 };   // "RIFF"
    private static readonly byte[] WebpHeader = { 0x57, 0x45, 0x42, 0x50 };   // "WEBP"

    private readonly ITestOutputHelper _output;

    public IconNamingTests(ITestOutputHelper output) => _output = output;

    // —— ① Icon 字段形态（45 全量）——

    [Fact]
    public void All45_Icons_MatchPattern_AndKeyFilename()
    {
        Assert.Equal(45, EventCatalog.Events.Count);
        Assert.Empty(IconNaming.IconIssues(EventCatalog.Events));
        foreach (var e in EventCatalog.Events)
        {
            Assert.Matches(IconFileRegex, e.Icon);
            Assert.Equal("ds_" + e.Key + ".webp", e.Icon);
            // 表 Icon 字段实际形态（T18 原生约定）：IconFieldOf 合成=前缀+裸名去扩展名
            var field = InjectorMapping.IconFieldOf(e.Icon);
            Assert.Equal("Assets/RuntimeAssets/Texture/EventNew/ds_" + e.Key, field);
            Assert.Matches(NativeIconPathRegex, field);
        }
        Assert.Equal("ds_g1.webp", ByKey("g1").Icon);   // 链路验证（G1=99205）
    }

    /// <summary>T18 备选格式开关（编译期小开关，不暴露 cfg）：当前=原生约定格式；
    /// 备选=面板 CSS 相对路径（LegacyIconPrefix+裸名原样带 .webp——EventChoice.html 自身
    /// 样式即用 ../../Res/ 前缀）。钉死常量与 IconFieldOf 的剥扩展名行为（防御裸名无扩展名）。</summary>
    [Fact]
    public void IconFieldOf_NativeFormat_StripsExtensionWithDefaults()
    {
        Assert.False(InjectorMapping.UseLegacyIconPath);   // 主选=原生约定
        Assert.Equal("Assets/RuntimeAssets/Texture/EventNew/ds_g1", InjectorMapping.IconFieldOf("ds_g1.webp"));
        Assert.Equal("Assets/RuntimeAssets/Texture/EventNew/ds_g1", InjectorMapping.IconFieldOf("ds_g1"));   // 无扩展名防御
        Assert.Equal("Assets/RuntimeAssets/Texture/EventNew/", InjectorMapping.IconFieldOf(""));              // 空防御
        Assert.Equal("../../Res/Icon/", InjectorMapping.LegacyIconPrefix);   // 备选前缀形态钉死（+裸名原样）
    }

    [Fact]
    public void IconIssues_Flags_BadPattern_KeyMismatch_And_Duplicate()
    {
        var broken = new[]
        {
            new EventDef { Id = 1, Key = "x1", Icon = "x1.webp" },        // 缺 ds_ 前缀
            new EventDef { Id = 2, Key = "x2", Icon = "ds_other.webp" },  // 文件名与 Key 不一致
            new EventDef { Id = 3, Key = "x3", Icon = "ds_x3.WEBP" },     // 扩展名大写
            new EventDef { Id = 4, Key = "x4", Icon = "" },              // 空
            new EventDef { Id = 5, Key = "x5", Icon = "ds_x5.webp" },     // 与下一行重复
            new EventDef { Id = 6, Key = "x6", Icon = "ds_x5.webp" },     // 重复（且与自身 Key 不一致）
        };
        var issues = IconNaming.IconIssues(broken);
        Assert.Contains(issues, i => i.Contains("ds_") && i.Contains("x1"));        // 形态违规点名
        Assert.Contains(issues, i => i.Contains("x2") && i.Contains("Key"));        // Key 不一致点名
        Assert.Contains(issues, i => i.Contains("x3"));                             // .WEBP 形态违规
        Assert.Contains(issues, i => i.Contains("x4"));                             // 空.Icon
        Assert.Contains(issues, i => i.Contains("重复"));                            // 重复点名
        Assert.Empty(IconNaming.IconIssues(new[] { new EventDef { Id = 7, Key = "y1", Icon = "ds_y1.webp" } }));
    }

    // —— ② EmbeddedResource 名单对账（Art\ 目录 = csproj EmbeddedResource 通配 Art/** 的物理面）——

    [Fact]
    public void ExpectedResourceNames_45_Unique_CatalogOrder()
    {
        var names = IconNaming.ExpectedResourceNames(EventCatalog.Events);
        Assert.Equal(45, names.Count);
        Assert.Equal(45, names.Distinct().Count());
        Assert.All(names, n => Assert.StartsWith(IconNaming.ResourcePrefix, n));
        Assert.Equal(IconNaming.ResourcePrefix + "ds_n1.webp", names[0]);
        Assert.Equal(IconNaming.ResourcePrefix + "ds_d3.webp", names[^1]);
        Assert.Contains(IconNaming.ResourcePrefix + "ds_g1.webp", names);
    }

    [Fact]
    public void ArtDir_PlaceholderG1_Present_NoStrays_AllWebpHeader()
    {
        var artDir = FindArtDir();
        var files = Directory.GetFiles(artDir, "ds_*.webp").Select(Path.GetFileName).OrderBy(f => f).ToList();

        // 链路验证硬要求：占位 ds_g1.webp 在资源里（正式图到位前唯一一张）
        Assert.Contains("ds_g1.webp", files);

        foreach (var f in files)
        {
            Assert.Matches(IconFileRegex, f);   // 目录内文件名一律合规（作者放图即自校验）
            using var s = File.OpenRead(Path.Combine(artDir, f));
            var head = new byte[12];
            Assert.Equal(12, s.Read(head, 0, 12));
            Assert.Equal(RiffHeader, head[..4]);   // WebP 文件头 RIFF….WEBP（防误放 png/jpg 改名货）
            Assert.Equal(WebpHeader, head[8..]);
        }

        var resources = files.Select(IconNaming.ResourceNameOf);
        var strays = IconNaming.StrayResourceIssues(EventCatalog.Events, resources);
        Assert.Empty(strays);   // 目录外事件图（拼错 Key/多余文件）=错，必须清理或改名
    }

    [Fact]
    public void ArtDir_MissingResources_Reported_NotAsserted()
    {
        var artDir = FindArtDir();
        var resources = Directory.GetFiles(artDir, "ds_*.webp")
            .Select(f => IconNaming.ResourceNameOf(Path.GetFileName(f))).ToList();
        var missing = IconNaming.MissingResources(EventCatalog.Events, resources);

        Assert.DoesNotContain(IconNaming.ResourcePrefix + "ds_g1.webp", missing);   // 占位不可缺
        Assert.InRange(missing.Count, 0, 44);   // 其余缺图=预期（正式图到位前），齐了则 0
        _output.WriteLine("Art\\ 现有 " + resources.Count + "/45；缺图（正式图到位前=预期，仅报告）: "
            + (missing.Count == 0 ? "无，45 张齐" : string.Join(", ", missing)));
    }

    [Fact]
    public void StrayResourceIssues_Flag_UnknownAndMalformed()
    {
        var strays = IconNaming.StrayResourceIssues(EventCatalog.Events, new[]
        {
            IconNaming.ResourcePrefix + "ds_zz9.webp",          // 不在 45 Key 集（目录外）
            "SurvivalLog.DailySurprise.Art.readme.txt",         // 非 webp
            "Other.Prefix.ds_g1.webp",                          // 前缀不对
            IconNaming.ResourcePrefix + "ds_g1.webp",           // 合规（不报）
        });
        Assert.Equal(3, strays.Count);
        Assert.Contains(strays, i => i.Contains("ds_zz9.webp"));
    }

    [Fact]
    public void MissingResources_IsExpectedMinusActual()
    {
        var missing = IconNaming.MissingResources(
            EventCatalog.Events, new[] { IconNaming.ResourcePrefix + "ds_g1.webp" });
        Assert.Equal(44, missing.Count);
        Assert.DoesNotContain(IconNaming.ResourcePrefix + "ds_g1.webp", missing);
        // 全齐 → 空
        Assert.Empty(IconNaming.MissingResources(
            EventCatalog.Events, IconNaming.ExpectedResourceNames(EventCatalog.Events)));
    }

    // —— 助手 ——

    private static EventDef ByKey(string key) => Assert.Single(EventCatalog.Events, e => e.Key == key);

    /// <summary>定位仓库 SurvivalLog.DailySurprise\Art\（测试程序集 bin 向上回溯；
    /// 仓库布局固定：Tests\bin\Debug\net6.0 → 上 4 级=仓库根）。找不到=仓库外运行，直接失败。</summary>
    private static string FindArtDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var d = dir; d != null; d = d.Parent)
        {
            var probe = Path.Combine(d.FullName, "SurvivalLog.DailySurprise", "Art");
            if (Directory.Exists(probe)) return probe;
        }
        throw new Xunit.Sdk.XunitException(
            "找不到 SurvivalLog.DailySurprise\\Art 目录（IconNamingTests 需从本仓库内运行，BaseDirectory=" + dir.FullName + "）");
    }
}
