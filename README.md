# Shos.WindowsDesktopApplicationDevelopmentTool

[日本語](README.jp.md)

**Let Claude Code in WSL build, test and run Windows desktop apps by itself.**

Claude Code running inside WSL2 (Ubuntu) normally cannot build a WPF or C++ app. It ends up saying *"Please build this on Windows and tell me the result"*, and the work stops until a human does it.

This repository removes that wall. With it, Claude Code can repeat **edit → build → test → find the error → fix** for Windows desktop apps on its own, while it can do only three things on Windows: **build**, **run unit tests**, and **start and stop registered apps**.

| Supported | Status |
|-----------|--------|
| C# / .NET: WPF, WinForms | Supported (`dotnet build` / `dotnet test`) |
| C++: Win32, MFC | Supported (Visual Studio `MSBuild.exe` / `vstest.console.exe`) |
| C# / .NET: WinUI 3 | Should work through `dotnet`, but not verified yet |

---

## Contents

1. [The key points in one minute](#the-key-points-in-one-minute)
2. [How it works](#how-it-works)
3. [What is in this repository](#what-is-in-this-repository)
4. [Manual: developing a new Windows desktop app with Claude Code](#manual-developing-a-new-windows-desktop-app-with-claude-code)
   - [Part A. One-time setup on your PC](#part-a-one-time-setup-on-your-pc)
   - [Part B. Starting a new app](#part-b-starting-a-new-app)
   - [Part C. Everyday development with Claude Code](#part-c-everyday-development-with-claude-code)
   - [Part D. When Claude Code asks you for something](#part-d-when-claude-code-asks-you-for-something)
5. [Security: what Claude Code can and cannot do](#security-what-claude-code-can-and-cannot-do)
6. [Status](#status)
7. [Developing the bridge itself](#developing-the-bridge-itself)

---

## The key points in one minute

1. **The wall.** Inside the Claude Code sandbox in WSL, Linux `dotnet` cannot build WPF or C++, and calling Windows programs (`cmd.exe`, `dotnet.exe`, ...) is blocked.
2. **The door.** Claude Code starts MCP servers *outside* its sandbox. So we put a small MCP server, **WinDevBridge**, on Windows. Claude Code calls it, and it runs the build on Windows. The sandbox itself stays closed.
3. **Only three operations.** WinDevBridge offers only `build`, `test` and `run_app`. It checks every path and argument, and its settings live where Claude Code cannot write.
4. **Readable results.** Every result starts with one line: success, failure or timeout. Japanese (Shift-JIS) compiler output is converted to UTF-8, so it is not garbled.
5. **GUI apps never hang the loop.** `run_app` watches the app for a few seconds, reports whether a window appeared and whether it responds, and then always closes it.
6. **A skill teaches Claude Code how to use it.** The `windows-desktop-dev` skill tells Claude Code to use these tools instead of asking you, how to read errors, and when it really should ask you.

---

## How it works

```
WSL2 (Ubuntu)                                   Windows 11 (your user)
┌─────────────────────────────────┐            ┌─────────────────────────────────────────┐
│ Claude Code (outside sandbox)   │─ stdio ───▶│ C:\Tools\WinDevBridge\WinDevBridge.exe  │
│   └ MCP client "windev"         │  (Interop) │   bridge.json (Claude Code can't write) │
│                                 │            │   ├ build   → dotnet build / MSBuild    │
│ Bash sandbox (bubblewrap)       │            │   ├ test    → dotnet test / vstest      │
│   × Windows programs (blocked)  │            │   └ run_app → registered apps only,     │
│   × network to Windows host     │            │               always closed at the end  │
└─────────────────────────────────┘            └─────────────────────────────────────────┘
              source code is shared: /mnt/c/... in WSL = C:\... in Windows
```

| Tool | What you pass | What it runs on Windows |
|------|---------------|-------------------------|
| `build` | project or solution (`.csproj` / `.vcxproj` / `.sln` / `.slnx`), `Debug` / `Release`, optional platform (`x64` / `Win32` / `x86` / `ARM64`) | C#: `dotnet build`<br>C++: `MSBuild.exe` (found with `vswhere`) |
| `test` | C#: test project or solution<br>C++: the built test `.dll`; optional filter | C#: `dotnet test`<br>C++: `vstest.console.exe` |
| `run_app` | the name of an app registered in `bridge.json`, seconds to watch | starts the app, watches it, closes it (kills it after 5 more seconds if needed) |

Five approaches were compared before choosing this one (opening WSL Interop to Bash, this MCP server, a resident HTTP server, a Windows Sandbox / Hyper-V VM, and cross-compiling on Linux). See section 1 of the [design document](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md).

---

## What is in this repository

| Path | Contents |
|------|----------|
| `src/WinDevBridge` | The bridge: an MCP stdio server (C# / .NET 10) |
| `src/WinDevBridge.Tests` | xUnit tests for the bridge (run on Linux and Windows) |
| `skills/windows-desktop-dev` | The Claude Code skill that teaches how to use the bridge |
| `samples/TemperatureConverter` | Sample: C# / WPF app + xUnit tests |
| `samples/TemperatureConverterCpp` | Sample: C++ / Win32 app + Microsoft C++ Unit Test Framework tests. Also a good starting point for new C++ projects |
| `Plans/` | Design document: comparison of approaches, setup, security boundary, `CLAUDE.md` template |
| `.github/prompts/` | The prompt that defines the goals of this project |

---

## Manual: developing a new Windows desktop app with Claude Code

The manual has four parts. **Part A** is done once per PC. **Part B** is done once per new app. **Part C** is what you do every day. **Part D** tells you what to do when Claude Code asks you for help.

In the commands below:

- **[PowerShell]** means a normal (non-admin) PowerShell on Windows.
- **[WSL]** means a WSL (Ubuntu) terminal, *outside* Claude Code.
- **[Claude Code]** means you type it as a prompt to Claude Code.

### Part A. One-time setup on your PC

#### A1. Install what Windows needs

| You develop | Install on Windows |
|-------------|--------------------|
| C# (WPF, WinForms, WinUI 3) | .NET SDK 10 (includes the Windows Desktop runtime). `dotnet --version` in PowerShell must work |
| C++ (Win32) | Visual Studio 2022 or later, or Build Tools for Visual Studio, with the **Desktop development with C++** workload |
| C++ (MFC) | The same workload, plus the **C++ MFC for latest build tools** component |
| WinUI 3 | Visual Studio with the **WinUI application development** workload (templates and Windows App SDK) |

You also need WSL2 (Ubuntu), the .NET SDK 10 in WSL (to build the bridge), and Claude Code in WSL.

#### A2. Build the bridge and put it on Windows

The bridge goes in `C:\Tools\WinDevBridge`. This folder is outside the area Claude Code can write, so Claude Code cannot change the bridge or its settings.

**[PowerShell]**

```powershell
git clone https://github.com/Fujiwo/Shos.WindowsDesktopApplicationDevelopmentTool C:\src\Shos.WindowsDesktopApplicationDevelopmentTool
cd C:\src\Shos.WindowsDesktopApplicationDevelopmentTool
dotnet publish src\WinDevBridge -c Release -r win-x64 --self-contained false -o C:\Tools\WinDevBridge
Copy-Item src\WinDevBridge\bridge.sample.json C:\Tools\WinDevBridge\bridge.json
```

#### A3. Decide your workspace folder and write `bridge.json`

The bridge only builds, tests and runs things inside **one folder**, the *workspace*. We recommend one parent folder for all your Windows app repositories, for example `C:\src`.

- Put it on the Windows file system (`C:\...`), because the build runs on Windows. In WSL it appears as `/mnt/c/src`.
- Avoid Dropbox and OneDrive folders. Sync clients lock files in `bin` and `obj` during builds. If you must use them, exclude `bin` and `obj` from sync (see [Tips](#tips)).

Edit `C:\Tools\WinDevBridge\bridge.json`:

```json
{
  "workspaceRoot": "C:\\src",
  "buildTimeoutSeconds": 600,
  "testTimeoutSeconds": 900,
  "maxObservationSeconds": 60,
  "apps": {}
}
```

| Setting | Meaning |
|---------|---------|
| `workspaceRoot` | The only folder the bridge works in. Paths from Claude Code are relative to it |
| `buildTimeoutSeconds` / `testTimeoutSeconds` | A build or test that runs longer is stopped |
| `maxObservationSeconds` | The longest time `run_app` may watch an app |
| `apps` | Apps that `run_app` may start: a name and the path of the `.exe`, relative to `workspaceRoot`. You add one per app in Part B |
| `msbuildPath` / `vstestPath` | Optional, for C++. Absolute paths to `MSBuild.exe` / `vstest.console.exe`. If omitted, the latest Visual Studio with the C++ workload is used. Set them to pin a version (for example, Visual Studio 2019 for a `v142` project) |

#### A4. Register the bridge with Claude Code

**[WSL]**

```bash
claude mcp add --scope user --transport stdio windev -- /mnt/c/Tools/WinDevBridge/WinDevBridge.exe
```

`--scope user` makes `windev` available in every repository.

#### A5. Set permissions and the timeout

Add this to `~/.claude/settings.json` in WSL (merge it with what is already there):

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

- `allow`: Claude Code may use the three tools without asking you each time, so the loop doesn't stop.
- `ask`: Claude Code must ask you before it edits build definitions, because a build definition can run any command during a build.
- `deny`: Claude Code cannot change the bridge, its settings, the MCP registration, or these permissions.
- `MCP_TOOL_TIMEOUT` (milliseconds) must be longer than the test timeout (900 seconds).

#### A6. Install the skill

**[WSL]**, in the cloned repository (`/mnt/c/src/Shos.WindowsDesktopApplicationDevelopmentTool`):

```bash
mkdir -p ~/.claude/skills/windows-desktop-dev
cp -r skills/windows-desktop-dev/SKILL.md skills/windows-desktop-dev/references ~/.claude/skills/windows-desktop-dev/
```

#### A7. Check that it works

Start Claude Code in WSL, type `/mcp`, and check that `windev` is **connected**. Then try:

**[Claude Code]** (started in `/mnt/c/src/Shos.WindowsDesktopApplicationDevelopmentTool`)

```
Build and test samples/TemperatureConverter with windev.
```

Note: because `workspaceRoot` is `C:\src`, paths start from `C:\src`, so Claude Code will pass `Shos.WindowsDesktopApplicationDevelopmentTool/samples/...`. The skill tells it how to find the right starting point.

If the result starts with `結果: 成功` ("Result: success"), Part A is done. (The bridge currently writes its messages in Japanese.)

### Part B. Starting a new app

Do these steps for each new app. The examples create an app called `MyApp` in `C:\src\MyApp` (`/mnt/c/src/MyApp` in WSL).

#### B1. Create the projects

You can let Claude Code create the projects (just describe the app), or create them yourself. Use the route for your technology.

**C# / WPF** — **[WSL]** (these commands were checked with the .NET 10 SDK on Linux)

```bash
mkdir -p /mnt/c/src/MyApp && cd /mnt/c/src/MyApp
git init
dotnet new gitignore
dotnet new sln -n MyApp                     # creates MyApp.slnx
dotnet new wpf   -n MyApp       -o src/MyApp         --no-restore
dotnet new xunit -n MyApp.Tests -o tests/MyApp.Tests --no-restore
# The test project must target Windows too, or it cannot reference the WPF app:
sed -i 's#<TargetFramework>net10.0</TargetFramework>#<TargetFramework>net10.0-windows</TargetFramework>#' tests/MyApp.Tests/MyApp.Tests.csproj
dotnet sln MyApp.slnx add src/MyApp/MyApp.csproj tests/MyApp.Tests/MyApp.Tests.csproj
dotnet add tests/MyApp.Tests reference src/MyApp
```

`--no-restore` is used because packages are restored later on Windows by the bridge.

**C# / WinForms** — the same as WPF, but use `dotnet new winforms` instead of `dotnet new wpf`.

**C# / WinUI 3** (not verified yet) — create the app on Windows with Visual Studio's **Blank App (WinUI 3 in Desktop)** template, and add an xUnit project as above. To let `run_app` start it as a normal `.exe`, make it unpackaged by adding `<WindowsPackageType>None</WindowsPackageType>` to the app's `.csproj`. WinUI 3 projects need a platform, so build with `platform: "x64"`.

**C++ / Win32** — start from the sample. **[Claude Code]**

```
Create a new C++ Win32 app in /mnt/c/src/MyApp, based on the structure of
/mnt/c/src/Shos.WindowsDesktopApplicationDevelopmentTool/samples/TemperatureConverterCpp:
an app project, a Native Unit Test project, and a .slnx. Use new project GUIDs.
```

Or create the projects in Visual Studio (**Windows Desktop Application** + **Native Unit Test Project**) and apply the settings below.

**C++ / MFC** — create the app in Visual Studio with the **MFC App** template, add a **Native Unit Test Project**, and apply the settings below. Keep the logic you want to test in plain C++ classes that do not depend on MFC windows.

**Recommended settings for every `.vcxproj`** (the C++ sample already has them):

| Setting | Why |
|---------|-----|
| `<Platform Condition="'$(Platform)' == ''">x64</Platform>` at the top | If the platform is omitted, the build doesn't pick a configuration that doesn't exist (`Win32`) |
| `<OutDir>$(MSBuildProjectDirectory)\bin\$(Platform)\$(Configuration)\</OutDir>` and `<IntDir>$(MSBuildProjectDirectory)\obj\$(Platform)\$(Configuration)\</IntDir>` | The output path is the same whether you build the project or the solution, so the test DLL and the app have one fixed path |
| `/utf-8` in `AdditionalOptions` | Claude Code writes UTF-8 without a BOM. Without `/utf-8`, Japanese Windows reads source files as Shift-JIS |
| `<ProjectSubType>NativeUnitTestProject</ProjectSubType>` (test project) | Adds the Microsoft C++ Unit Test Framework paths automatically |

#### B2. Register the app in `bridge.json`

Build the app once (or ask Claude Code to build it), then add its `.exe` to `apps` in `C:\Tools\WinDevBridge\bridge.json`. The path is relative to `workspaceRoot`.

```json
"apps": {
  "MyApp": "MyApp/src/MyApp/bin/Debug/net10.0-windows/MyApp.exe"
}
```

For C++ with the recommended settings: `"MyApp": "MyApp/src/MyApp/bin/x64/Debug/MyApp.exe"`.

**Restart Claude Code** after you change `bridge.json`. The bridge reads it only when it starts.

#### B3. Write a short `CLAUDE.md` for the app

Put this in `/mnt/c/src/MyApp/CLAUDE.md` and adjust the paths. It tells Claude Code where things are, so it doesn't have to guess.

````markdown
## Windows build, test and run

This is a Windows desktop app (C# / .NET 10 / WPF). Use the `windows-desktop-dev` skill and the
`windev` MCP tools for every build, test and run. Do not ask me to build on Windows.

The windev workspace is `C:\src`, so paths start with `MyApp/`.

| What | Value |
|------|-------|
| Solution | `MyApp/MyApp.slnx` |
| App project | `MyApp/src/MyApp/MyApp.csproj` |
| Test project | `MyApp/tests/MyApp.Tests/MyApp.Tests.csproj` |
| App name for run_app | `MyApp` |
````

For C++, list the test DLL instead of the test project (for example `MyApp/tests/MyApp.Tests/bin/x64/Debug/MyApp.Tests.dll`) and say `platform: x64`. A longer template is in section 4 of the [design document](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md).

#### B4. First run

**[WSL]**

```bash
cd /mnt/c/src/MyApp
claude
```

**[Claude Code]**

```
Build the solution, run the tests, and start the app with run_app to check that it opens and closes.
```

If all three work, the app is ready for everyday development. Commit the initial state with git.

### Part C. Everyday development with Claude Code

Just describe what you want. You don't need to say "build" or "test"; the skill makes Claude Code check its own work.

**Example prompts**

```
Add a "Convert" button that converts Celsius to Fahrenheit. Write unit tests for the conversion first.
```

```
The app freezes when I click "Load". Find out why and fix it.
```

```
Fix the build errors.
```

**What Claude Code does**

1. Writes or changes a unit test, and runs `test` to see it **fail** first (this shows the test really checks something).
2. Changes the code.
3. Runs `test` again until all tests pass. On a build error (`error CS....`, `error C....`, `error LNK....`) it reads the first error and fixes it.
4. For UI changes, runs `build` and then `run_app`, and checks that the window appears, responds, and closes.
5. Reports what it changed and what it checked.

**Tips for good results**

- Keep logic out of the UI (out of code-behind, dialogs and message handlers), so that it can be tested with unit tests. `run_app` can tell whether a window opens and responds, but not what it shows.
- Ask for small steps. Each step gets its own build-and-test check.
- Review the git diff before you commit, especially changes to `.csproj` / `.vcxproj` files.

#### Tips

- **Dropbox / OneDrive:** if your source is in a synced folder, exclude `bin` and `obj`. For Dropbox, in PowerShell:

  ```powershell
  Get-ChildItem -Recurse -Directory -Include bin,obj C:\path\to\repo |
    ForEach-Object { Set-Content -Path $_.FullName -Stream com.dropbox.ignored -Value 1 }
  ```

- **Long output:** only the last 12,000 characters of output are returned. Ask Claude Code to filter tests or build one project at a time.
- **Encoding:** output in Shift-JIS is converted to UTF-8 automatically. Source files should be UTF-8.

### Part D. When Claude Code asks you for something

Claude Code should stop and ask you only in these cases. Each request tells you what it tried, what it got, and exactly what you should do.

| Claude Code says | What you do |
|------------------|-------------|
| `windev` tools are not available | Check `/mcp`. If it is not connected, check A2–A4 and restart Claude Code |
| An app is not registered (`登録されていないアプリです`) | Add it to `apps` in `bridge.json` (B2) and restart Claude Code |
| The path is outside the workspace (`作業フォルダーの外`) | Move the repository under `workspaceRoot`, or change `workspaceRoot` and restart |
| Visual Studio is not found, or `MSB8020` (toolset missing), or MFC / Windows SDK missing | Add the component in Visual Studio Installer, or set `msbuildPath` in `bridge.json` |
| It wants to edit `.csproj` / `.vcxproj` / `.props` (permission prompt) | Read the reason and approve or reject |
| A build or test timed out and needs more time | Raise `buildTimeoutSeconds` / `testTimeoutSeconds` and restart |

More errors and fixes: [skills/windows-desktop-dev/references/troubleshooting.md](skills/windows-desktop-dev/references/troubleshooting.md).

---

## Security: what Claude Code can and cannot do

| Layer | What it prevents |
|-------|------------------|
| Bash sandbox (kept on) | Running Windows programs from Bash, network access to the Windows host |
| Claude Code permissions (A5) | Changing the bridge, its settings, the MCP registration or the permissions. Edits to build definitions need your approval |
| The bridge | Anything except the three tools. Paths must be existing projects (or test DLLs) inside `workspaceRoot`; configuration and platform come from fixed lists; only registered apps run; no shell; time limits on everything |

**Remaining risk, stated honestly:** allowing build and test means allowing code to run as your Windows user. Claude Code can edit tests, app code and (with your approval) build definitions, and these run during the build or test. Argument checks cannot prevent this. If you work with code you do not fully trust, run the bridge as a low-privilege Windows user or in a VM, connected with `ssh`. Only the `claude mcp add` command changes. See section 3.3 of the [design document](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md).

---

## Status

| Item | Status |
|------|--------|
| Design document (approach, setup, security, `CLAUDE.md` template) | Done |
| Bridge unit tests (79 tests, including C++ support) | Passing on Linux |
| MCP handshake and tool calls on Linux (including C++ requests) | Verified |
| `dotnet publish -r win-x64` | Done, no warnings |
| Skill `windows-desktop-dev` | Draft, reviewed |
| Part B1 C# commands (`dotnet new`, `dotnet sln add`, `dotnet add reference`) | Verified on Linux |
| Calling `build` / `test` / `run_app` from Claude Code on Windows | **Not yet verified** |
| One full autonomous loop on the sample apps (C# and C++) | **Not yet verified** |
| WinUI 3 | **Not yet verified** |

---

## Developing the bridge itself

**[WSL]**

```bash
dotnet build WinDevBridge.slnx
dotnet test src/WinDevBridge.Tests
dotnet publish src/WinDevBridge -c Release -r win-x64 --self-contained false -o artifacts/WinDevBridge
```

Inside the Claude Code sandbox, the home directory is not writable and NuGet certificate revocation checks are blocked. Set `DOTNET_CLI_HOME` and `NUGET_PACKAGES` to a directory under `$TMPDIR`, and set `NUGET_CERT_REVOCATION_MODE=offline` first.

To support a new kind of project (for example, Google Test), or to change the tools, read sections 1–3 of the [design document](Plans/20261014-1746-WindowsDesktopAutonomousDevelopment.md).
