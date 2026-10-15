# Shos.WindowsDesktopApplicationDevelopmentTool

[日本語](README.jp.md)

Let **Claude Code running in a WSL2 sandbox** build, test and launch **Windows desktop applications** (C# / .NET / WPF, and later WinForms, WinUI 3, C++ / MFC / Win32) on its own, without asking a human to "please build this on Windows", while keeping the Windows host protected.

The repository contains:

- **WinDevBridge**: a small MCP stdio server written in C# / .NET 10. It runs on Windows and exposes exactly three tools: `build`, `test` and `run_app`.
- **A design document** that compares the possible approaches, gives the setup steps, defines the security boundary, and provides a `CLAUDE.md` template for target projects.
- **A sample WPF app** (`TemperatureConverter`) with xUnit tests, used to verify the edit → build → test → fix loop.

## The problem

When Claude Code runs inside WSL2 with its Bash sandbox (bubblewrap) enabled:

1. Linux alone cannot build or run Windows-native apps. Even with `EnableWindowsTargeting=true`, a WPF project fails to build on Linux, and tests and GUI apps cannot run.
2. WSL Interop is blocked inside the sandbox. Calling `cmd.exe` or `powershell.exe` fails with `UtilConnectUnix:534: socket failed`, because seccomp denies creating AF_UNIX sockets.
3. So Claude Code stops and hands the build over to a human, and the autonomous loop breaks.

Opening Interop to Bash would fix (1)–(3), but it would also let every Bash command run arbitrary Windows programs.

## Key points

### 1. A Windows-side MCP server instead of opening the sandbox

MCP stdio servers are launched by Claude Code itself, **outside** the Bash sandbox. WinDevBridge uses this: Claude Code starts `WinDevBridge.exe` through WSL Interop, and talks to it over stdin/stdout. The Bash sandbox stays fully closed.

```
WSL2 (Ubuntu)                                   Windows 11 (signed-in user)
┌─────────────────────────────────┐            ┌─────────────────────────────────────────┐
│ Claude Code (outside sandbox)   │─ stdio ───▶│ C:\Tools\WinDevBridge\WinDevBridge.exe  │
│   └ MCP client "windev"         │  (Interop) │   bridge.json (Claude Code can't write) │
│                                 │            │   ├ build   → dotnet build              │
│ Bash sandbox (bubblewrap)       │            │   ├ test    → dotnet test               │
│   × Interop (blocked)           │            │   └ run_app → registered app only       │
│   × network to Windows host     │            └─────────────────────────────────────────┘
└─────────────────────────────────┘                    ▲ source shared via /mnt/c = C:\
```

Five approaches were compared on autonomy, security and cost (opening Interop to Bash, a Windows MCP server, a resident HTTP bridge, Windows Sandbox / Hyper-V VM, and Linux cross-compiling). The MCP server was chosen. See section 1 of the [design document](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md).

### 2. Only three typed operations

| Tool | Arguments | What it does |
|------|-----------|--------------|
| `build` | `project` (relative path to `.csproj` / `.sln` / `.slnx`), `configuration` (`Debug` / `Release`) | `dotnet build ... --disable-build-servers` |
| `test` | `project`, `filter` (optional, `dotnet test --filter` expression) | `dotnet test ... --disable-build-servers` |
| `run_app` | `app` (a name registered in `bridge.json`), `seconds` | Launches the app, watches it, then **always** closes it (close request, then kills the process tree after 5 seconds) |

The bridge validates every request:

- Paths must point to an existing project inside the workspace. Absolute paths and `..` escapes are rejected.
- Only registered apps can be launched, and the observation time has an upper limit.
- Arguments are passed as a list, never through a shell. Child processes get no stdin.
- Timeouts stop the whole process tree. Requests are processed one at a time, so builds and tests don't fight over `obj`.
- `bridge.json` is read only from next to the executable (`C:\Tools\WinDevBridge`), outside the area the sandbox can write. Arguments and environment variables cannot change it.

### 3. Output that Claude Code can read

- The first line is always one of: success (exit code 0), failure (exit code n), or timeout.
- Output from child processes is detected as UTF-8 or Shift-JIS (code page 932) and returned as UTF-8, so Japanese compiler messages are not garbled.
- stdout and stderr are each trimmed to their last 12,000 characters, where errors usually are.
- `run_app` reports whether a main window appeared, whether it was responding (hang detection), and whether the app exited by itself (crash on startup).

Note: the bridge's messages are currently written in Japanese.

### 4. An honest security model

Allowing build and test is the same as allowing **arbitrary code to run as the signed-in Windows user**: Claude Code can edit MSBuild `Exec` tasks, custom targets, NuGet build props, and the tests themselves. Validating arguments cannot prevent this. The design document states this risk and offers three levels of isolation:

1. (current) Require human approval for edits to build definitions (`*.csproj`, `*.props`, `*.targets`, `nuget.config`, `global.json`) via Claude Code permissions.
2. Run the bridge as a low-privilege local user over `ssh`.
3. Run the bridge in a Hyper-V VM over `ssh`.

Because the bridge speaks only stdio, moving to level 2 or 3 only changes the MCP registration command.

### 5. A `CLAUDE.md` template for target projects

Section 4 of the design document is a ready-to-copy `CLAUDE.md` section. It tells Claude Code which tools to use, not to call `dotnet` or `*.exe` from Bash, how to read results, how to judge a GUI launch, and exactly when it may ask a human for help.

## Repository layout

| Path | Contents |
|------|----------|
| `src/WinDevBridge` | The bridge (MCP stdio server, .NET 10, [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) SDK) |
| `src/WinDevBridge.Tests` | xUnit tests for the bridge (run on both Linux and Windows) |
| `samples/TemperatureConverter` | Sample WPF app and xUnit tests (Windows only) |
| `Plans/` | The design document: approach comparison, setup steps, security boundary, `CLAUDE.md` template |
| `.github/prompts/` | The prompt that defines the goals and requirements of this project |
| `.claude/skills/` | Coding-guideline skills used while developing this repository |
| `docs/for-skills/` | Records used to evaluate those skills |

## Getting started

Full details are in section 2 of the [design document](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md).

### Requirements

- Windows 11 with WSL2 (Ubuntu)
- .NET SDK 10 on Windows (`C:\Program Files\dotnet\dotnet.exe`), plus the Windows Desktop runtime for WPF
- .NET SDK 10 on Linux (to build the bridge)
- Claude Code running in WSL2

### 1. Build and publish the bridge (WSL)

```bash
dotnet test src/WinDevBridge.Tests
dotnet publish src/WinDevBridge -c Release -r win-x64 --self-contained false -o artifacts/WinDevBridge
```

Inside the Claude Code sandbox, the home directory is not writable and NuGet certificate revocation checks are blocked. Set `DOTNET_CLI_HOME` and `NUGET_PACKAGES` to a directory under `$TMPDIR`, and set `NUGET_CERT_REVOCATION_MODE=offline` first.

### 2. Install it on Windows (PowerShell, no admin needed)

```powershell
$repo = 'C:\path\to\Shos.WindowsDesktopApplicationDevelopmentTool'
New-Item -ItemType Directory -Force C:\Tools\WinDevBridge | Out-Null
Copy-Item -Recurse -Force "$repo\artifacts\WinDevBridge\*" C:\Tools\WinDevBridge\
Copy-Item -Force "$repo\src\WinDevBridge\bridge.sample.json" C:\Tools\WinDevBridge\bridge.json
```

Then edit `C:\Tools\WinDevBridge\bridge.json`:

```json
{
  "workspaceRoot": "C:\\path\\to\\your\\repository",
  "buildTimeoutSeconds": 600,
  "testTimeoutSeconds": 900,
  "maxObservationSeconds": 60,
  "apps": {
    "TemperatureConverter": "samples/TemperatureConverter/TemperatureConverter/bin/Debug/net10.0-windows/TemperatureConverter.exe"
  }
}
```

### 3. Register it with Claude Code (WSL shell, outside Claude Code)

```bash
claude mcp add --scope local --transport stdio windev -- /mnt/c/Tools/WinDevBridge/WinDevBridge.exe
```

Add the permissions from section 3.2 of the design document to `.claude/settings.json`: allow `mcp__windev__*`, ask before editing build definitions, deny edits to the bridge and its settings, and keep the sandbox on. Also set `MCP_TOOL_TIMEOUT` to `960000` so that the MCP timeout doesn't come before the test timeout. Restart Claude Code and check that `windev` is connected with `/mcp`.

### 4. Use it

Copy the template from section 4 of the design document into the target project's `CLAUDE.md`. Claude Code can then run the loop by itself:

edit → `test` (new test fails) → fix → `test` (all pass) → `run_app` (for UI changes)

## Tips

- **Keep the source on the Windows file system (`/mnt/c/...`).** Builds run on Windows. If the source is in the WSL file system, Windows `dotnet.exe` has to read it over a network file system, which is slow.
- **Exclude `bin`, `obj` and `artifacts` from Dropbox or OneDrive sync.** Sync clients lock files during builds. With Dropbox, set the `com.dropbox.ignored` stream on those folders. The design document has the command.
- **C++ / MFC / Win32**: not implemented yet. The design document explains how to extend the bridge: find `MSBuild.exe` with `vswhere.exe` and add one command-line builder for `.vcxproj`. The rest of the bridge can be reused as is.

## Status

| Item | Status |
|------|--------|
| Design document (approach, setup, security, `CLAUDE.md` template) | Done |
| Bridge unit tests (45 tests) | Passing on Linux |
| MCP handshake and tool calls on Linux | Verified |
| `dotnet publish -r win-x64` | Done, no warnings |
| Calling `build` / `test` / `run_app` from Claude Code on Windows | Not yet verified |
| One full autonomous loop on the sample app | Not yet verified |
