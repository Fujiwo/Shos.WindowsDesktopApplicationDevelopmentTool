# Shos.WindowsDesktopApplicationDevelopmentTool

[English](README.md)

**WSL2 のサンドボックスで動く Claude Code** が、**Windows デスクトップアプリ**(C# / .NET / WPF / WinForms と、C++ / MFC / Win32)のビルド、テスト、起動を自分で行えるようにする。「Windows でビルドしてください」と人に頼んで止まることをなくし、それでいて Windows のホストは守る。

このリポジトリには、次のものがある。

- **WinDevBridge**: C# / .NET 10 で書いた小さな MCP stdio サーバー。Windows で動き、`build`、`test`、`run_app` の 3 つのツールだけを公開する。C# には `dotnet` を、C++ には Visual Studio の `MSBuild.exe` と `vstest.console.exe` を使う。
- **設計の文書**: 実現方式の比較、構築の手順、セキュリティの境界、対象のプロジェクト用の `CLAUDE.md` のテンプレート。
- **2 つのサンプルアプリ**: 「編集 → ビルド → テスト → 修正」の一巡を確かめるのに使う。`TemperatureConverter`(C# / WPF と xUnit のテスト)と `TemperatureConverterCpp`(C++ / Win32 と Microsoft C++ Unit Test Framework のテスト)。

## 解く問題

Claude Code を WSL2 で、Bash サンドボックス(bubblewrap)を有効にして動かすと、次のことが起きる。

1. Linux だけでは、Windows ネイティブのアプリをビルドも実行もできない。`EnableWindowsTargeting=true` を付けても WPF のプロジェクトは Linux でビルドに失敗し、テストも GUI アプリも動かせない。
2. サンドボックスの中では WSL Interop が使えない。`cmd.exe` や `powershell.exe` を呼ぶと `UtilConnectUnix:534: socket failed` で失敗する。seccomp が AF_UNIX のソケットの作成を拒むからである。
3. そのため Claude Code はビルドを人に頼んで止まり、自律のループが途切れる。

Bash に Interop を開けば 1〜3 は解けるが、Bash のどのコマンドからも Windows の任意のプログラムを動かせるようになってしまう。

## このリポジトリのポイント

### 1. サンドボックスを開けずに、Windows 側の MCP サーバーを使う

MCP の stdio サーバーは、Claude Code 自身が Bash サンドボックスの**外**で起動する。WinDevBridge はこれを使う。Claude Code が WSL Interop で `WinDevBridge.exe` を起動し、標準入出力で話す。Bash サンドボックスは閉じたままでよい。

```
WSL2 (Ubuntu)                                        Windows 11 (ログインユーザー)
┌──────────────────────────────────────┐            ┌──────────────────────────────────────────┐
│ Claude Code (サンドボックスの外)     │─ stdio ───▶│ C:\Tools\WinDevBridge\WinDevBridge.exe   │
│   └ MCP クライアント "windev"        │  (Interop) │   bridge.json(Claude Code は書けない)    │
│                                      │            │   ├ build   → dotnet build               │
│ Bash サンドボックス (bubblewrap)     │            │   ├ test    → dotnet test                │
│   × Interop(seccomp で拒否)          │            │   └ run_app → 登録したアプリ(必ず終了)   │
│   × Windows ホストへのネットワーク   │            │   (C++: MSBuild.exe / vstest.console)    │
│                                      │            └──────────────────────────────────────────┘
└──────────────────────────────────────┘                     ▲ ソースは /mnt/c = C:\ を共有
```

5 つの方式(Bash への Interop の解放、Windows の MCP サーバー、常駐の HTTP のブリッジ、Windows Sandbox / Hyper-V の VM、Linux でのクロスコンパイル)を、自律性、セキュリティ、導入と運用のコストで比べ、MCP サーバーを選んだ。比較は[設計の文書](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md)の節 1 にある。

### 2. 型付きの 3 つの操作だけを許す

| ツール | 引数 | すること |
|--------|------|----------|
| `build` | `project`(`.csproj` / `.vcxproj` / `.sln` / `.slnx` への相対パス)、`configuration`(`Debug` / `Release`)、`platform`(省略可。`x64` / `Win32` / `x86` / `ARM64`) | C#: `dotnet build ... --disable-build-servers`<br>C++(`.vcxproj` と、それを含むソリューション): `MSBuild.exe ... -nodeReuse:false` |
| `test` | `project`(テストのプロジェクトかソリューション、または C++ のテストのビルドした `.dll`)、`filter`(省略可) | C#: `dotnet test ... --disable-build-servers`<br>C++: `vstest.console.exe <dll> /TestCaseFilter:...` |
| `run_app` | `app`(`bridge.json` に登録した名前)、`seconds` | アプリを起動して観察した後に、**必ず**終わらせる(窓を閉じる要求を送り、5 秒で終わらなければプロセスツリーごと強制終了) |

ブリッジは、要求を次のように検証する。

- パスは、作業フォルダーの中の実在するプロジェクトに限る。絶対パスと `..` での脱出は断る。
- 構成とプラットフォームは決まった一覧の中からだけ受ける。`-p:` や `-t:` のスイッチを差し込まれない。
- 起動できるのは登録したアプリだけで、観察の秒数には上限がある。
- 引数はシェルを通さず、要素の並びで渡す。子プロセスには標準入力を渡さない。
- 時間切れのときはプロセスツリーごと止める。要求は 1 つずつ処理し、ビルドとテストが同じ `obj` を取り合わないようにする。
- `bridge.json` は実行ファイルの隣(`C:\Tools\WinDevBridge`)からだけ読む。そこはサンドボックスが書ける範囲の外で、引数や環境変数でも変えられない。

### 3. Claude Code が読める出力

- 先頭行は必ず、成功(終了コード 0)、失敗(終了コード n)、時間切れ のどれかである。
- 子プロセスの出力が UTF-8 か Shift-JIS(コードページ 932)かを判定し、UTF-8 に揃えて返す。`dotnet`、`MSBuild.exe`、`vstest.console.exe` の日本語のメッセージも化けない。
- 標準出力と標準エラー出力を、それぞれ末尾の 12,000 文字に切り詰める。エラーはたいてい末尾にある。
- `run_app` は、メインウィンドウが出たか、応答しているか(ハングの検出)、自分で終了したか(起動直後の異常終了)を返す。

### 4. 隠さないセキュリティのモデル

ビルドとテストを許すことは、**Windows のログインユーザーの権限で任意のコードを動かすことを許すのと同じ**である。Claude Code は MSBuild の `Exec` タスク、独自の targets、NuGet の build props、テストのコードそのものを編集できる。引数の検証ではこれを防げない。設計の文書はこのリスクをそのまま書き、隔離の段階を 3 つ示す。

1. (今回)Claude Code の権限の設定で、ビルドの定義(`*.csproj`、`*.vcxproj`、`*.props`、`*.targets`、`nuget.config`、`global.json`)の編集に人の承認を求める。C++ のビルド前後のイベントやカスタムビルドも、これで止まる。
2. ブリッジを `ssh` 越しに、低権限のローカルユーザーで動かす。
3. ブリッジを `ssh` 越しに、Hyper-V の VM で動かす。

ブリッジは標準入出力だけで話すので、段階 2 や 3 に進むときに変わるのは MCP の登録のコマンドだけである。

### 5. 対象のプロジェクト用の `CLAUDE.md` のテンプレート

設計の文書の節 4 は、そのまま写せる `CLAUDE.md` の節である。使うツール、Bash から `dotnet` や `*.exe` を呼ばないこと、結果の読み方、GUI の起動の判定の仕方、人に頼んでよい場面を Claude Code に伝える。

## リポジトリの構成

| 場所 | 中身 |
|------|------|
| `src/WinDevBridge` | ブリッジ(MCP stdio サーバー。.NET 10、[ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) の SDK) |
| `src/WinDevBridge.Tests` | ブリッジの xUnit のテスト(Linux でも Windows でも動く) |
| `samples/TemperatureConverter` | WPF のサンプルアプリと xUnit のテスト(Windows でしかビルドできない) |
| `samples/TemperatureConverterCpp` | C++ / Win32 のサンプルアプリと Microsoft C++ Unit Test Framework のテスト(Windows の Visual Studio でしかビルドできない) |
| `Plans/` | 設計の文書: 方式の比較、構築の手順、セキュリティの境界、`CLAUDE.md` のテンプレート |
| `.github/prompts/` | このプロジェクトの目的と要件を定めたプロンプト |
| `.claude/skills/` | このリポジトリの開発で使ったコーディング規範のスキル |
| `docs/for-skills/` | そのスキルを評価するための記録 |

## 使い始める

詳しくは[設計の文書](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md)の節 2 にある。

### 要るもの

- Windows 11 と WSL2 (Ubuntu)
- Windows 側の .NET SDK 10(`C:\Program Files\dotnet\dotnet.exe`)と、WPF 用の Windows デスクトップのランタイム
- Linux 側の .NET SDK 10(ブリッジのビルドに使う)
- WSL2 で動く Claude Code
- C++ / MFC / Win32 を扱うときだけ: 「**C++ によるデスクトップ開発**」のワークロードを入れた Visual Studio か Build Tools for Visual Studio(MFC を使うなら「**C++ MFC**」のコンポーネントも)

### 1. ブリッジをビルドして発行する(WSL)

```bash
dotnet test src/WinDevBridge.Tests
dotnet publish src/WinDevBridge -c Release -r win-x64 --self-contained false -o artifacts/WinDevBridge
```

Claude Code のサンドボックスの中では、ホームに書けず、NuGet の証明書の失効確認も拒まれる。先に `DOTNET_CLI_HOME` と `NUGET_PACKAGES` を `$TMPDIR` の下に向け、`NUGET_CERT_REVOCATION_MODE=offline` にする。

### 2. Windows に置く(PowerShell。管理者でなくてよい)

```powershell
$repo = 'C:\path\to\Shos.WindowsDesktopApplicationDevelopmentTool'
New-Item -ItemType Directory -Force C:\Tools\WinDevBridge | Out-Null
Copy-Item -Recurse -Force "$repo\artifacts\WinDevBridge\*" C:\Tools\WinDevBridge\
Copy-Item -Force "$repo\src\WinDevBridge\bridge.sample.json" C:\Tools\WinDevBridge\bridge.json
```

続けて `C:\Tools\WinDevBridge\bridge.json` を書き換える。

```json
{
  "workspaceRoot": "C:\\path\\to\\your\\repository",
  "buildTimeoutSeconds": 600,
  "testTimeoutSeconds": 900,
  "maxObservationSeconds": 60,
  "apps": {
    "TemperatureConverter": "samples/TemperatureConverter/TemperatureConverter/bin/Debug/net10.0-windows/TemperatureConverter.exe",
    "TemperatureConverterCpp": "samples/TemperatureConverterCpp/TemperatureConverterCpp/bin/x64/Debug/TemperatureConverterCpp.exe"
  }
}
```

C++ のとき、ブリッジは `vswhere.exe` で、C++ のワークロードの入った最新の Visual Studio から `MSBuild.exe` と `vstest.console.exe` を探す。版を固定したいとき(例: `v142` のプロジェクトを Visual Studio 2019 でビルドする)は、`"msbuildPath"` と `"vstestPath"` に絶対パスを書く。

### 3. Claude Code に登録する(WSL のシェル。Claude Code の外で)

```bash
claude mcp add --scope local --transport stdio windev -- /mnt/c/Tools/WinDevBridge/WinDevBridge.exe
```

`.claude/settings.json` に、設計の文書の節 3.2 の権限を入れる。`mcp__windev__*` を許し、ビルドの定義の編集は確認を求め、ブリッジとその設定の編集は拒み、サンドボックスは有効のままにする。テストの時間切れより MCP の時間切れが先に来ないように、`MCP_TOOL_TIMEOUT` を `960000` にする。Claude Code を起動し直し、`/mcp` で `windev` が connected になっていることを確かめる。

### 4. 使う

設計の文書の節 4 のテンプレートを、対象のプロジェクトの `CLAUDE.md` に写す。あとは Claude Code が自分で次のループを回す。

編集 → `test`(新しいテストが失敗する)→ 修正 → `test`(すべて成功)→ `run_app`(画面に関わる変更のとき)

## C++ / MFC / Win32

`dotnet build` は `.vcxproj` をビルドできず、`dotnet test` は C++ のネイティブのテストを実行できない。そこでブリッジは、C++ のときだけ Visual Studio のツールに切り替える。パスの検証、1 つずつの処理、時間切れ、出力の読み方、`run_app` は C# と同じである。

| すること | ツールの呼び方 |
|----------|----------------|
| アプリをビルドする | `build` に `project: "samples/TemperatureConverterCpp/TemperatureConverterCpp.slnx"`, `platform: "x64"` |
| テストをビルドする | `build` にテストの `.vcxproj`(またはソリューション) |
| テストを実行する | `test` に `project: "samples/TemperatureConverterCpp/TemperatureConverterCpp.Tests/bin/x64/Debug/TemperatureConverterCpp.Tests.dll"` |
| アプリを起動して終わらせる | `run_app` に `app: "TemperatureConverterCpp"` |

C++ では、`test` はビルドしない。古い DLL をテストしないように、必ず先にテストのプロジェクトを `build` する。`test` に `.vcxproj` を渡すと、DLL を渡すように促すエラーが返る。

`.vcxproj` に入れておくとよい設定(サンプルはすべて入れてある):

- 先頭に `<Platform Condition="'$(Platform)' == ''">x64</Platform>`。`platform` を省略したときに、ない構成(`Win32`)にならない。
- `OutDir` / `IntDir` を `$(MSBuildProjectDirectory)\bin\...` と `obj\...` に。単独でビルドしてもソリューションからビルドしても、出力の場所が同じになる。`.gitignore` と Dropbox の除外にもそのまま入る。
- `/utf-8`。Claude Code は BOM のない UTF-8 で書くが、日本語の Windows はそれをコードページ 932 として読んでしまう。
- テストのプロジェクトは、`<ProjectSubType>NativeUnitTestProject</ProjectSubType>` を付けた DLL に。Microsoft C++ Unit Test Framework の場所が自動で足される。

MFC のプロジェクトも、`UseOfMfc` が付くだけで、同じようにビルドできる。Google Test は、まだ `test` で扱えない。

## ヒント

- **ソースは Windows のファイルシステム(`/mnt/c/...`)に置く。** ビルドは Windows で動くからである。WSL のファイルシステムに置くと、Windows の `dotnet.exe` がネットワークのファイルシステム越しに読むことになり、遅い。
- **`bin`、`obj`、`artifacts` を Dropbox や OneDrive の同期から外す。** 同期のクライアントがビルドの最中にファイルを握るからである。Dropbox では、そのフォルダーに `com.dropbox.ignored` のストリームを付ける。コマンドは設計の文書にある。

## 現状

| 項目 | 状況 |
|------|------|
| 設計の文書(方式、構築、セキュリティ、`CLAUDE.md` のテンプレート) | 済み |
| ブリッジの単体テスト(C++ の対応を含めて 79 件) | Linux ですべて成功 |
| Linux での MCP の接続とツールの呼び出し(C++ の要求を含む) | 確認済み |
| `dotnet publish -r win-x64` | 済み(警告 0) |
| Windows で Claude Code から `build` / `test` / `run_app` を呼ぶ | 未確認 |
| サンプルアプリで自律のループを一巡回す | 未確認 |
| Windows で C++ のサンプルをビルドしてテストする(`MSBuild.exe`、`vstest.console.exe`) | 未確認。サンプルの `.vcxproj` も、まだ Windows で一度もビルドしていない |
