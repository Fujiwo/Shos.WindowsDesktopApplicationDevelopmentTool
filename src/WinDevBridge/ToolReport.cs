using System.Text;

namespace WinDevBridge;

/// <summary>ツールの結果を、Claude Code が読む文面にする。</summary>
public static class ToolReport
{
    // Claude Code は MCP のツールの長い結果を切り詰める。ビルドとテストの失敗のまとめは末尾に出るので、末尾を残す。
    public const int MaxOutputLength = 12_000;

    public static string Describe(ProcessOutcome outcome, TimeSpan timeout)
    {
        var result = outcome switch
        {
            { TimedOut: true } => $"時間切れ({timeout.TotalSeconds:0} 秒で止めました)",
            { ExitCode: 0 } => "成功(終了コード 0)",
            _ => $"失敗(終了コード {outcome.ExitCode})",
        };
        return Compose(result, [], outcome.StandardOutput, outcome.StandardError);
    }

    public static string Describe(AppOutcome outcome)
    {
        var result = outcome.Ending switch
        {
            AppEnding.ExitedOnItsOwn => $"観察のうちに自分で終了しました(終了コード {outcome.ExitCode})",
            AppEnding.ClosedByWindow => $"観察の後、ウィンドウを閉じて終了させました(終了コード {outcome.ExitCode})",
            _ => "観察の後、閉じる要求で終了しなかったので強制終了しました",
        };
        string[] details = outcome.Ending == AppEnding.ExitedOnItsOwn ? [] : [MainWindow(outcome)];
        return Compose(result, details, outcome.StandardOutput, outcome.StandardError);
    }

    static string MainWindow(AppOutcome outcome)
        => outcome.MainWindowTitle.Length == 0
            ? "メインウィンドウ: なし"
            : $"メインウィンドウ: \"{outcome.MainWindowTitle}\"({(outcome.Responding ? "応答あり" : "応答なし")})";

    static string Compose(string result, string[] details, string standardOutput, string standardError)
    {
        var report = new StringBuilder($"結果: {result}\n");
        foreach (var detail in details)
            report.Append(detail).Append('\n');
        AppendSection(report, "標準出力", standardOutput);
        AppendSection(report, "標準エラー出力", standardError);
        return report.ToString();
    }

    static void AppendSection(StringBuilder report, string name, string text)
    {
        if (text.Length == 0)
            return;
        report.Append($"--- {name} ---\n");
        if (text.Length > MaxOutputLength)
            report.Append($"(先頭の {text.Length - MaxOutputLength} 文字を省略)\n");
        report.Append(text[^Math.Min(text.Length, MaxOutputLength)..]).Append('\n');
    }
}
