using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace WinDevBridge;

/// <summary>Claude Code に見せる操作。ビルド、テスト、許可したアプリの起動と終了だけを置く。</summary>
[McpServerToolType]
public sealed class BridgeTools(BridgeSettings settings)
{
    // ビルドとテストが同時に走ると、同じ bin と obj を取り合う。要求は一つずつ処理する。
    static readonly SemaphoreSlim OneAtATime = new(1, 1);

    readonly Workspace workspace = new(settings.WorkspaceRoot);

    [McpServerTool(Name = "build")]
    [Description("Windows の dotnet でプロジェクトかソリューションをビルドする。結果(成功・失敗・時間切れ)と出力を返す。")]
    public Task<string> BuildAsync(
        [Description("作業フォルダーからの相対パス(例: samples/App/App.csproj)")] string project,
        [Description("Debug か Release")] string configuration = "Debug")
    {
        var command = Accept(() => DotnetCommand.Build(workspace.ResolveProject(project), configuration));
        return RunAsync(command, settings.BuildTimeout);
    }

    [McpServerTool(Name = "test")]
    [Description("Windows の dotnet test で単体テストを実行する。必要ならビルドもする。結果と出力を返す。")]
    public Task<string> TestAsync(
        [Description("テストプロジェクトかソリューションの、作業フォルダーからの相対パス")] string project,
        [Description("dotnet test --filter の式(例: FullyQualifiedName~Temperature)。省略するとすべて実行する")] string? filter = null)
    {
        var command = Accept(() => DotnetCommand.Test(workspace.ResolveProject(project), filter));
        return RunAsync(command, settings.TestTimeout);
    }

    [McpServerTool(Name = "run_app")]
    [Description("許可されたアプリを Windows で起動し、指定の秒数だけ観察した後に必ず終了させる。メインウィンドウの有無、応答の有無、終わり方を返す。")]
    public Task<string> RunAppAsync(
        [Description("bridge.json の apps に登録したアプリの名前")] string app,
        [Description("観察する秒数")] int seconds = 10)
    {
        var observation = Accept(() => Observation(seconds));
        var executable = Accept(() => workspace.ResolveFile(AppPath(app)));
        return OneAtATimeAsync(async () =>
            ToolReport.Describe(await AppRunner.RunAsync(new(executable, []), Path.GetDirectoryName(executable)!, observation)));
    }

    Task<string> RunAsync(CommandLine command, TimeSpan timeout)
        => OneAtATimeAsync(async () =>
            ToolReport.Describe(await ProcessRunner.RunAsync(command, settings.WorkspaceRoot, timeout), timeout));

    static async Task<string> OneAtATimeAsync(Func<Task<string>> operation)
    {
        await OneAtATime.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    TimeSpan Observation(int seconds)
    {
        var max = (int)settings.MaxObservation.TotalSeconds;
        return seconds is >= 1 && seconds <= max
            ? TimeSpan.FromSeconds(seconds)
            : throw new RequestRejectedException($"観察する秒数は 1〜{max} 秒で指定してください: {seconds}");
    }

    string AppPath(string app)
        => settings.Apps.TryGetValue(app, out var path)
            ? path
            : throw new RequestRejectedException($"登録されていないアプリです: {app}(登録済み: {string.Join(", ", settings.Apps.Keys)})");

    static T Accept<T>(Func<T> validate)
    {
        try
        {
            return validate();
        }
        catch (RequestRejectedException rejected)
        {
            throw new McpException(rejected.Message, rejected);
        }
    }
}
