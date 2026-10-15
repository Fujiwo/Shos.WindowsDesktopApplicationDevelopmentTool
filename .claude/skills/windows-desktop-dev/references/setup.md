# windev の準備(ユーザーが行うこと)

windev のツールが無い、またはつながらないときに、ユーザーへ伝える内容。Claude Code はサンドボックスの外に書けないので、これらはユーザーが行う。伝えるときは、足りないものだけを抜き出す。

## 1. ブリッジを置く(Windows、PowerShell。管理者でなくてよい)

WinDevBridge(https://github.com/Fujiwo/Shos.WindowsDesktopApplicationDevelopmentTool)を発行し、Claude Code が書けない場所に置く。`bridge.json` は実行ファイルと同じフォルダーに置く(ブリッジはそこからだけ読む)。

```powershell
git clone https://github.com/Fujiwo/Shos.WindowsDesktopApplicationDevelopmentTool C:\src\WinDevBridge
dotnet publish C:\src\WinDevBridge\src\WinDevBridge -c Release -r win-x64 --self-contained false -o C:\Tools\WinDevBridge
Copy-Item C:\src\WinDevBridge\src\WinDevBridge\bridge.sample.json C:\Tools\WinDevBridge\bridge.json
```

WSL の dotnet で発行したもの(リポジトリの README の手順)を `C:\Tools\WinDevBridge` に写してもよい。

## 2. bridge.json を書く(`C:\Tools\WinDevBridge\bridge.json`)

```json
{
  "workspaceRoot": "C:\\src\\MyApp",
  "buildTimeoutSeconds": 600,
  "testTimeoutSeconds": 900,
  "maxObservationSeconds": 60,
  "apps": {
    "MyApp": "src/MyApp/bin/Debug/net10.0-windows/MyApp.exe",
    "MyCppApp": "src/MyCppApp/bin/x64/Debug/MyCppApp.exe"
  }
}
```

| 項目 | 意味 |
|------|------|
| `workspaceRoot` | ビルドとテストを許すフォルダーの絶対パス。複数のリポジトリで使うなら、その親フォルダーにする |
| `apps` | `run_app` で起動してよいアプリの名前と、`workspaceRoot` からの実行ファイルの相対パス |
| `msbuildPath` / `vstestPath` | 省略可。C++ で使う `MSBuild.exe` / `vstest.console.exe` の絶対パス。省略すると vswhere で、C++ のワークロードの入った最新の Visual Studio から探す |

`bridge.json` を変えたら、Claude Code を起動し直す(ブリッジは起動のときに読む)。

## 3. Claude Code に登録する(WSL のシェル。Claude Code の外で)

```bash
claude mcp add --scope user --transport stdio windev -- /mnt/c/Tools/WinDevBridge/WinDevBridge.exe
```

`--scope user` にすると、登録は `~/.claude.json` に入り、どのリポジトリでも windev を使える。

## 4. 権限と時間切れ(`~/.claude/settings.json`)

どのリポジトリでも同じ境界にするため、ユーザー単位の設定に入れる。`env` の `MCP_TOOL_TIMEOUT` は、テストの時間切れ(既定 900 秒)より前に MCP の時間切れが来ないようにするためのもの(ミリ秒)。

```json
{
  "permissions": {
    "allow": ["mcp__windev__build", "mcp__windev__test", "mcp__windev__run_app"],
    "ask": ["Edit(**/*.csproj)", "Edit(**/*.vcxproj)", "Edit(**/*.props)", "Edit(**/*.targets)", "Edit(**/nuget.config)", "Edit(**/global.json)"],
    "deny": [
      "Edit(//mnt/c/Tools/**)",
      "Edit(~/.claude.json)",
      "Edit(~/.claude/settings.json)",
      "Edit(**/.mcp.json)",
      "Edit(**/.claude/settings.json)",
      "Edit(**/.claude/settings.local.json)"
    ]
  },
  "sandbox": { "enabled": true, "allowUnsandboxedCommands": false },
  "env": { "MCP_TOOL_TIMEOUT": "960000" }
}
```

- `allow` は、確認待ちでループが止まらないようにする。
- `ask` は、ビルドの定義の編集に承認を求める。ビルドの定義は、ビルドのときに任意のコマンドを走らせられる場所だからである。
- `deny` は、境界そのもの(ブリッジとその設定、MCP の登録、権限の設定)を書き換えさせない。

設定を変えたら Claude Code を起動し直し、`/mcp` で `windev` が connected になっていることを確かめる。

## 5. Windows 側に要るもの

- C#: .NET SDK(`C:\Program Files\dotnet\dotnet.exe` が `PATH` にあること)と、Windows デスクトップのランタイム。
- C++: Visual Studio か Build Tools for Visual Studio の「C++ によるデスクトップ開発」。MFC を使うなら「C++ MFC」のコンポーネントも。

## 6. ファイルの置き場所

- ソースは Windows のファイルシステム(`/mnt/c/...`)に置く。ビルドは Windows で動くからである。
- Dropbox や OneDrive で同期しているなら、`bin`、`obj` を同期から外す。同期がファイルを握り、ビルドとぶつかる。
