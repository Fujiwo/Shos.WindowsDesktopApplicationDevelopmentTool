# Shos.WindowsDesktopApplicationDevelopmentTool

[English](README.md)

**WSL の Claude Code が、Windows デスクトップアプリのビルド、テスト、起動を自分で行えるようにする。**

WSL2(Ubuntu)で動く Claude Code は、ふつうは WPF や C++ のアプリをビルドできない。そのため「Windows でビルドして、結果を教えてください」と言って止まり、人が代わりに作業するまで開発が進まない。

このリポジトリは、その壁を取り除く。これを使うと、Claude Code は Windows デスクトップアプリの **編集 → ビルド → テスト → エラーの発見 → 修正** を自分で繰り返せる。それでいて、Claude Code が Windows でできることは、**ビルド**、**単体テストの実行**、**登録したアプリの起動と終了** の 3 つだけである。

| 対象 | 状況 |
|------|------|
| C# / .NET: WPF、WinForms | 対応(`dotnet build` / `dotnet test`) |
| C++: Win32、MFC | 対応(Visual Studio の `MSBuild.exe` / `vstest.console.exe`) |
| C# / .NET: WinUI 3 | `dotnet` で動くはずだが、まだ確かめていない |

---

## 目次

1. [1 分で分かるポイント](#1-分で分かるポイント)
2. [仕組み](#仕組み)
3. [このリポジトリにあるもの](#このリポジトリにあるもの)
4. [手順書: Claude Code で新しい Windows デスクトップアプリを開発する](#手順書-claude-code-で新しい-windows-デスクトップアプリを開発する)
   - [A. PC ごとに一度だけ行う準備](#a-pc-ごとに一度だけ行う準備)
   - [B. 新しいアプリを始める](#b-新しいアプリを始める)
   - [C. Claude Code との毎日の開発](#c-claude-code-との毎日の開発)
   - [D. Claude Code から頼まれたとき](#d-claude-code-から頼まれたとき)
5. [セキュリティ: Claude Code にできること、できないこと](#セキュリティ-claude-code-にできることできないこと)
6. [現状](#現状)
7. [ブリッジそのものを開発する](#ブリッジそのものを開発する)

---

## 1 分で分かるポイント

1. **壁。** WSL の Claude Code のサンドボックスの中では、Linux の `dotnet` は WPF も C++ もビルドできず、Windows のプログラム(`cmd.exe`、`dotnet.exe` など)の呼び出しも拒まれる。
2. **扉。** Claude Code は MCP サーバーを、サンドボックスの**外**で起動する。そこで、Windows に小さな MCP サーバー **WinDevBridge** を置く。Claude Code がそれを呼び、それが Windows でビルドする。サンドボックスは閉じたままでよい。
3. **操作は 3 つだけ。** WinDevBridge が受けるのは `build`、`test`、`run_app` だけである。パスと引数をすべて検証し、設定は Claude Code が書けない場所に置く。
4. **読める結果。** 結果は必ず、成功・失敗・時間切れのどれかを示す 1 行から始まる。日本語(Shift-JIS)のコンパイラーの出力も UTF-8 に直すので、化けない。
5. **GUI アプリでループが止まらない。** `run_app` はアプリを数秒観察し、窓が出たか、応答しているかを報告して、最後に必ず終わらせる。
6. **使い方はスキルで教える。** `windows-desktop-dev` スキルが Claude Code に、人に頼まずにこれらのツールを使うこと、エラーの読み方、本当に人に頼むべき場面を教える。

---

## 仕組み

```
WSL2 (Ubuntu)                                        Windows 11 (あなたのユーザー)
┌──────────────────────────────────────┐            ┌──────────────────────────────────────────┐
│ Claude Code (サンドボックスの外)     │─ stdio ───▶│ C:\Tools\WinDevBridge\WinDevBridge.exe   │
│   └ MCP クライアント "windev"        │  (Interop) │   bridge.json(Claude Code は書けない)    │
│                                      │            │   ├ build   → dotnet build / MSBuild     │
│ Bash サンドボックス (bubblewrap)     │            │   ├ test    → dotnet test / vstest       │
│   × Windows のプログラム(拒否)       │            │   └ run_app → 登録したアプリだけ。       │
│   × Windows ホストへのネットワーク   │            │               最後に必ず終わらせる       │
└──────────────────────────────────────┘            └──────────────────────────────────────────┘
              ソースは共有: WSL の /mnt/c/... = Windows の C:\...
```

| ツール | 渡すもの | Windows で動かすもの |
|--------|----------|----------------------|
| `build` | プロジェクトかソリューション(`.csproj` / `.vcxproj` / `.sln` / `.slnx`)、`Debug` / `Release`、省略できるプラットフォーム(`x64` / `Win32` / `x86` / `ARM64`) | C#: `dotnet build`<br>C++: `MSBuild.exe`(`vswhere` で探す) |
| `test` | C#: テストのプロジェクトかソリューション<br>C++: ビルドしたテストの `.dll`。省略できるフィルター | C#: `dotnet test`<br>C++: `vstest.console.exe` |
| `run_app` | `bridge.json` に登録したアプリの名前、観察する秒数 | アプリを起動し、観察して、閉じる(閉じなければ 5 秒後に強制終了) |

この方式を選ぶ前に、5 つの方式を比べた(Bash への WSL Interop の解放、この MCP サーバー、常駐の HTTP サーバー、Windows Sandbox / Hyper-V の VM、Linux でのクロスコンパイル)。[設計の文書](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md)の節 1 にある。

---

## このリポジトリにあるもの

| 場所 | 中身 |
|------|------|
| `src/WinDevBridge` | ブリッジ。MCP stdio サーバー(C# / .NET 10) |
| `src/WinDevBridge.Tests` | ブリッジの xUnit のテスト(Linux でも Windows でも動く) |
| `skills/windows-desktop-dev` | ブリッジの使い方を Claude Code に教えるスキル |
| `samples/TemperatureConverter` | サンプル: C# / WPF のアプリと xUnit のテスト |
| `samples/TemperatureConverterCpp` | サンプル: C++ / Win32 のアプリと Microsoft C++ Unit Test Framework のテスト。新しい C++ のプロジェクトのひな形にもなる |
| `Plans/` | 設計の文書: 方式の比較、構築の手順、セキュリティの境界、`CLAUDE.md` のテンプレート |
| `.github/prompts/` | このプロジェクトの目的を定めたプロンプト |

---

## 手順書: Claude Code で新しい Windows デスクトップアプリを開発する

手順書は 4 つに分かれる。**A** は PC ごとに一度だけ行う。**B** は新しいアプリごとに一度行う。**C** は毎日の作業である。**D** は、Claude Code から助けを頼まれたときにすることである。

コマンドの印の意味:

- **[PowerShell]** Windows のふつうの(管理者でない)PowerShell。
- **[WSL]** WSL(Ubuntu)のターミナル。Claude Code の**外**で。
- **[Claude Code]** Claude Code へのプロンプトとして入力する。

### A. PC ごとに一度だけ行う準備

#### A1. Windows に必要なものを入れる

| 開発するもの | Windows に入れるもの |
|--------------|----------------------|
| C#(WPF、WinForms、WinUI 3) | .NET SDK 10(Windows デスクトップのランタイムを含む)。PowerShell で `dotnet --version` が動くこと |
| C++(Win32) | Visual Studio 2022 以降、または Build Tools for Visual Studio の「**C++ によるデスクトップ開発**」ワークロード |
| C++(MFC) | 上のワークロードに加えて、「**最新の v14x ビルド ツール用 C++ MFC**」コンポーネント |
| WinUI 3 | Visual Studio の「**WinUI アプリケーション開発**」ワークロード(テンプレートと Windows App SDK) |

ほかに、WSL2(Ubuntu)、WSL の .NET SDK 10(ブリッジのビルドに使う)、WSL の Claude Code が要る。

#### A2. ブリッジをビルドして Windows に置く

ブリッジは `C:\Tools\WinDevBridge` に置く。この場所は Claude Code が書ける範囲の外なので、Claude Code はブリッジもその設定も変えられない。

**[PowerShell]**

```powershell
git clone https://github.com/Fujiwo/Shos.WindowsDesktopApplicationDevelopmentTool C:\src\Shos.WindowsDesktopApplicationDevelopmentTool
cd C:\src\Shos.WindowsDesktopApplicationDevelopmentTool
dotnet publish src\WinDevBridge -c Release -r win-x64 --self-contained false -o C:\Tools\WinDevBridge
Copy-Item src\WinDevBridge\bridge.sample.json C:\Tools\WinDevBridge\bridge.json
```

#### A3. 作業フォルダーを決めて `bridge.json` を書く

ブリッジがビルド、テスト、起動を行うのは、**1 つのフォルダー**(作業フォルダー)の中だけである。Windows アプリのリポジトリをすべてまとめる親フォルダーを 1 つ決めるとよい(例: `C:\src`)。

- Windows のファイルシステム(`C:\...`)に置く。ビルドは Windows で動くからである。WSL からは `/mnt/c/src` に見える。
- Dropbox や OneDrive のフォルダーは避ける。同期のソフトが、ビルドの最中に `bin` や `obj` のファイルを握る。どうしても使うなら、`bin` と `obj` を同期から外す([ヒント](#ヒント))。

`C:\Tools\WinDevBridge\bridge.json` を書き換える。

```json
{
  "workspaceRoot": "C:\\src",
  "buildTimeoutSeconds": 600,
  "testTimeoutSeconds": 900,
  "maxObservationSeconds": 60,
  "apps": {}
}
```

| 項目 | 意味 |
|------|------|
| `workspaceRoot` | ブリッジが扱う唯一のフォルダー。Claude Code が渡すパスは、ここからの相対パスになる |
| `buildTimeoutSeconds` / `testTimeoutSeconds` | これより長くかかるビルドやテストは止める |
| `maxObservationSeconds` | `run_app` がアプリを観察できる最長の秒数 |
| `apps` | `run_app` で起動してよいアプリ。名前と、`workspaceRoot` からの `.exe` の相対パス。B でアプリごとに足す |
| `msbuildPath` / `vstestPath` | 省略可(C++ 用)。`MSBuild.exe` / `vstest.console.exe` の絶対パス。省略すると、C++ のワークロードの入った最新の Visual Studio を使う。版を固定したいとき(例: `v142` のプロジェクトを Visual Studio 2019 で)に書く |

#### A4. ブリッジを Claude Code に登録する

**[WSL]**

```bash
claude mcp add --scope user --transport stdio windev -- /mnt/c/Tools/WinDevBridge/WinDevBridge.exe
```

`--scope user` にすると、どのリポジトリでも `windev` が使える。

#### A5. 権限と時間切れを設定する

WSL の `~/.claude/settings.json` に次を入れる(すでにある設定とまとめる)。

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

- `allow`: 3 つのツールを毎回の確認なしで使わせる。確認待ちでループが止まらないようにするためである。
- `ask`: ビルドの定義の編集の前に、あなたの承認を求めさせる。ビルドの定義は、ビルドのときに任意のコマンドを走らせられる場所だからである。
- `deny`: ブリッジ、その設定、MCP の登録、この権限の設定を変えさせない。
- `MCP_TOOL_TIMEOUT`(ミリ秒)は、テストの時間切れ(900 秒)より長くする。

#### A6. スキルを入れる

**[WSL]** で、clone したリポジトリ(`/mnt/c/src/Shos.WindowsDesktopApplicationDevelopmentTool`)の中で:

```bash
mkdir -p ~/.claude/skills/windows-desktop-dev
cp -r skills/windows-desktop-dev/SKILL.md skills/windows-desktop-dev/references ~/.claude/skills/windows-desktop-dev/
```

#### A7. 動くことを確かめる

WSL で Claude Code を起動し、`/mcp` で `windev` が **connected** になっていることを確かめる。続けて試す。

**[Claude Code]**(`/mnt/c/src/Shos.WindowsDesktopApplicationDevelopmentTool` で起動して)

```
samples/TemperatureConverter を windev でビルドしてテストして。
```

`workspaceRoot` が `C:\src` なので、パスは `C:\src` から始まる。Claude Code は `Shos.WindowsDesktopApplicationDevelopmentTool/samples/...` を渡すことになる。正しい起点の見つけ方は、スキルが教える。

結果が `結果: 成功` で始まれば、A は終わりである。

### B. 新しいアプリを始める

アプリごとに行う。例では、`C:\src\MyApp`(WSL では `/mnt/c/src/MyApp`)に `MyApp` というアプリを作る。

#### B1. プロジェクトを作る

プロジェクトは、Claude Code に作らせても(作りたいアプリを説明するだけでよい)、自分で作ってもよい。技術ごとの手順に従う。

**C# / WPF** — **[WSL]**(Linux の .NET 10 SDK で確かめたコマンド)

```bash
mkdir -p /mnt/c/src/MyApp && cd /mnt/c/src/MyApp
git init
dotnet new gitignore
dotnet new sln -n MyApp                     # MyApp.slnx ができる
dotnet new wpf   -n MyApp       -o src/MyApp         --no-restore
dotnet new xunit -n MyApp.Tests -o tests/MyApp.Tests --no-restore
# テストのプロジェクトも Windows 向けにしないと、WPF のアプリを参照できない:
sed -i 's#<TargetFramework>net10.0</TargetFramework>#<TargetFramework>net10.0-windows</TargetFramework>#' tests/MyApp.Tests/MyApp.Tests.csproj
dotnet sln MyApp.slnx add src/MyApp/MyApp.csproj tests/MyApp.Tests/MyApp.Tests.csproj
dotnet add tests/MyApp.Tests reference src/MyApp
```

`--no-restore` を付けるのは、パッケージの復元を後で Windows のブリッジに任せるからである。

**C# / WinForms** — WPF と同じ。`dotnet new wpf` の代わりに `dotnet new winforms` を使う。

**C# / WinUI 3**(まだ確かめていない) — Windows の Visual Studio で「**空のアプリ (デスクトップの WinUI 3)**」のテンプレートからアプリを作り、上と同じように xUnit のプロジェクトを足す。`run_app` でふつうの `.exe` として起動できるように、アプリの `.csproj` に `<WindowsPackageType>None</WindowsPackageType>` を足して、パッケージ化しない形にする。WinUI 3 のプロジェクトはプラットフォームが要るので、`platform: "x64"` でビルドする。

**C++ / Win32** — サンプルをひな形にする。**[Claude Code]**

```
/mnt/c/src/Shos.WindowsDesktopApplicationDevelopmentTool/samples/TemperatureConverterCpp と同じ構成で、
/mnt/c/src/MyApp に新しい C++ の Win32 アプリを作って。アプリのプロジェクト、ネイティブの単体テストの
プロジェクト、.slnx を作り、プロジェクトの GUID は新しくして。
```

または、Visual Studio で「**Windows デスクトップ アプリケーション**」と「**ネイティブ単体テスト プロジェクト**」を作り、下の設定を入れる。

**C++ / MFC** — Visual Studio の「**MFC アプリ**」のテンプレートでアプリを作り、「**ネイティブ単体テスト プロジェクト**」を足して、下の設定を入れる。テストしたいロジックは、MFC の窓に依存しないふつうの C++ のクラスに置く。

**すべての `.vcxproj` に入れておく設定**(C++ のサンプルには入っている):

| 設定 | 理由 |
|------|------|
| 先頭に `<Platform Condition="'$(Platform)' == ''">x64</Platform>` | プラットフォームを省略されたときに、ない構成(`Win32`)にならない |
| `<OutDir>$(MSBuildProjectDirectory)\bin\$(Platform)\$(Configuration)\</OutDir>` と `<IntDir>$(MSBuildProjectDirectory)\obj\$(Platform)\$(Configuration)\</IntDir>` | プロジェクトからビルドしても、ソリューションからビルドしても、出力の場所が同じになる。テストの DLL とアプリのパスが 1 つに決まる |
| `AdditionalOptions` に `/utf-8` | Claude Code は BOM のない UTF-8 で書く。`/utf-8` が無いと、日本語の Windows はソースを Shift-JIS として読む |
| `<ProjectSubType>NativeUnitTestProject</ProjectSubType>`(テストのプロジェクト) | Microsoft C++ Unit Test Framework の場所が自動で足される |

#### B2. アプリを `bridge.json` に登録する

アプリを一度ビルドしてから(Claude Code に頼んでもよい)、その `.exe` を `C:\Tools\WinDevBridge\bridge.json` の `apps` に足す。パスは `workspaceRoot` からの相対パスである。

```json
"apps": {
  "MyApp": "MyApp/src/MyApp/bin/Debug/net10.0-windows/MyApp.exe"
}
```

C++ で上の設定を入れたなら: `"MyApp": "MyApp/src/MyApp/bin/x64/Debug/MyApp.exe"`。

`bridge.json` を変えたら、**Claude Code を起動し直す**。ブリッジは起動のときにだけ読む。

#### B3. アプリ用の短い `CLAUDE.md` を書く

`/mnt/c/src/MyApp/CLAUDE.md` に次を書き、パスを合わせる。どこに何があるかを Claude Code に伝えるので、推測させずに済む。

````markdown
## Windows でのビルド・テスト・起動

これは Windows デスクトップアプリ(C# / .NET 10 / WPF)である。ビルド、テスト、起動には、
`windows-desktop-dev` スキルと MCP サーバー `windev` のツールを使う。Windows でのビルドを人に頼まない。

windev の作業フォルダーは `C:\src` なので、パスは `MyApp/` から始まる。

| もの | 値 |
|------|----|
| ソリューション | `MyApp/MyApp.slnx` |
| アプリのプロジェクト | `MyApp/src/MyApp/MyApp.csproj` |
| テストのプロジェクト | `MyApp/tests/MyApp.Tests/MyApp.Tests.csproj` |
| run_app のアプリの名前 | `MyApp` |
````

C++ では、テストのプロジェクトの代わりにテストの DLL(例: `MyApp/tests/MyApp.Tests/bin/x64/Debug/MyApp.Tests.dll`)を書き、`platform: x64` と書き添える。より詳しいテンプレートは、[設計の文書](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md)の節 4 にある。

#### B4. 最初に動かす

**[WSL]**

```bash
cd /mnt/c/src/MyApp
claude
```

**[Claude Code]**

```
ソリューションをビルドし、テストを実行し、run_app でアプリが開いて閉じることを確かめて。
```

3 つとも動けば、毎日の開発を始められる。最初の状態を git でコミットしておく。

### C. Claude Code との毎日の開発

したいことを説明するだけでよい。「ビルドして」「テストして」と言わなくても、スキルが Claude Code に自分の作業を確かめさせる。

**プロンプトの例**

```
摂氏を華氏に換算する「換算」ボタンを足して。換算の単体テストを先に書いてね。
```

```
「読み込み」を押すとアプリが固まる。原因を調べて直して。
```

```
ビルドエラーを直して。
```

**Claude Code がすること**

1. 単体テストを書くか変え、`test` でまず**失敗する**ことを見る(テストが本当に何かを確かめていることが分かる)。
2. コードを変える。
3. すべてのテストが成功するまで `test` を繰り返す。ビルドエラー(`error CS....`、`error C....`、`error LNK....`)が出たら、最初のエラーを読んで直す。
4. 画面に関わる変更なら、`build` してから `run_app` で、窓が出て、応答し、閉じることを確かめる。
5. 何を変え、何を確かめたかを報告する。

**うまくいくためのコツ**

- ロジックを画面(コードビハインド、ダイアログ、メッセージハンドラー)から分け、単体テストで確かめられるようにする。`run_app` で分かるのは、窓が開いて応答するかまでで、何が表示されているかは分からない。
- 小さな単位で頼む。単位ごとにビルドとテストで確かめられる。
- コミットの前に git の差分を見る。特に `.csproj` / `.vcxproj` の変更は見る。

#### ヒント

- **Dropbox / OneDrive:** ソースが同期するフォルダーにあるなら、`bin` と `obj` を外す。Dropbox なら PowerShell で:

  ```powershell
  Get-ChildItem -Recurse -Directory -Include bin,obj C:\path\to\repo |
    ForEach-Object { Set-Content -Path $_.FullName -Stream com.dropbox.ignored -Value 1 }
  ```

- **長い出力:** 出力は末尾の 12,000 文字だけが返る。テストを絞るか、プロジェクトを 1 つずつビルドするよう頼む。
- **文字コード:** Shift-JIS の出力は自動で UTF-8 に直る。ソースは UTF-8 にする。

### D. Claude Code から頼まれたとき

Claude Code が止まって頼んでくるのは、次の場面だけのはずである。頼むときは、何をしようとしたか、何が返ったか、あなたに何をしてほしいかを書いてくる。

| Claude Code の言うこと | あなたがすること |
|------------------------|------------------|
| `windev` のツールが使えない | `/mcp` を見る。connected でなければ、A2〜A4 を見直し、Claude Code を起動し直す |
| アプリが登録されていない(`登録されていないアプリです`) | `bridge.json` の `apps` に足し(B2)、Claude Code を起動し直す |
| パスが作業フォルダーの外(`作業フォルダーの外`) | リポジトリを `workspaceRoot` の下に移すか、`workspaceRoot` を変えて起動し直す |
| Visual Studio が見つからない、`MSB8020`(ツールセットが無い)、MFC や Windows SDK が無い | Visual Studio Installer でコンポーネントを足すか、`bridge.json` に `msbuildPath` を書く |
| `.csproj` / `.vcxproj` / `.props` を編集したい(権限の確認) | 理由を読んで、許すか断る |
| ビルドやテストが時間切れになり、もっと時間が要る | `buildTimeoutSeconds` / `testTimeoutSeconds` を延ばし、起動し直す |

ほかのエラーと直し方: [skills/windows-desktop-dev/references/troubleshooting.md](skills/windows-desktop-dev/references/troubleshooting.md)。

---

## セキュリティ: Claude Code にできること、できないこと

| 層 | 防ぐこと |
|----|----------|
| Bash サンドボックス(有効のまま) | Bash から Windows のプログラムを動かすこと、Windows ホストへのネットワーク |
| Claude Code の権限(A5) | ブリッジ、その設定、MCP の登録、権限の設定を変えること。ビルドの定義の編集には、あなたの承認が要る |
| ブリッジ | 3 つのツールの外のすべて。パスは `workspaceRoot` の中の実在するプロジェクト(またはテストの DLL)に限る。構成とプラットフォームは決まった一覧から。起動できるのは登録したアプリだけ。シェルを通さない。すべてに時間の上限がある |

**残るリスク(隠さずに書く):** ビルドとテストを許すことは、あなたの Windows のユーザーの権限でコードを動かすことを許すのと同じである。Claude Code はテスト、アプリのコード、(あなたの承認があれば)ビルドの定義を編集でき、それらはビルドやテストのときに動く。引数の検証ではこれを防げない。信頼しきれないコードを扱うなら、ブリッジを低権限の Windows ユーザーか VM で動かし、`ssh` でつなぐ。変わるのは `claude mcp add` のコマンドだけである。[設計の文書](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md)の節 3.3 を見る。

---

## 現状

| 項目 | 状況 |
|------|------|
| 設計の文書(方式、構築、セキュリティ、`CLAUDE.md` のテンプレート) | 済み |
| ブリッジの単体テスト(C++ の対応を含めて 79 件) | Linux ですべて成功 |
| Linux での MCP の接続とツールの呼び出し(C++ の要求を含む) | 確認済み |
| `dotnet publish -r win-x64` | 済み(警告 0) |
| スキル `windows-desktop-dev` | 下書き。レビュー済み |
| B1 の C# のコマンド(`dotnet new`、`dotnet sln add`、`dotnet add reference`) | Linux で確認済み |
| Windows で Claude Code から `build` / `test` / `run_app` を呼ぶ | **未確認** |
| サンプルアプリ(C# と C++)で自律のループを一巡回す | **未確認** |
| WinUI 3 | **未確認** |

---

## ブリッジそのものを開発する

**[WSL]**

```bash
dotnet build WinDevBridge.slnx
dotnet test src/WinDevBridge.Tests
dotnet publish src/WinDevBridge -c Release -r win-x64 --self-contained false -o artifacts/WinDevBridge
```

Claude Code のサンドボックスの中では、ホームに書けず、NuGet の証明書の失効確認も拒まれる。先に `DOTNET_CLI_HOME` と `NUGET_PACKAGES` を `$TMPDIR` の下に向け、`NUGET_CERT_REVOCATION_MODE=offline` にする。

新しい種類のプロジェクト(例: Google Test)に対応させるときや、ツールを変えるときは、[設計の文書](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md)の節 1〜3 を読む。
