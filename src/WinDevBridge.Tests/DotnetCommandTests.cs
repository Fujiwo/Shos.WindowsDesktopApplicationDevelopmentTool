namespace WinDevBridge.Tests;

public class DotnetCommandTests
{
    [Fact]
    public void BuildRunsDotnetBuildWithoutBuildServers()
    {
        var command = DotnetCommand.Build(@"C:\ws\App\App.csproj", "Debug");

        Assert.Equal("dotnet", command.FileName);
        Assert.Equal(
            ["build", @"C:\ws\App\App.csproj", "--configuration", "Debug", "--nologo", "--verbosity", "minimal", "--disable-build-servers"],
            command.Arguments);
    }

    [Fact]
    public void BuildAcceptsRelease()
        => Assert.Contains("Release", DotnetCommand.Build("App.csproj", "Release").Arguments);

    [Fact]
    public void BuildRejectsUnknownConfiguration()
        => Assert.Throws<RequestRejectedException>(() => DotnetCommand.Build("App.csproj", "Debug -p:X=1"));

    [Fact]
    public void TestWithoutFilterRunsAllTests()
        => Assert.Equal(
            ["test", "App.Tests.csproj", "--nologo", "--verbosity", "minimal", "--disable-build-servers"],
            DotnetCommand.Test("App.Tests.csproj", filter: null).Arguments);

    [Fact]
    public void TestWithFilterPassesFilterAsOneArgument()
        => Assert.Equal(
            ["test", "App.Tests.csproj", "--nologo", "--verbosity", "minimal", "--disable-build-servers", "--filter", "FullyQualifiedName~Temperature & Category!=Slow"],
            DotnetCommand.Test("App.Tests.csproj", "FullyQualifiedName~Temperature & Category!=Slow").Arguments);

    [Fact]
    public void TestWithBlankFilterRunsAllTests()
        => Assert.DoesNotContain("--filter", DotnetCommand.Test("App.Tests.csproj", " ").Arguments);
}
