# Windows デスクトップアプリを Claude Code が自律して開発する環境

- 作成: 2026-10-14 17:46(日本時間)
- 元のプロンプト: `.github/prompts/ClaudeCodeDevelopmentBestPractices.prompt.md`
- 作ったツール: `src/WinDevBridge`(ブリッジ)、`src/WinDevBridge.Tests`(xUnit のテスト)
- 検証の対象: `samples/TemperatureConverter`(C# / .NET 10 / WPF のアプリと xUnit のテスト)、`samples/TemperatureConverterCpp`(C++ / Win32 のアプリと Microsoft C++ Unit Test Framework のテスト。2026-10-15 に追加)

## 0. 前提の確認(比べる前に実機で確かめたこと)

| 前提 | 結果 | 確かめ方 |
|------|------|----------|
| Bash サンドボックスの中から WSL Interop を使えるか | **使えない**。`cmd.exe /c ver` が `UtilConnectUnix:534: socket failed 1` で失敗する。1 は EPERM で、サンドボックスの seccomp が AF_UNIX のソケットの作成を拒んでいる | `/mnt/c/Windows/System32/cmd.exe /c ver` を実行 |
| Bash サンドボックスから Windows ホストへネットワークでつながるか | **直接はつながらない**。サンドボックスのネットワークの名前空間には `lo` しかない。外へ出られるのはフィルター付きのプロキシー(localhost:3128)だけ。プロキシー経由で Windows ホストへ届くかは確かめていない(この方式では要らない) | `ip addr`、`env \| grep -i proxy` |
| MCP の stdio サーバーとフックはサンドボックスの外で動くか | **外で動く**(Claude Code の文書 sandboxing / mcp)。実機での確認は節 2 の手順の後に行う(下の「検証の記録」) | 文書 |
| Bash から Interop を開く設定 | `sandbox.network.allowAllUnixSockets`(すべての Unix ソケットを開く)と `sandbox.excludedCommands`(文字列が一致したコマンドをサンドボックスの外で全権で動かす)がある | 文書 |
| Windows 側のツールチェーン | .NET SDK 10.0.401 / 9.0.300、ランタイム 10.0.12(Microsoft.NETCore.App と Microsoft.WindowsDesktop.App)、Visual Studio 2019 / 2022 / 18 | `/mnt/c/Program Files/dotnet` などの一覧 |
| Linux 側で WPF をビルドできるか | **できない**。`-p:EnableWindowsTargeting=true` を付けても、サンプルの WPF のプロジェクトが理由を出さずに失敗し、テストのプロジェクトから型が見えない | `dotnet build samples/TemperatureConverter/TemperatureConverter.Tests -p:EnableWindowsTargeting=true` |
| Linux 側の dotnet をサンドボックスで使えるか | ホームに書けないため、`DOTNET_CLI_HOME` と `NUGET_PACKAGES` を `$TMPDIR` の下に移せば使える。NuGet にはプロキシー経由でつながるが、証明書の失効確認(crl3.digicert.com など)は拒まれるので `NUGET_CERT_REVOCATION_MODE=offline` にする | ブリッジの開発で実際に使用 |

## 1. 実現方式の比較と選定

| 方式 | 自律性 | セキュリティ | 導入・運用のコスト |
|------|--------|--------------|--------------------|
| A. Bash から WSL Interop を開く(`allowAllUnixSockets`、または `excludedCommands` でラッパーをサンドボックスの外で動かす) | 高い | **低い**。`allowAllUnixSockets` は Bash のすべてのコマンドに Windows の任意のコマンドを許す。`excludedCommands` は文字列の一致なので、書き方を変えれば外れる。外れなくてもラッパーは全権で動き、引数の検証を bash で書くことになる | 低い |
| **B. Windows で動く MCP サーバー(Claude Code が Interop で stdio のサーバーとして起動)** | 高い。常駐の管理が要らず、Claude Code の起動とともに上がる | 中。操作は「ビルド」「テスト」「登録したアプリの起動と終了」の 3 つの型付きのツールに限られ、設定は Claude Code が書けない場所にある。ただしビルドとテストはログインユーザーの権限で動く(節 3 の残るリスク) | 低い。実行ファイルを置き、`claude mcp add` で登録するだけ。ビルドは Windows のネイティブの速度で、`/mnt/c` のソースを Windows から直接読む |
| C. Windows に常駐する HTTP のブリッジ(別ユーザーやサービスで動かす) | 中。常駐の管理が要る。Bash からは届かないので、結局 MCP のクライアントが要る | 中〜高。別ユーザーで動かせる。一方で、ポート、認証、ファイアウォールを自分で守る必要がある | 中。NAT と mirrored でつなぎ方が変わる |
| D. Windows Sandbox / Hyper-V の VM にブリッジを置く | 中。VM の起動とソースの同期が要る | 高い。ホストから隔離できる | 高い。Windows Sandbox は起動のたびに初期化され、GUI の確認も難しい |
| E. Linux でのクロスコンパイル(`EnableWindowsTargeting`) | **低い**。テストも起動もできず、WPF はビルドにも失敗した(節 0) | 高い | 低い |

**選定: B。**

自律性と導入のしやすさは A と並び、ほかの方式より高い。それでいて、Claude Code にできることを 3 つのツールに絞れる。A は、Bash サンドボックスという今ある境界を自分で外すことになる。C と D は隔離が強いが、一巡を回すために要らない常駐、認証、同期を先に背負うことになる。

ブリッジ(`WinDevBridge`)は stdio だけで話す。そのため、隔離を強めるときもツールの層は変えなくてよい。MCP の起動のコマンドを `ssh 低権限ユーザー@localhost WinDevBridge.exe` や `ssh VM WinDevBridge.exe` に替えれば、C や D と同じ隔離に進める(節 3.3)。

```
WSL2 (Ubuntu)                                        Windows 11 (ログインユーザー)
┌──────────────────────────────────────┐            ┌──────────────────────────────────────────┐
│ Claude Code (サンドボックスの外)     │─ stdio ───▶│ C:\Tools\WinDevBridge\WinDevBridge.exe   │
│   └ MCP クライアント "windev"        │  (Interop) │   bridge.json(Claude Code は書けない)    │
│                                      │            │   ├ build   → dotnet build               │
│ Bash サンドボックス (bubblewrap)     │            │   ├ test    → dotnet test                │
│   × Interop(seccomp で拒否)          │            │   └ run_app → 登録したアプリ(必ず終了)   │
│   × Windows ホストへのネットワーク   │            └──────────────────────────────────────────┘
└──────────────────────────────────────┘                     ▲ ソースは /mnt/c = C:\ を共有
```

### ブリッジのツール

| ツール | 引数 | すること |
|--------|------|----------|
| `build` | `project`(作業フォルダーからの相対パス。`.csproj` / `.vcxproj` / `.sln` / `.slnx`)、`configuration`(`Debug` / `Release`)、`platform`(省略可。`x64` / `Win32` / `x86` / `ARM64`) | C#: `dotnet build <project> --configuration <c> --nologo --verbosity minimal --disable-build-servers [--property:Platform=<p>]`<br>C++(`.vcxproj` と、それを含むソリューション): `MSBuild.exe <project> -restore -nologo -verbosity:minimal -nodeReuse:false -p:UseSharedCompilation=false -p:Configuration=<c> [-p:Platform=<p>]` |
| `test` | `project`(`.csproj` / `.sln` / `.slnx`、または C++ のテストのビルドした `.dll`)、`filter`(省略可。テストを絞る式) | C#: `dotnet test <project> --nologo --verbosity minimal --disable-build-servers [--filter <f>]`<br>C++: `vstest.console.exe <dll> [/TestCaseFilter:<f>]` |
| `run_app` | `app`(bridge.json に登録した名前)、`seconds`(1〜上限) | アプリを起動し、指定の秒数だけ観察した後に、窓を閉じる要求を送る。5 秒で終わらなければ、プロセスツリーごと強制終了する |

結果は、先頭行が `結果: 成功(終了コード 0)` / `結果: 失敗(終了コード 1)` / `結果: 時間切れ(600 秒で止めました)` の文面で返る。その後に、標準出力と標準エラー出力をそれぞれ末尾 12,000 文字まで付ける。断った要求は、理由を付けたエラー(MCP の `isError`)として返る。

### C++ / MFC / Win32(2026-10-15 に追加)

`dotnet build` は `.vcxproj` をビルドできず、`dotnet test` は C++ のネイティブのテストを実行できない。そこで、C++ のときだけ Visual Studio のツールに切り替える。ツールの層、パスの検証、1 つずつの処理、プロセスの扱い、出力の読み方は C# と同じものを使う(MSBuild.exe と vstest.console.exe はコードページ 932 で出力することがあるが、`ConsoleText` が判定して UTF-8 に揃える)。

| 場面 | 切り替え方 |
|------|------------|
| ビルド | `.vcxproj`、または中に `.vcxproj` を含む `.sln` / `.slnx` なら MSBuild.exe を使う(`ProjectFile.NeedsMSBuild`)。C# と C++ が混ざったソリューションも MSBuild.exe でビルドする(`-restore` で C# の NuGet も戻す) |
| テスト | `project` に `.dll` を渡したら vstest.console.exe で実行する。C++ のテストは、`build` でテストのプロジェクトをビルドしてから、できた DLL を渡す。`.vcxproj` を `test` に渡すと、DLL を渡すように促すエラーになる |
| ツールの場所 | `bridge.json` の `msbuildPath` / `vstestPath`(省略可。絶対パス)。省略すると、`vswhere.exe -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find ...` で、C++ のワークロードの入った最新の Visual Studio から探す。複数の版があり、古いツールセット(例: v142)のプロジェクトを古い版でビルドしたいときは、`msbuildPath` で版を固定する |
| プラットフォーム | C++ のプロジェクトは構成とプラットフォームの組(`Debug` と `x64` など)を持つので、`platform` を足した。値は一覧の中からだけ受ける(任意の文字列を通すと、`-p:` や `-t:` を差し込まれる)。C# にも同じ値を `--property:Platform` で渡す |

MFC も、プロジェクトのファイルに `UseOfMfc` があるだけで、ビルドの仕方は Win32 と同じである(Visual Studio に MFC のコンポーネントが要る。節 2.6)。GUI の起動と終了の確かめ方(`run_app`)も、C# と変わらない。

## 2. 構築の手順

印の意味: **[Claude]** は Claude Code がサンドボックスの中でできること、**[ユーザー]** はユーザーが行うこと(サンドボックスは設定ファイルとリポジトリの外に書けないため)。

### 2.1 ブリッジを発行する [Claude]

```bash
. $TMPDIR/dotnet-env.sh   # DOTNET_CLI_HOME / NUGET_PACKAGES / NUGET_CERT_REVOCATION_MODE=offline(節 2.4)
dotnet test src/WinDevBridge.Tests
dotnet publish src/WinDevBridge -c Release -r win-x64 --self-contained false -o artifacts/WinDevBridge
```

### 2.2 Windows 側に置く [ユーザー]

PowerShell(管理者でなくてよい)で実行する。

```powershell
$repo = 'C:\dropbox\dropbox\source\github\repos\Shos.WindowsDesktopApplicationDevelopmentTool'
New-Item -ItemType Directory -Force C:\Tools\WinDevBridge | Out-Null
Copy-Item -Recurse -Force "$repo\artifacts\WinDevBridge\*" C:\Tools\WinDevBridge\
Copy-Item -Force "$repo\src\WinDevBridge\bridge.sample.json" C:\Tools\WinDevBridge\bridge.json
```

置き場所を `C:\Tools` にしたのは、Claude Code のサンドボックスが書けるのが作業フォルダーの中だけだからである。Claude Code はブリッジの実行ファイルも `bridge.json` も変えられない。`bridge.json` の中身は次のとおり(`src/WinDevBridge/bridge.sample.json`)。

```json
{
  "workspaceRoot": "C:\\dropbox\\dropbox\\source\\github\\repos\\Shos.WindowsDesktopApplicationDevelopmentTool",
  "buildTimeoutSeconds": 600,
  "testTimeoutSeconds": 900,
  "maxObservationSeconds": 60,
  "apps": {
    "TemperatureConverter": "samples/TemperatureConverter/TemperatureConverter/bin/Debug/net10.0-windows/TemperatureConverter.exe"
  }
}
```

ブリッジは Windows の `PATH` にある `dotnet`(`C:\Program Files\dotnet\dotnet.exe`)を使う。C++ のサンプルを使うときは、`apps` に `"TemperatureConverterCpp": "samples/TemperatureConverterCpp/TemperatureConverterCpp/bin/x64/Debug/TemperatureConverterCpp.exe"` を足す(`bridge.sample.json` には入れてある)。MSBuild.exe と vstest.console.exe の場所を固定したいときは、`"msbuildPath"` と `"vstestPath"` に絶対パスを書く(節 1 の「C++ / MFC / Win32」)。

### 2.3 Claude Code に登録する [ユーザー]

WSL のシェル(Claude Code の外)で実行する。`--scope local` にすると登録は `~/.claude.json` に入り、リポジトリには入らない。

```bash
claude mcp add --scope local --transport stdio windev -- /mnt/c/Tools/WinDevBridge/WinDevBridge.exe
```

続けて、`.claude/settings.json` に節 3.2 の設定を入れる。テストの時間切れ(900 秒)より MCP のツールの時間切れが先に来ないように、`MCP_TOOL_TIMEOUT` を 960000(ミリ秒)にしておく。Claude Code を起動し直し、`/mcp` で `windev` が connected になっていることを確かめる。

### 2.4 WSL 側の設定

- **`/etc/wsl.conf`**: Interop は有効のままにする(ブリッジの起動に要る)。Windows の `PATH` を Linux に混ぜない設定は任意である。サンドボックスの中ではどのみち Windows のコマンドは失敗するが、混ぜなければ、サンドボックスを外したときに `dotnet` が Windows のものに化けない。変えたら `wsl --shutdown` で再起動する。

  ```ini
  [interop]
  enabled = true
  appendWindowsPath = false
  ```

- **環境変数**(Linux 側の dotnet でブリッジ自身を開発するときだけ要る): サンドボックスではホームに書けない。NuGet の証明書の失効確認の通信も拒まれる。そのため、次を `.claude/settings.json` の `env` に入れる(今は `$TMPDIR/dotnet-env.sh` を読み込んで代用している)。

  ```json
  "env": {
    "DOTNET_CLI_HOME": "/tmp/claude-1000/dotnet-home",
    "NUGET_PACKAGES": "/tmp/claude-1000/dotnet-home/nuget-packages",
    "NUGET_CERT_REVOCATION_MODE": "offline",
    "DOTNET_NOLOGO": "1",
    "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    "MCP_TOOL_TIMEOUT": "960000"
  }
  ```

- **ラッパースクリプト**: 作らない。引数の検証とプロセスの後始末は、型付きの引数を受けるブリッジ(C#)が受け持つ。bash のラッパーで同じことをすると、引用符とパスの変換の検証が脆くなる。

### 2.5 ファイルの置き場所

- **ソースは Windows のファイルシステム(`/mnt/c/...`)に置く。** ビルドもテストも Windows で動くからである。WSL のネイティブの領域(`\\wsl$\...`)に置くと、Windows の `dotnet.exe` がネットワークのファイルシステム越しに読むことになり、遅く、ファイルの変更の監視も効かない。Claude Code のファイル操作は 9P 越しになって遅くなるが、編集は小さいので問題にならない。
- **Dropbox の同期から `bin` / `obj` / `artifacts` を外す。** ビルドのたびに大量のファイルが書き換わり、同期がファイルを握ってビルドとぶつかるからである。フォルダーができてから、PowerShell で実行する(Dropbox の公式の除外の方法)。

  ```powershell
  Get-ChildItem -Recurse -Directory -Include bin,obj,artifacts $repo |
    ForEach-Object { Set-Content -Path $_.FullName -Stream com.dropbox.ignored -Value 1 }
  ```

  根本的な解決は、作業コピーを Dropbox の外(例 `C:\src`)に置き、同期を git に任せることである。
- **ファイルロックを避ける仕組み**(ブリッジに組み込み済み):
  - `--disable-build-servers` で、MSBuild のノードと VBCSCompiler を残さない。
  - 要求は 1 つずつ処理する(ビルドとテストが同じ `obj` を取り合わない)。
  - `run_app` はアプリを必ず終わらせる(起動したままの exe が次のビルドの上書きを妨げない)。

### 2.6 C++ / MFC / Win32 のための準備 [ユーザー]

- **Visual Studio のワークロード**: Visual Studio Installer で「C++ によるデスクトップ開発」を入れる。MSVC のツールセット、Windows SDK、MSBuild、Microsoft C++ Unit Test Framework、vstest.console.exe が入る。MFC を使うなら、同じワークロードの「最新の v14x ビルド ツール用 C++ MFC」も入れる。Build Tools for Visual Studio だけでもよい(vswhere の `-products *` で見つかる)。
- **プロジェクトのファイル**(`samples/TemperatureConverterCpp` の `.vcxproj` がこの形になっている):
  - `<Platform Condition="'$(Platform)' == ''">x64</Platform>` を先頭に置く。`platform` を省略されたときに、ない構成(`Debug` と `Win32` の組)にならない。
  - `OutDir` / `IntDir` を `$(MSBuildProjectDirectory)\bin\...` / `obj\...` にする。ソリューションからでも単独でもビルドの出力の場所が同じになり、`run_app` の登録と `test` に渡す DLL のパスが一つに決まる。`.gitignore` と Dropbox の除外(節 2.5)にもそのまま入る。
  - `/utf-8` を付ける。日本語の Windows では、BOM のない UTF-8 のソースがコードページ 932 として読まれ、文字化けや C4819 の警告になる。WSL の Claude Code は BOM のない UTF-8 で書く。
  - `PlatformToolset` を `$(DefaultPlatformToolset)` にすると、入っている Visual Studio の版に合わせる。既存のプロジェクトで版が決まっている(例: `v142`)ときは、そのツールセットを入れるか、`msbuildPath` でその版の MSBuild.exe を使う。
  - テストのプロジェクトは `<ProjectSubType>NativeUnitTestProject</ProjectSubType>` を付けた DLL にする。Microsoft C++ Unit Test Framework のヘッダーとライブラリの場所が自動で足される。
- **Google Test**: このブリッジの `test` では扱わない(vstest.console.exe で動かすには Test Adapter for Google Test の場所を渡す必要があり、今の一巡には要らない)。テストの実行ファイルを `apps` に登録して `run_app` で動かすと、終了コードと標準出力は読めるが、GUI 向けの観察になる。

## 3. セキュリティの境界と権限の制御

### 3.1 境界の層

| 層 | 防ぐこと | 仕組み |
|----|----------|--------|
| Bash サンドボックス | Bash から Windows のコマンドを呼ぶこと、Windows ホストへのネットワーク | seccomp による AF_UNIX の拒否(Interop が使えない)、ネットワークの名前空間。`allowAllUnixSockets` も、Windows のコマンドの `excludedCommands` も**設定しない** |
| Claude Code の権限 | ブリッジ、その設定、MCP の登録、権限の設定そのものを書き換えること | 節 3.2 の `permissions` |
| ブリッジ | 許した 3 つの操作の外 | ツールは `build` / `test` / `run_app` だけ。パスは作業フォルダーの中の実在するプロジェクト(テストでは、それに加えてビルドした DLL)に限る(絶対パスと `..` での脱出を断る)。構成は `Debug` / `Release` だけ。プラットフォームは `x64` / `Win32` / `x86` / `ARM64` だけ。MSBuild.exe と vstest.console.exe の場所は `bridge.json` か vswhere からだけ決まり、要求では変えられない。アプリは `bridge.json` に登録した名前だけ。観察の秒数には上限がある。シェルを通さずに引数を要素の並びで渡す。子プロセスに標準入力を引き継がせない。時間切れのときはプロセスツリーごと止める |
| 設定の置き場所 | ブリッジの規則を Claude Code が緩めること | `bridge.json` は実行ファイルの隣からだけ読み、引数や環境変数では変えられない。置き場所(`C:\Tools`)はサンドボックスの書ける範囲の外 |

### 3.2 Claude Code の権限の設定(`.claude/settings.json`)

```json
{
  "permissions": {
    "allow": [
      "mcp__windev__build",
      "mcp__windev__test",
      "mcp__windev__run_app"
    ],
    "ask": [
      "Edit(**/*.csproj)",
      "Edit(**/*.vcxproj)",
      "Edit(**/*.props)",
      "Edit(**/*.targets)",
      "Edit(**/nuget.config)",
      "Edit(**/global.json)"
    ],
    "deny": [
      "Edit(//mnt/c/Tools/**)",
      "Edit(~/.claude.json)",
      "Edit(.mcp.json)",
      "Edit(.claude/settings.json)",
      "Edit(.claude/settings.local.json)",
      "Bash(dangerouslyDisableSandbox:true)"
    ]
  },
  "sandbox": {
    "enabled": true,
    "allowUnsandboxedCommands": false
  },
  "env": {
    "MCP_TOOL_TIMEOUT": "960000"
  }
}
```

- `allow` は、ブリッジの 3 つのツールを確認なしで使えるようにする。自律のループが確認待ちで止まらないためである。
- `ask` は、ビルドの定義(MSBuild のプロジェクト(`.csproj` / `.vcxproj`)、props、targets、NuGet の設定、SDK の固定)を変える編集に、人の承認を求める。C++ では、ビルド前後のイベント(`PreBuildEvent` / `PostBuildEvent`)や `CustomBuild` も `.vcxproj` の中にあるので、ここで止まる。ビルドの定義は、ビルドのときに任意のコマンドを走らせられる場所だからである(節 3.3)。新しいプロジェクトを作る作業では確認が増えるので、そのときだけ緩めてよい。
- `deny` は、境界そのもの(ブリッジ、その設定、MCP の登録、権限の設定)を書き換えさせない。Write や Edit のツールは Bash サンドボックスの外で動くので、ここで止める必要がある。
- `allowUnsandboxedCommands: false` は、サンドボックスの外での再実行という抜け道を閉じる。

**PreToolUse フック(任意、多層防御)。** サンドボックスが無効にされたときに備え、Bash から Windows の実行ファイルを呼ぶコマンドを止める。`.claude/hooks/deny-windows-exe.sh` を作り、`settings.json` の `hooks.PreToolUse` に `matcher: "Bash"` で登録する。

```bash
#!/usr/bin/env bash
# Bash から Windows の実行ファイルを直接呼ばせない。Windows での操作は MCP サーバー windev を通す。
command=$(jq -r '.tool_input.command // ""')
if grep -qiE '(\.exe|/mnt/c/windows/)' <<<"$command"; then
  echo "Windows の実行ファイルは Bash から呼べません。mcp__windev__build / test / run_app を使ってください。" >&2
  exit 2
fi
```

### 3.3 残るリスクと、それを抑える分離

**ビルドとテストを許すことは、Windows のログインユーザーの権限で任意のコードを動かすことを許すのと同じである。** Claude Code はソース、プロジェクトのファイル、テストのコードを編集できる。そのため、MSBuild の `Exec` タスクや独自の targets、NuGet のパッケージの build props、テストのコードそのもの、`run_app` で起動するアプリが、ログインユーザーの権限で何でもできる。ブリッジのパスや引数の検証は、ツールの使い方を絞るものであり、これは防げない。

もう一つある。Windows で動く子プロセスには、Claude Code のネットワークのフィルターが効かない。

抑え方を、強さの順に段階で示す。

| 段階 | すること | 防げること | 費用 |
|------|----------|------------|------|
| 1(今回) | 節 3.2 の `ask` で、ビルドの定義の変更に人の承認を求める。変更はすべて git の差分で見える | ビルドの定義を経由した気づかれない実行。テストのコードとアプリのコードを経由した実行は残る(C++ のテストの DLL は vstest のプロセスに読み込まれて動く) | 低い |
| 2 | Windows に低権限のローカルユーザーを作り、作業フォルダーだけに書き込み権限を与える。OpenSSH サーバーを有効にし、MCP の登録を `ssh -T bridge@localhost C:/Tools/WinDevBridge/WinDevBridge.exe` に替える | ログインユーザーのファイル、資格情報、ブラウザーなどへの到達 | 中。ssh の鍵の管理、作業フォルダーの ACL の設定 |
| 3 | 段階 2 と同じ ssh の登録で、接続先を Hyper-V の VM にする。ソースは VM と共有する | ホストそのものへの到達 | 高い。VM の運用、共有フォルダーの速度 |

ブリッジは stdio だけで話すので、段階 2 と 3 で変わるのは MCP の登録のコマンドだけである。最初の一巡は段階 1 で回した。段階 2 以降に進むかは、扱うコードの信頼の度合いでユーザーが決める。

## 4. 対象のプロジェクト用の `CLAUDE.md` のテンプレート

`<...>` を対象のプロジェクトに合わせて書き換える。

````markdown
## Windows でのビルド・テスト・起動

このリポジトリは Windows デスクトップアプリ(<C# / .NET 10 / WPF、または C++ / MFC / Win32>)である。WSL の Bash では、Linux の dotnet で WPF をビルドできず、Windows のコマンドもサンドボックスで失敗する。それでも、**MCP サーバー `windev` を使えば Windows 側でビルド・テスト・起動ができる。人に頼まずに、次のツールを使う。**

| やりたいこと | ツール | 引数の例 |
|--------------|--------|----------|
| ビルド(C#) | `mcp__windev__build` | `project: "<src/App/App.csproj>"`, `configuration: "Debug"` |
| ビルド(C++) | `mcp__windev__build` | `project: "<src/App/App.vcxproj>"`(またはソリューション), `configuration: "Debug"`, `platform: "x64"` |
| 単体テスト(C#) | `mcp__windev__test` | `project: "<tests/App.Tests/App.Tests.csproj>"`, `filter: "FullyQualifiedName~<Calculator>"` |
| 単体テスト(C++) | `mcp__windev__build` の後に `mcp__windev__test` | `build` で `project: "<tests/App.Tests/App.Tests.vcxproj>"` をビルドし、`test` に `project: "<tests/App.Tests/bin/x64/Debug/App.Tests.dll>"` を渡す |
| アプリの起動と終了 | `mcp__windev__run_app` | `app: "<App>"`, `seconds: 10` |

- パスは、リポジトリ直下からの相対パスを `/` 区切りで書く。`/mnt/c/...` や `C:\...` の絶対パスと、リポジトリの外は断られる。
- Bash で `dotnet build` / `dotnet test` / `cmd.exe` / `powershell.exe` / `*.exe` を使わない。
- 回し方: 編集 → `test`(新しいテストが失敗することを確かめる)→ 修正 → `test`(すべて成功)→ 画面に関わる変更なら `run_app`。
- C++ では、`test` はビルドしない。テストの前に、必ずテストのプロジェクトを `build` する(古い DLL のテストを読まないため)。アプリを `run_app` する前も、アプリのプロジェクトを `build` する。
- C++ のソースは BOM のない UTF-8 で書く。プロジェクトに `/utf-8` が無いと、日本語のコメントや文字列で C4819 の警告や文字化けが出る。そのときは、`.vcxproj` に `/utf-8` を足す編集の承認をユーザーに求める。

### 出力の読み方

- 結果の先頭行は `結果: 成功(終了コード 0)` / `結果: 失敗(終了コード n)` / `結果: 時間切れ(n 秒で止めました)` のどれかである。失敗のときは、標準出力の `error CS....`(C#)、`error C....` / `error LNK....`(C++)や、テストの `Failed` の行を読む。
- ブリッジは子プロセスの出力を UTF-8 か Shift-JIS(コードページ 932)かを判定し、UTF-8 に揃えて返す。Claude Code の側で文字コードを変換しない。それでも文字化けしていたら、そのまま報告する。
- 出力は、標準出力と標準エラー出力のそれぞれ末尾 12,000 文字だけが返る(`(先頭の n 文字を省略)` と出る)。足りないときは `filter` で対象を絞る。

### GUI アプリ

- アプリを Bash や `start` で起動しない。`run_app` は、指定した秒数の後に必ず終わらせる(窓を閉じる要求を送り、5 秒で終わらなければ強制終了)ので、ハングで止まらない。
- 判定の仕方:
  - `観察の後、ウィンドウを閉じて終了させました` と `メインウィンドウ: "<タイトル>"(応答あり)` が出れば、正常に起動して閉じた。
  - `観察のうちに自分で終了しました` は、起動直後の異常終了を疑い、終了コードと標準エラー出力を読む。
  - `(応答なし)` はハングを示す。UI スレッドを止めている処理を探す。
  - `メインウィンドウ: なし` は、窓が出ていないことを示す。
- 画面の中身は `run_app` では確かめられない。ロジックを画面から分け、単体テストで確かめる。

### 人に頼んでよい場面

次の場面に限っては止めて、ユーザーに頼む。

- `windev` のツールが見つからない、またはつながらない(`/mcp` で確かめる)。
- `bridge.json` を変える必要がある(アプリの登録、時間切れの延長、MSBuild.exe の版の固定など)、またはブリッジを更新したい。
- C++ のビルドが「Visual Studio が見つかりません」や、ツールセット(`MSB8020` など)、MFC のライブラリの不足で失敗した(Visual Studio のコンポーネントの追加が要る)。
- 権限で拒まれた操作が、作業にどうしても要る。
- ビルドの定義(`*.csproj`、`*.props`、`*.targets`、`nuget.config`、`global.json`)を変える編集の承認。

頼むときは、次の 3 点を書く。

1. 何をしようとしたか。
2. どのツールに何を渡し、何が返ったか(エラーの全文)。
3. ユーザーにしてほしいこと(例: 「bridge.json の apps に `"App": "src/App/bin/Debug/net10.0-windows/App.exe"` を足して、Claude Code を起動し直してください」)。
````

## 検証の記録

| 日時 | 何を確かめたか | 結果 |
|------|----------------|------|
| 2026-10-14 | ブリッジの単体テスト(Linux 側の dotnet 10.0.112) | 45 件すべて成功 |
| 2026-10-14 | ブリッジを Linux で起動し、MCP の initialize → tools/list → tools/call を送った | 3 つのツールが公開された。ブリッジ自身の `build` が成功した。作業フォルダーの外を指す `build` は、理由付きのエラー(`isError: true`)で返った |
| 2026-10-14 | `dotnet publish -r win-x64` | 警告 0 で `artifacts/WinDevBridge` に出力 |
| (未) | Windows でブリッジを起動し、Claude Code から `build` / `test` / `run_app` を呼ぶ(MCP のサーバーが Interop を使えることの実機での確認を含む) | 節 2.2〜2.3 の反映待ち |
| (未) | サンプルで「編集 → テスト(失敗)→ 修正 → テスト(成功)→ 起動と終了」を一巡回す | 同上 |
| 2026-10-15 | C++ への拡張のブリッジの単体テスト(Linux 側の dotnet 10.0.112)。MSBuild.exe、vstest.console.exe、vswhere の代わりに、引数を出すだけのスクリプトを置いて確かめた | 79 件すべて成功 |
| 2026-10-15 | ブリッジを Linux で起動し、MCP で tools/list と C++ の要求を送った | `build` に `platform` が出た。`.slnx`(C++ を含む)の `build` は「Visual Studio が見つかりません ... msbuildPath」、`.vcxproj` の `test` は DLL を促すエラー、`platform: "Any CPU"` は一覧外のエラーで返った |
| (未) | Windows で、`samples/TemperatureConverterCpp` を `build`(MSBuild.exe を vswhere で探す)→ テストの DLL を `test`(vstest.console.exe。x64 の DLL のプラットフォームを vstest が自分で判定するかを含む)→ `run_app` | 節 2.2〜2.3 の反映待ち。サンプルの `.vcxproj` も、まだ一度も Windows でビルドしていない |
