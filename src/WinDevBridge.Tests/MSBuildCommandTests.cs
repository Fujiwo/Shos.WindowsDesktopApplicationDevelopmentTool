namespace WinDevBridge.Tests;

public class MSBuildCommandTests
{
    const string MSBuild = @"C:\VS\MSBuild\Current\Bin\MSBuild.exe";

    [Fact]
    public void BuildRunsMSBuildWithoutNodeReuseOrSharedCompilation()
    {
        var command = MSBuildCommand.Build(MSBuild, @"C:\ws\App\App.vcxproj", "Debug", platform: null);

        Assert.Equal(MSBuild, command.FileName);
        Assert.Equal(
            [@"C:\ws\App\App.vcxproj", "-restore", "-nologo", "-verbosity:minimal", "-nodeReuse:false", "-p:UseSharedCompilation=false", "-p:Configuration=Debug"],
            command.Arguments);
    }

    [Fact]
    public void BuildPassesPlatformAsProperty()
        => Assert.Equal("-p:Platform=x64", MSBuildCommand.Build(MSBuild, "App.vcxproj", "Release", "x64").Arguments[^1]);

    [Fact]
    public void BuildRejectsUnknownConfiguration()
        => Assert.Throws<RequestRejectedException>(() => MSBuildCommand.Build(MSBuild, "App.vcxproj", "Debug;Platform=x64", null));

    [Fact]
    public void BuildRejectsUnknownPlatform()
        => Assert.Throws<RequestRejectedException>(() => MSBuildCommand.Build(MSBuild, "App.vcxproj", "Debug", "x64 -t:Exec"));
}
