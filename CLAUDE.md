# CLAUDE.md

このファイルは、このリポジトリで作業する Claude Code (claude.ai/code) への指針を示す。

## プロジェクトの状況

プロジェクトの目的と要件は `.github/prompts/ClaudeCodeDevelopmentBestPractices.prompt.md` に定めてある。設計や実装を始める前に読む。成果物 1〜4 は `Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md` にある。

| 場所 | 中身 |
|------|------|
| `src/WinDevBridge` | ブリッジ。Windows で動く MCP stdio サーバーで、`build` / `test` / `run_app` を公開する。C# は dotnet、C++ は Visual Studio の MSBuild.exe と vstest.console.exe を使う |
| `src/WinDevBridge.Tests` | ブリッジの xUnit のテスト(Linux でも Windows でも動く) |
| `samples/TemperatureConverter` | 一巡の検証に使う WPF のアプリと xUnit のテスト(Windows でしかビルドできない) |
| `samples/TemperatureConverterCpp` | C++ / Win32 のアプリと Microsoft C++ Unit Test Framework のテスト(Windows の Visual Studio でしかビルドできない) |

### コマンド(ブリッジの開発。Linux 側の dotnet)

サンドボックスではホームに書けず、NuGet の証明書の失効確認も拒まれるので、先に環境変数を読み込む。リストアには `api.nuget.org` への通信が要る。

```bash
. $TMPDIR/dotnet-env.sh   # DOTNET_CLI_HOME と NUGET_PACKAGES を $TMPDIR に、NUGET_CERT_REVOCATION_MODE=offline
dotnet build WinDevBridge.slnx
dotnet test src/WinDevBridge.Tests
dotnet publish src/WinDevBridge -c Release -r win-x64 --self-contained false -o artifacts/WinDevBridge
```

`$TMPDIR/dotnet-env.sh` が無いときは、上の 3 つの変数と `DOTNET_NOLOGO=1` を export する。

### Windows 側での操作(サンプルなど)

MCP サーバー `windev` のツールを使う(Plans の節 4 のテンプレートに従う)。登録と配置はユーザーが行う(Plans の節 2.2〜2.3)。

## 目的（プロンプトファイルの要約）

Windows 11 のホストにある WSL2 (Ubuntu) のサンドボックスで動く Claude Code が、**Windows デスクトップアプリ**（C# / .NET / WPF / WinForms / WinUI 3、C++ / MFC / Win32 など）の「編集 → ビルド → テスト → エラーの検出 → 修正」のサイクルを、ビルドや実行を人に頼まずに自律して回せる環境（と、必要ならそれを支えるツール。ツールは C# / .NET で作る）を作る。

解くべき問題:
1. Linux だけでは Windows ネイティブのアプリ（GUI や Windows 固有の API に依存する部分）をビルドも実行もできない。
2. Claude Code が Windows でのビルドや実行を人に頼んで止まり、自律のループが途切れやすい。
3. ホストを守ったまま、サンドボックスから Windows のツールチェーンを呼べるようにする必要がある。

求められる成果物（この順で出す）:
1. 実現方式（WSL Interop の限定的な解放、ホスト側の MCP サーバー / 軽量 API のブリッジ、Windows のコンテナ / VM、クロスコンパイルなど）を、自律性・セキュリティ・導入と運用のコストで比べ、一つを選ぶ。
2. 構築の手順: WSL 側（`/etc/wsl.conf`、環境変数、ラッパースクリプト）、Windows 側（`dotnet.exe`、`MSBuild.exe`、テストランナー、またはブリッジサーバー）、ファイルの置き場所（`/mnt/c/` と WSL ネイティブのファイルシステムの性能、ファイルロックの問題）。
3. セキュリティの境界: Claude Code にできることを「ビルド」「単体テストの実行」「特定のアプリの起動と終了」だけに絞る（Claude Code の権限の設定、ラッパーでの引数の検証、権限の分離）。
4. 対象プロジェクト用の `CLAUDE.md` のテンプレート: ブリッジ / ラッパーのコマンドを明示し、標準出力・標準エラー出力の文字コード（UTF-8 / Shift-JIS）を扱い、GUI のハングを防ぐ（ヘッドレスのテスト、またはタイムアウト付きの起動）。

出力先: 成果物 1〜4 は、`Plans` フォルダーの新しい Markdown ファイル 1 つに 4 つの節として書く。ファイル名には日時を含める。

## 環境についての注意

- 作業コピーは Windows のファイルシステムの `/mnt/c/...`（Dropbox で同期）にあり、今は git リポジトリではない。
- Bash のコマンドは bubblewrap のサンドボックスで動く。リポジトリ直下のいくつかのドットファイルやディレクトリ（`.vscode`、`.idea`、`.gitmodules`、`.bashrc` など）は、サンドボックスの中では中身の見えないキャラクターデバイスとして見える。`.mcp.json` は 0 バイトの読み取り専用のファイルとして見える。どれもサンドボックスによるもので、プロジェクトのファイルではない。
- サンドボックスの中からは WSL Interop が使えない。`cmd.exe` や `powershell.exe` を呼ぶと `UtilConnectUnix:534: socket failed` で失敗する（2026-10-14 に確認）。Windows のツールチェーンをどう呼ぶかが、このプロジェクトの中心の課題である。
- サンドボックスは、`.claude/settings.json`、`.claude/settings.local.json`、`.claude/hooks`、`.claude/skills` への書き込みを拒む。成果物 3 の権限の設定やフックは、Claude が案を示し、ユーザーが反映する。
- Bash のネットワークは、フィルター付きのプロキシーを通る。WSL2 から Windows のホスト（NAT のホストの IP、または mirrored モードの localhost）へつながるかは、まだ確かめていない。ブリッジの方式を比べる前に確かめる。
- Linux 側には .NET SDK 10.0.112 がある（`/usr/bin/dotnet`）。Windows 側には `C:\Program Files\dotnet\dotnet.exe` と Visual Studio 2019・2022・18 がある。

## コーディング規範（sustainable-code スキル）
設計・実装・テスト・レビューの判断基準には、`.claude/skills/` にあるプロジェクトのスキルを使う。会話は日本語なので、**`sustainable-code-jp` を使う**（`sustainable-code` は英語版で、このプロジェクトでは使わない。ユーザーの決定、2026-09-27）。どの reference を読むかは、スキルの SKILL.md にある表に従う。

- スキルのファイルは変えない（`.claude/skills` への書き込みは禁じられている）。改善したくなったら、案を下の「スキルの評価の記録」に書くだけにする。
- **レビューの観点**: 七箇条、引き算（Think Simple）、整合（プロンプトファイルの要件や設計と食い違わないか、書かれた方式で要求を実現できるか）、正しさ（例外の境界、後始末、実行環境の差）。七箇条の外の指摘は、箇条に当てはめずに「整合」「正しさ」と書く。
- **実行環境での確認**: 区切りごとに、テストの Green に加えて、本物の実行環境で動かして確かめる。WSL と Windows で振る舞いが変わるもの（パスの変換、文字コード、プロセスの起動と終了、ファイルロック）は、その区切りのうちに Windows 側で確かめる。
- **計画と引き算**: 「目的」に書かれた成果物は、スキルの判断ルール 1 の「具体的な根拠」に当たる。引き算で見送るときは、その成果物に照らした理由を書く。
- 優先順位はスキルの定めどおり、ユーザーの指示、この CLAUDE.md、スキルの順とする。
- テストフレームワークの導入はスキルではユーザーの確認が要る事項だが、このプロジェクトでは xUnit を使うことでユーザーと合意済みなので、確認なしで導入してよい（ユーザーの決定、2026-10-14）。

### スキルの評価の記録
このプロジェクトの一巡を終えた後に、日本語版のスキルの改善案を作る（ユーザーの指示、2026-10-14）。そのために、一巡の間、スキルの効果と使われ方を記録し続ける。一巡は、プロンプトファイルの成果物 1〜4 を作り、そのツールで Windows デスクトップアプリの「編集 → ビルド → テスト → 修正」を自律して一度回せたところまでとする。

- 記録は docs/for-skills/reviews/skill-log.md に追記する。書くきっかけ、書式、判定の区分は、そのファイルの冒頭のとおりである。後から判定しようとすると根拠が残らないので、出来事が起きたその場で書き、判定もその場で決める。
- 比べる基準は、docs/for-skills/reviews/skill-evaluation-basement.md。
- 一巡の間は、スキルのファイルを変えない。改善したくなったら、案を記録に書くだけにする。この「コーディング規範」の節を変えたときは、日付と内容を記録の「条件の変更」に書く。
- 一巡の後に、記録から数を取り、基準と比べた評価を docs/for-skills/reviews/skill-evaluation.md に書いて、日本語版の改善案を作る。英語版は、日本語版を改良した後に合わせる。
