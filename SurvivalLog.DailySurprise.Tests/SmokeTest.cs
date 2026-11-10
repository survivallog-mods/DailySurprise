using SurvivalLog.DailySurprise;
using Xunit;

namespace SurvivalLog.DailySurprise.Tests;

/// <summary>冒烟（Task 1）：插件身份常量与 spec 关键值逐字一致。Game 层
/// （DailySurprisePlugin/DsTrace）依赖 BepInEx/interop DLL，测试上下文没有——
/// DsTrace 文件写入留待 BaseDir 注入式测试，本任务不做。</summary>
public sealed class SmokeTest
{
    [Fact]
    public void PluginIdentity_MatchesSpec()
    {
        Assert.False(string.IsNullOrEmpty(Ds.Guid));
        Assert.Equal("com.local.survivallog.dailysurprise", Ds.Guid);
        Assert.Equal("喜从天降", Ds.Name);
    }
}
