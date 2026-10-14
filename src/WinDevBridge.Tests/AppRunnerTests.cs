using System.Diagnostics;

namespace WinDevBridge.Tests;

public class AppRunnerTests
{
    // 30 秒動き続けるコマンド。窓を持たないので、観察の後は強制終了される。
    static CommandLine LongRunningCommand()
        => OperatingSystem.IsWindows() ? new("ping", ["-n", "30", "127.0.0.1"]) : new("sleep", ["30"]);

    [Fact]
    public async Task AppThatExitsDuringObservationReportsItsExitCode()
    {
        var outcome = await AppRunner.RunAsync(new("dotnet", ["--version"]), Environment.CurrentDirectory, TimeSpan.FromSeconds(30));

        Assert.Equal(AppEnding.ExitedOnItsOwn, outcome.Ending);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public async Task AppWithoutWindowStillRunningAfterObservationIsKilled()
    {
        var stopwatch = Stopwatch.StartNew();

        var outcome = await AppRunner.RunAsync(LongRunningCommand(), Environment.CurrentDirectory, TimeSpan.FromSeconds(1));

        Assert.Equal(AppEnding.Killed, outcome.Ending);
        Assert.Null(outcome.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"止めるのに {stopwatch.Elapsed} かかった");
    }
}
