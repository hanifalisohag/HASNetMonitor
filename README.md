# Taskbar Network Monitor

A small always-on-top Windows widget that sits on the taskbar next to the tray and
shows live download and upload speed as coloured bars, text, or both.

It is taskbar-owned, seats itself above the shell, and repairs its own presence if the
shell hides or buries it.

- Eight looks, two stacking orders, selectable font family/size/weight
- Position it by distance from the tray, then nudge it left/right/up/down in pixels
- Settings apply to the live widget as you change them
- Optional start with Windows (per-user `Run` key, no admin rights needed)
- Config is a plain JSON file you can edit by hand

---

## Requirements

| | |
|---|---|
| OS | Windows 11 (developed and tested here); Windows 10 is untested |
| .NET SDK | 11.0.100 or later (`dotnet --version`) |
| Python (optional) | Only to regenerate the icon; needs Pillow |

The app targets `net11.0-windows` and uses WinForms. It runs from a single folder and
needs no installer.

## Build

```bash
dotnet restore
dotnet build
```

Debug output lands in `bin/Debug/net11.0-windows/TaskbarNetMonitor.exe`.

Always target `TaskbarNetMonitor.csproj` explicitly when you use `-o`/`--output` or
publish. The folder also contains `TaskbarNetMonitor.slnx`, which includes the test
project, and an output path applied to the solution copies test assemblies into the
app folder (`NETSDK1194`).

## Run

```bash
# from source (rebuilds if needed)
dotnet run --project TaskbarNetMonitor.csproj

# the built exe
./bin/Debug/net11.0-windows/TaskbarNetMonitor.exe
```

Only one instance runs: a second copy takes the `Local\TaskbarNetMonitor.Instance`
mutex and exits quietly, so two widgets never fight over the same spot.

Run it for a fixed time when you just want to look at it (seconds, capped at 600):

```bash
./bin/Debug/net11.0-windows/TaskbarNetMonitor.exe --smoke 10
```

Right-click the widget for the menu. It has no taskbar button, so quit it from the menu
(**Exit**) rather than closing a window.

## Test

```bash
dotnet test                                  # everything
```

Run the test project directly for filtered or Release runs:

```bash
# one class
dotnet test tests/TaskbarNetMonitor.Tests/TaskbarNetMonitor.Tests.csproj \
  --filter "FullyQualifiedName~TaskbarGeometryTests"

# one test
dotnet test tests/TaskbarNetMonitor.Tests/TaskbarNetMonitor.Tests.csproj \
  --filter "FullyQualifiedName~NudgeMovesTheWidgetByExactlyTheGivenPixels"

# only the target you ship
dotnet test tests/TaskbarNetMonitor.Tests/TaskbarNetMonitor.Tests.csproj -f net48

# Release, without rebuilding
dotnet test tests/TaskbarNetMonitor.Tests/TaskbarNetMonitor.Tests.csproj \
  -c Release --no-build

# coverage (coverlet, writes coverage.cobertura.xml)
dotnet test tests/TaskbarNetMonitor.Tests/TaskbarNetMonitor.Tests.csproj \
  --collect:"XPlat Code Coverage" --results-directory TestResults
```

The suite is 320 tests per target, and it runs against **both** app targets, so the
`net48` build that ships is held to exactly the same tests as the development build.
Use `-f net48` to test only the shipping target — slower per test, but it exercises the
.NET Framework code paths (config parsing, compatibility helpers). Five of them drive real
windows (visibility, hit-testing, topmost repair). They are `SkippableFact`s and are
reported as **skipped** when the session is locked or otherwise cannot show a window —
that is expected, not a failure. Unlock and run again to exercise them.

## Clean

```bash
dotnet clean
dotnet clean -c Release
```

Removes `bin/` and `obj/` artefacts for that configuration.

## Release

Publish the project, not the folder:

```bash
# The one to distribute: 239 KB exe, nothing to install on the target.
dotnet publish TaskbarNetMonitor.csproj -c Release -f net48 -o publish

# Needs the .NET 11 Desktop Runtime on the target machine (~5 files).
dotnet publish TaskbarNetMonitor.csproj -c Release -f net11.0-windows \
  -r win-x64 --self-contained false -o publish

# One self-contained exe, nothing to install, but ~53 MB compressed / 115 MB plain.
dotnet publish TaskbarNetMonitor.csproj -c Release -f net11.0-windows \
  -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

See [Shipping a small exe](#shipping-a-small-exe) for what to hand out and why.

Trimming (`-p:PublishTrimmed=true`) and NativeAOT are refused by the SDK for WinForms
(`NETSDK1175`), so neither can shrink the modern build. That is the reason the small
build targets .NET Framework instead.

## Shipping a small exe

```bash
dotnet publish TaskbarNetMonitor.csproj -c Release -f net48 -o publish
```

Output:

| File | Size | Needed? |
|---|---|---|
| `TaskbarNetMonitor.exe` | 239 KB | yes |
| `TaskbarNetMonitor.exe.config` | 0.6 KB | yes, carries `DpiAwareness=PerMonitorV2` |
| `TaskbarNetMonitor.pdb` | 36 KB | no, debug symbols |

Copy the two files together. Without the `.config` the app still runs, but the settings
window is bitmap-stretched on a display scaled above 100%.

**Why .NET Framework.** No modern .NET is part of Windows: the .NET 11 Desktop Runtime is
a separate installer, and a self-contained build carries ~50 MB of runtime. .NET Framework is
an OS component, so a Framework exe contains only this app's code — hence 239 KB rather
than 53 MB. The in-box versions are: Windows 8 → 4.5, 8.1 → 4.5.1, 10 1809 → 4.7.2,
10 1903+ and 11 → 4.8. This build targets 4.8, so **Windows 10 1903+ and Windows 11** are
supported; Windows 8/8.1 would need a 4.5 fallback that is not built.

What the `net48` build does differently, so it stays small and dependency-free:

- **Config JSON** uses the in-box `JavaScriptSerializer` instead of `System.Text.Json`,
  which would drag several DLLs next to the exe. A small pre-pass strips `//` and `/* */`
  comments and trailing commas first, so a config file stays interchangeable between the
  two builds. `Core/ConfigJson.cs`.
- **Compatibility helpers** in `Core/Compat.cs`, `Core/Numeric.cs` and `Core/Elapsed.cs`
  stand in for `Math.Clamp`, `double.IsFinite`, `Stopwatch.GetElapsedTime`,
  `Environment.ProcessPath` and `NativeLibrary`, which the Framework lacks.
- **DPI awareness** comes from `App.config` (the Framework equivalent of the project's
  `ApplicationHighDpiMode`), not from the modern runtime host.

Both targets compile from the same sources with `#if NETFRAMEWORK` only inside those
compatibility files, and the whole test suite runs on both.

## Diagnostics

```bash
# prints a report, then exits 0 on PASS and 1 on FAIL
./bin/Debug/net11.0-windows/TaskbarNetMonitor.exe --self-test

# or run it straight from source
dotnet run --project TaskbarNetMonitor.csproj -- --self-test
```

`--self-test` also writes a rendered frame to
`%TEMP%\taskbar-monitor-preview.png`, which is the quickest way to inspect a look
without touching your taskbar.

If the widget misbehaves (hides, blinks, jumps), log what it sees:

```bash
TNM_TRACE=trace.log ./bin/Debug/net11.0-windows/TaskbarNetMonitor.exe --smoke 10
```

`TNM_TRACE` takes a file path and records every show/hide/reorder decision with a
millisecond timestamp, plus a sampler that notes when the widget's pixels go blank.
The file is appended to, so delete it between runs. Leave the variable unset to turn
tracing off.

## Configuration

Settings are stored per user at:

```
%APPDATA%\TaskbarNetMonitor\config.json
```

The menu's **Open settings folder** opens it. On first run a config is written with
defaults. A `config.json` next to the exe is read as a fallback and then saved to the
per-user path. The file is plain JSON, tolerates comments and trailing commas, and is
repaired on load — out-of-range values are clamped, unknown style names fall back to
`Bars`.

| Key | Default | Range / values |
|---|---|---|
| `DownloadColorHex` | `#2ECC71` | any `#RRGGBB` |
| `UploadColorHex` | `#E74C3C` | any `#RRGGBB` |
| `RefreshIntervalMs` | `1000` | 250–60000 |
| `OffsetFromTrayPx` | `6` | 0–64 |
| `NudgeXPx` | `0` | −400–400 |
| `NudgeYPx` | `0` | −400–400 |
| `AutoScale` | `true` | `true`/`false` |
| `MaxScaleMbps` | `100` | 0.01–100000 |
| `ShowText` | `true` | `true`/`false` |
| `ShowBackdrop` | `false` | `true`/`false` |
| `BackdropOpacity` | `120` | 0–255 |
| `BarThicknessPx` | `3` | 1–24 |
| `SegmentGapPx` | `2` | 0–12 |
| `FontSizePt` | `9` | 6–20 |
| `Style` | `Bars` | see below |
| `SortOrder` | `DownloadFirst` | `DownloadFirst`, `UploadFirst` |
| `FontFamily` | empty | an installed family name, or empty to match the taskbar |
| `FontBold` | `false` | `true`/`false` |
| `FontItalic` | `false` | `true`/`false` |
| `RoundedBars` | `false` | `true`/`false` |
| `AccentGlow` | `false` | `true`/`false` |
| `StartWithWindows` | `true` | `true`/`false` |

Styles: `Bars` (classic rows), `Level` (segmented level meter), `Sidebar` (vertical
bars), `SidebarSegments` (vertical bars split by gaps), `Neon` (glow, rounded caps),
`Segments` (chunky blocks), `Dots` (dot matrix), `Minimal` (hairline fills, no track).

A nudge is applied after the normal seat next to the tray and is clamped to the
taskbar's own bounds — it can reach the bar edge but never leave the bar. On a 48 px
taskbar there are only a few pixels of vertical room, so a large `NudgeYPx` there
does little by design.

Start with Windows writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`,
value `TaskbarNetMonitor`, pointing at the executable that is running. The config is the
source of truth: the key is re-applied on every start, which also repairs it after a
Windows update removes it.

To start over, exit the app and delete the config folder.

## Settings dialog

Changes apply to the live widget immediately — the dialog also previews them on a mock
taskbar.

- **OK** saves to `config.json`.
- **Cancel** (or closing the window) restores what was there before you opened it.

Groups: **Look** (style, stacking, font, weight, bar shape, thickness, segment gap,
font size, text), **Position** (offset from tray, nudges with arrow buttons, reset),
**Colours and behaviour** (colours, refresh, scaling, backdrop, start with Windows).

## Tray menu

| Item | What it does |
|---|---|
| Settings… | Opens the dialog |
| Look | Style, stacking order, bold, italic |
| Position | Nudge left/right 4 px, up/down 2 px, reset |
| Reset bar scaling | Resets the auto-scaler and the smoothed bar values |
| Start with Windows | Toggles the `Run` entry |
| Open settings folder | Opens `%APPDATA%\TaskbarNetMonitor` |
| Exit | Quits |

## Project layout

```
Program.cs                 entry point, --self-test / --smoke, single-instance guard
SelfTest.cs                environment and rendering checks behind --self-test
App.config                 DPI awareness for the net48 build
Core/                      config, styles, fonts, geometry, traffic sampling
  ConfigJson.cs            config JSON, one backend per target
  Compat.cs                net48 stand-ins for .NET 11 only APIs
  Numeric.cs, Elapsed.cs   Math.Clamp / IsFinite / Stopwatch helpers, both targets
  IsExternalInit.cs        net48 polyfill for `init` accessors
Interop/                   Win32 declarations
Rendering/                 layout and the widget renderer
UI/                        layered window, taskbar widget, settings dialog, host
Assets/TaskbarNetMonitor.ico   app icon
tools/make_icon.py         regenerates the icon from the design grid
tests/TaskbarNetMonitor.Tests  xUnit tests, run on both targets
  MathBridge.cs            net48 alias so the tests' Math.Clamp compiles
```

## Regenerating the icon

`Assets/TaskbarNetMonitor.ico` is the single definition of the app mark: it drives the
exe icon in Explorer, the notification-area (tray) icon, and the settings dialog. The
file is also embedded as an assembly resource, so the tray and dialog load it without
depending on where the exe lives — no surface draws its own copy, which is what keeps
them identical.

To change the mark, edit the 16-unit grid in `tools/make_icon.py` and run:

```bash
python -m pip install pillow   # once
python tools/make_icon.py
dotnet build -t:Rebuild
```

Keep every size in `SIZES` in `tools/make_icon.py`: the shell asks for 16, 20, 24, 32
and 40 px depending on DPI and view, and a missing frame gets scaled, which looks soft.

## Troubleshooting

**The widget is not visible.** Open Start once; the widget re-seats itself above the
shell. If it is still missing, check that it is not off the taskbar: **Position → Reset
position**.

**A second copy exits immediately.** By design — one instance only.

**Tests skip instead of running.** The session is locked or has no interactive
desktop, so window visibility and hit-testing cannot be observed. Unlock and rerun.

**Tests fail on window behaviour after a big change.** Re-run with `--self-test` and
compare the `[info] taskbar=… tray=… dpi=…` line against your taskbar; it reports the
geometry the app actually resolved.
