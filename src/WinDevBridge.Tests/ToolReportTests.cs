namespace WinDevBridge.Tests;

public class ToolReportTests
{
    static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    [Fact]
    public void SuccessShowsExitCodeAndOutput()
    {
        var report = ToolReport.Describe(new ProcessOutcome(0, false, "Build succeeded.", ""), Timeout);

        Assert.StartsWith("結果: 成功(終了コード 0)", report);
        Assert.Contains("--- 標準出力 ---\nBuild succeeded.", report);
    }

    [Fact]
    public void EmptyStandardErrorIsOmitted()
        => Assert.DoesNotContain("標準エラー出力", ToolReport.Describe(new ProcessOutcome(0, false, "ok", ""), Timeout));

    [Fact]
    public void FailureShowsExitCodeAndStandardError()
    {
        var report = ToolReport.Describe(new ProcessOutcome(1, false, "", "error CS1002: ; expected"), Timeout);

        Assert.StartsWith("結果: 失敗(終了コード 1)", report);
        Assert.Contains("--- 標準エラー出力 ---\nerror CS1002: ; expected", report);
    }

    [Fact]
    public void TimeoutShowsTheLimit()
        => Assert.StartsWith("結果: 時間切れ(600 秒で止めました)", ToolReport.Describe(new ProcessOutcome(null, true, "", ""), Timeout));

    [Fact]
    public void LongOutputKeepsTheTailAndTellsHowMuchWasOmitted()
    {
        var output = new string('a', 5) + new string('b', ToolReport.MaxOutputLength);

        var report = ToolReport.Describe(new ProcessOutcome(0, false, output, ""), Timeout);

        Assert.Contains("(先頭の 5 文字を省略)\n" + new string('b', ToolReport.MaxOutputLength), report);
        Assert.DoesNotContain("a", report.Split("--- 標準出力 ---")[1]);
    }

    [Fact]
    public void AppThatExitedOnItsOwnShowsItsExitCode()
        => Assert.StartsWith(
            "結果: 観察のうちに自分で終了しました(終了コード 3)",
            ToolReport.Describe(new AppOutcome(AppEnding.ExitedOnItsOwn, 3, "", true, "", "")));

    [Fact]
    public void AppClosedByWindowShowsTitleAndResponsiveness()
    {
        var report = ToolReport.Describe(new AppOutcome(AppEnding.ClosedByWindow, 0, "Sample", true, "", ""));

        Assert.StartsWith("結果: 観察の後、ウィンドウを閉じて終了させました(終了コード 0)", report);
        Assert.Contains("メインウィンドウ: \"Sample\"(応答あり)", report);
    }

    [Fact]
    public void HungAppIsReportedAsNotResponding()
    {
        var report = ToolReport.Describe(new AppOutcome(AppEnding.Killed, null, "Sample", false, "", ""));

        Assert.StartsWith("結果: 観察の後、閉じる要求で終了しなかったので強制終了しました", report);
        Assert.Contains("メインウィンドウ: \"Sample\"(応答なし)", report);
    }

    [Fact]
    public void AppWithoutWindowIsReportedAsSuch()
        => Assert.Contains("メインウィンドウ: なし", ToolReport.Describe(new AppOutcome(AppEnding.Killed, null, "", true, "", "")));
}
