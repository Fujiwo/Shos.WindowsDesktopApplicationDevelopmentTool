using System.Diagnostics;

namespace WinDevBridge.Tests;

public class ProcessRunnerTests
{
    static readonly TimeSpan Generous = TimeSpan.FromMinutes(1);

    // 30 秒かかるコマンド。Windows と Linux の両方でテストを回すため、OS ごとに選ぶ。
    static CommandLine SlowCommand()
        => OperatingSystem.IsWindows() ? new("ping", ["-n", "30", "127.0.0.1"]) : new("sleep", ["30"]);

    [Fact]
    public async Task SuccessfulCommandReportsExitCodeZeroAndOutput()
    {
        var outcome = await ProcessRunner.RunAsync(new("dotnet", ["--version"]), Environment.CurrentDirectory, Generous);

        Assert.Equal(0, outcome.ExitCode);
        Assert.False(outcome.TimedOut);
        Assert.Matches(@"^\d+\.\d+", outcome.StandardOutput);
    }

    [Fact]
    public async Task FailingCommandReportsNonZeroExitCodeAndError()
    {
        var outcome = await ProcessRunner.RunAsync(new("dotnet", ["no-such-command"]), Environment.CurrentDirectory, Generous);

        Assert.NotEqual(0, outcome.ExitCode);
        Assert.NotEmpty(outcome.StandardOutput + outcome.StandardError);
    }

    [Fact]
    public async Task CommandOverTimeoutIsStoppedAndReportedAsTimedOut()
    {
        var stopwatch = Stopwatch.StartNew();

        var outcome = await ProcessRunner.RunAsync(SlowCommand(), Environment.CurrentDirectory, TimeSpan.FromSeconds(1));

        Assert.True(outcome.TimedOut);
        Assert.Null(outcome.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"止めるのに {stopwatch.Elapsed} かかった");
    }
}
