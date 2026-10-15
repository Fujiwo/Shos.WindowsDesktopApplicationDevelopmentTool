namespace WinDevBridge;

/// <summary>C++ のプロジェクト(または C++ を含むソリューション)を Visual Studio の MSBuild.exe でビルドするコマンドライン。</summary>
public static class MSBuildCommand
{
    // dotnet の --disable-build-servers に当たる。MSBuild のノードと VBCSCompiler を残さず、bin や obj を握らせない。
    static readonly string[] CommonOptions = ["-restore", "-nologo", "-verbosity:minimal", "-nodeReuse:false", "-p:UseSharedCompilation=false"];

    public static CommandLine Build(string msbuildPath, string projectPath, string configuration, string? platform)
    {
        string[] platformOption = BuildOptions.Platform(platform) is { } p ? [$"-p:Platform={p}"] : [];
        return new(msbuildPath, [projectPath, .. CommonOptions, $"-p:Configuration={BuildOptions.Configuration(configuration)}", .. platformOption]);
    }
}
