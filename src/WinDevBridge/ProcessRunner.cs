namespace WinDevBridge;

/// <summary>終わるまで待つコマンドの結果。時間切れで止めたときは ExitCode が null になる。</summary>
public sealed record ProcessOutcome(int? ExitCode, bool TimedOut, string StandardOutput, string StandardError);

/// <summary>コマンドを起動し、終わるか時間切れになるまで待って、出力を集める。</summary>
public static class ProcessRunner
{
    public static async Task<ProcessOutcome> RunAsync(CommandLine command, string workingDirectory, TimeSpan timeout)
    {
        using var process = CapturedProcess.Start(command, workingDirectory);
        var timedOut = !await process.WaitForExitAsync(timeout);
        if (timedOut)
            process.Kill();
        var (standardOutput, standardError) = await process.CollectOutputAsync();
        return new(timedOut ? null : process.ExitCode, timedOut, standardOutput, standardError);
    }
}
