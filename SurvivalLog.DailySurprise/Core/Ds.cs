namespace SurvivalLog.DailySurprise;

/// <summary>插件身份常量（纯逻辑层）。独立成文件的原因：Game 层文件（DailySurprisePlugin.cs）
/// 依赖 BepInEx/interop DLL，测试上下文没有——Tests 按套件惯例源链接本文件做冒烟断言
/// （镜像 EndlessGuard.Tests 链接 DeviceXpMath.cs 的做法）。</summary>
public static class Ds
{
    public const string Guid = "com.local.survivallog.dailysurprise";
    public const string Name = "喜从天降";
    public const string Version = "1.0.0"; // 版本权威=本仓 csproj <Version>（两处须同字面，release.ps1 发版闸校验）
}
