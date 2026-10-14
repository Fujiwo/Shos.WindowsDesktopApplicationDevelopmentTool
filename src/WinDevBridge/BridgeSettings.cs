using System.Text.Json;

namespace WinDevBridge;

/// <summary>
/// ブリッジの設定。Claude Code が書き換えられない場所(ブリッジの実行ファイルの隣)の bridge.json から読む。
/// Apps はアプリの名前から、作業フォルダーからの実行ファイルの相対パスへの対応。
/// </summary>
public sealed record BridgeSettings(
    string WorkspaceRoot, TimeSpan BuildTimeout, TimeSpan TestTimeout, TimeSpan MaxObservation, IReadOnlyDictionary<string, string> Apps)
{
    public const string FileName = "bridge.json";

    public static BridgeSettings Load(string path) => Parse(File.ReadAllText(path));

    public static BridgeSettings Parse(string json)
    {
        var file = JsonSerializer.Deserialize<SettingsFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"{FileName} が空です。");

        if (string.IsNullOrWhiteSpace(file.WorkspaceRoot) || !Path.IsPathFullyQualified(file.WorkspaceRoot))
            throw new InvalidDataException($"{FileName} の workspaceRoot に作業フォルダーの絶対パスを書いてください。");
        if (!Directory.Exists(file.WorkspaceRoot))
            throw new InvalidDataException($"{FileName} の workspaceRoot のフォルダーがありません: {file.WorkspaceRoot}");

        return new(
            file.WorkspaceRoot,
            Seconds(file.BuildTimeoutSeconds, "buildTimeoutSeconds"),
            Seconds(file.TestTimeoutSeconds, "testTimeoutSeconds"),
            Seconds(file.MaxObservationSeconds, "maxObservationSeconds"),
            file.Apps ?? new Dictionary<string, string>());
    }

    static TimeSpan Seconds(int value, string name)
        => value > 0 ? TimeSpan.FromSeconds(value) : throw new InvalidDataException($"{FileName} の {name} は正の秒数にしてください: {value}");

    sealed record SettingsFile(
        string? WorkspaceRoot,
        int BuildTimeoutSeconds = 600,
        int TestTimeoutSeconds = 900,
        int MaxObservationSeconds = 60,
        Dictionary<string, string>? Apps = null);
}
