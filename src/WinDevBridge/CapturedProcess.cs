using System.Diagnostics;

namespace WinDevBridge;

/// <summary>標準出力と標準エラー出力を取り込みながら動いているプロセス。</summary>
public sealed class CapturedProcess : IDisposable
{
    // 止めた後も孫プロセスが出力のパイプを握っていることがあるので、読み残しを待つ時間に上限を置く。
    static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(5);

    readonly Process process;
    readonly Task<byte[]> standardOutput;
    readonly Task<byte[]> standardError;

    CapturedProcess(Process process)
    {
        this.process = process;
        standardOutput = ReadAllBytesAsync(process.StandardOutput.BaseStream);
        standardError = ReadAllBytesAsync(process.StandardError.BaseStream);
    }

    public int ExitCode => process.ExitCode;

    /// <summary>メインウィンドウのタイトル。窓がなければ空。</summary>
    public string MainWindowTitle
    {
        get
        {
            process.Refresh();
            return process.MainWindowTitle;
        }
    }

    /// <summary>メインウィンドウがメッセージに応答しているか。窓のないプロセスでは常に true。</summary>
    public bool Responding
    {
        get
        {
            process.Refresh();
            return process.Responding;
        }
    }

    /// <summary>メインウィンドウに閉じる要求を送る。窓がなければ false。</summary>
    public bool CloseMainWindow() => process.CloseMainWindow();

    public static CapturedProcess Start(CommandLine command, string workingDirectory)
        => new(Process.Start(CreateStartInfo(command, workingDirectory))
            ?? throw new InvalidOperationException($"起動できませんでした: {command.FileName}"));

    /// <summary>時間内に終われば true。</summary>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Kill() => process.Kill(entireProcessTree: true);

    public async Task<(string StandardOutput, string StandardError)> CollectOutputAsync()
    {
        await Task.WhenAny(Task.WhenAll(standardOutput, standardError), Task.Delay(DrainLimit));
        return (ConsoleText.Decode(await ReadOrEmpty(standardOutput)), ConsoleText.Decode(await ReadOrEmpty(standardError)));
    }

    public void Dispose() => process.Dispose();

    static ProcessStartInfo CreateStartInfo(CommandLine command, string workingDirectory)
    {
        // ブリッジの標準入出力は MCP の通信路なので、子プロセスには一つも引き継がせない。
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    static async Task<byte[]> ReadOrEmpty(Task<byte[]> reading)
        => reading.IsCompletedSuccessfully ? await reading : [];
}
