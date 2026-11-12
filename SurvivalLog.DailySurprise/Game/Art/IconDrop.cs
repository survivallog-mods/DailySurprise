using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;

namespace SurvivalLog.DailySurprise;

/// <summary>配图落盘通道（Task 11，EndlessGuard ArrowTower.EnsureIconFile 同源模式的多图版）：
/// 程序集 EmbeddedResource（csproj 通配 Art\*.webp，资源名=IconNaming.ResourcePrefix+文件名）
/// → 写 <游戏根>\SurvivalLog_Data\StreamingAssets\WebUI\Res\Icon\ds_*.webp；表 Icon 字段的
/// "../../Res/Icon/ds_xx.webp"（InjectorMapping.IconPrefix）按普通文件从该目录解析
///（EndlessGuard wave8 ToolTable.html 实证通道）。
/// 幂等（可重复调用）：目标已存在且字节数+SHA256 全等→跳过零写入；不一致→覆盖
///（升级换图：作者替换 Art\ 里的同名文件重新构建即热换）。
/// 失败隔离：逐资源 try/catch；每资源结果+末尾汇总行写 trace\icons.txt（0 张也有一行
/// 汇总）；目录契约不成立（WebUI\Res 不存在）绝不乱建目录，整通道静默退避。
/// 调用点：插件 Load（DailySurprisePlugin.Load，先于任何表注入；幂等故挂多次亦无害）。</summary>
internal static class IconDrop
{
    /// <summary>入口（插件 Load 调用）：游戏根=BepInEx.Paths.BepInExRootPath 上溯一级
    ///（EndlessGuard EnsureIconFile 同源取法，2026-11-01 实机 trace 修正版）。</summary>
    internal static void Install() => Install(null);

    /// <summary>带根落盘（gameRoot=null → 运行时 BepInEx.Paths 解析；给出则写到其下
    /// SurvivalLog_Data\...，供部署脚本/调试直用）。</summary>
    internal static void Install(string gameRoot)
    {
        var written = 0;
        var skipped = 0;
        var failed = 0;
        var names = Array.Empty<string>();
        var contractBroken = false;
        try
        {
            var root = gameRoot ?? Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, ".."));
            var resDir = Path.Combine(root, "SurvivalLog_Data", "StreamingAssets", "WebUI", "Res");
            if (!Directory.Exists(resDir))
            {
                // 目录契约不成立：不在错误位置建目录（EndlessGuard 定案），本会话事件卡显示裂图=预期。
                // 该行即本次 Install 的唯一记录行（finally 汇总不再叠加，防误读"0 张"）
                contractBroken = true;
                DsTrace.Write("icons", "WebUI/Res 目录不存在（" + resDir + "）——图标不落盘，事件卡将显示裂图");
                return;
            }
            Directory.CreateDirectory(Path.Combine(resDir, "Icon"));   // 已有则空转；只在已验证的 Res 下补子目录

            names = typeof(IconDrop).Assembly.GetManifestResourceNames();
            Array.Sort(names, StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (!name.StartsWith(IconNaming.ResourcePrefix, StringComparison.Ordinal)) continue;
                var file = IconNaming.FileNameOf(name);
                if (file == null)
                {
                    DsTrace.Write("icons", "跳过形态异常资源 " + name + "（应为 " + IconNaming.ResourcePrefix + "ds_<key>.webp）");
                    continue;
                }
                try
                {
                    var bytes = ReadResource(name);
                    var target = Path.Combine(resDir, "Icon", file);
                    if (File.Exists(target) && SameContent(target, bytes))
                    {
                        skipped++;   // 字节数+SHA256 全等：零写入零 trace（稳态静默）
                        continue;
                    }
                    var tmp = target + ".tmp";
                    File.WriteAllBytes(tmp, bytes);
                    File.Move(tmp, target, true);   // 原子换（失败残留 .tmp 由下次写覆盖）
                    written++;
                    DsTrace.Write("icons", "落盘 " + target + " (" + bytes.Length + " bytes)");
                }
                catch (Exception e)
                {
                    failed++;
                    DsTrace.Write("icons", "落盘失败 " + file + ": " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            DsTrace.Write("icons", "Install 异常: " + e.Message);
        }
        finally
        {
            // 汇总行（写 0 张也要有一行——硬规则）：N=内嵌 ds_*.webp 资源数；
            // 契约不成立时上面已写过唯一记录行，不再叠加
            if (!contractBroken)
                DsTrace.Write("icons", "Install 完成：内嵌图标资源 " + CountDsIcons(names) + " 张（新写 " + written
                    + "、跳过 " + skipped + "、失败 " + failed + "）"
                    + (CountDsIcons(names) == 0 ? "（Art\\ 无图：作者出图前仅占位链路，见 art-spec/acceptance-checklist.md）" : ""));
        }
    }

    private static int CountDsIcons(string[] names)
    {
        var n = 0;
        foreach (var name in names)
            if (name.StartsWith(IconNaming.ResourcePrefix, StringComparison.Ordinal)
                && IconNaming.FileNameOf(name) != null) n++;
        return n;
    }

    private static byte[] ReadResource(string name)
    {
        using var s = typeof(IconDrop).Assembly.GetManifestResourceStream(name);
        if (s == null) throw new InvalidOperationException("GetManifestResourceStream 返回 null: " + name);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>字节数+SHA256 双比对（长度先短路；图标均小图，整读无压力）。</summary>
    private static bool SameContent(string path, byte[] bytes)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length != bytes.Length) return false;
        var onDisk = File.ReadAllBytes(path);
        var a = SHA256.HashData(onDisk);
        var b = SHA256.HashData(bytes);
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
