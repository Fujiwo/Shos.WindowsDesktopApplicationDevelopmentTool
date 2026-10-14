namespace WinDevBridge;

/// <summary>アプリがどう終わったか。</summary>
public enum AppEnding
{
    /// <summary>観察の時間のうちに自分で終わった。</summary>
    ExitedOnItsOwn,
    /// <summary>観察の後、メインウィンドウを閉じる要求で終わった。</summary>
    ClosedByWindow,
    /// <summary>観察の後、閉じる要求で終わらなかったので強制終了した。</summary>
    Killed,
}

/// <summary>
/// アプリを観察した結果。MainWindowTitle と Responding は観察の終わりの時点の値で、自分で終わったときは空と true。
/// 強制終了したときは ExitCode が null になる。
/// </summary>
public sealed record AppOutcome(
    AppEnding Ending, int? ExitCode, string MainWindowTitle, bool Responding, string StandardOutput, string StandardError);

/// <summary>アプリを起動して決めた時間だけ観察し、その後に必ず終わらせる。GUI アプリがハングしてもブリッジは戻る。</summary>
public static class AppRunner
{
    static readonly TimeSpan CloseLimit = TimeSpan.FromSeconds(5);

    public static async Task<AppOutcome> RunAsync(CommandLine app, string workingDirectory, TimeSpan observation)
    {
        using var process = CapturedProcess.Start(app, workingDirectory);
        if (await process.WaitForExitAsync(observation))
            return await OutcomeAsync(process, AppEnding.ExitedOnItsOwn, "", responding: true);

        var title = process.MainWindowTitle;
        var responding = process.Responding;
        var ending = await StopAsync(process);
        return await OutcomeAsync(process, ending, title, responding);
    }

    static async Task<AppEnding> StopAsync(CapturedProcess process)
    {
        if (process.CloseMainWindow() && await process.WaitForExitAsync(CloseLimit))
            return AppEnding.ClosedByWindow;
        process.Kill();
        await process.WaitForExitAsync(CloseLimit);
        return AppEnding.Killed;
    }

    static async Task<AppOutcome> OutcomeAsync(CapturedProcess process, AppEnding ending, string title, bool responding)
    {
        var (standardOutput, standardError) = await process.CollectOutputAsync();
        int? exitCode = ending == AppEnding.Killed ? null : process.ExitCode;
        return new(ending, exitCode, title, responding, standardOutput, standardError);
    }
}
