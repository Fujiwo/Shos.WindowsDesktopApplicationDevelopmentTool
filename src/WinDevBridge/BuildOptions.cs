namespace WinDevBridge;

/// <summary>ビルドに渡してよい構成とプラットフォーム。任意の文字列を通すと、MSBuild のプロパティやスイッチを差し込まれる。</summary>
public static class BuildOptions
{
    static readonly string[] Configurations = ["Debug", "Release"];

    // C++ は Win32 / x64、C# のソリューションは x86 / x64 と書く。
    static readonly string[] Platforms = ["x64", "Win32", "x86", "ARM64"];

    public static string Configuration(string configuration)
        => Configurations.Contains(configuration)
            ? configuration
            : throw new RequestRejectedException($"構成は {string.Join(" か ", Configurations)} を指定してください: {configuration}");

    public static string? Platform(string? platform)
        => platform is null || Platforms.Contains(platform)
            ? platform
            : throw new RequestRejectedException($"プラットフォームは {string.Join(", ", Platforms)} のどれかを指定するか、省略してください: {platform}");
}
