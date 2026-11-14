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

# Release, without rebuilding
dotnet test tests/TaskbarNetMonitor.Tests/TaskbarNetMonitor.Tests.csproj \
  -c Release --no-build

# coverage (coverlet, writes coverage.cobertura.xml)
dotnet test tests/TaskbarNetMonitor.Tests/TaskbarNetMonitor.Tests.csproj \
  --collect:"XPlat Code Coverage" --results-directory TestResults
```

The suite currently runs 299 tests and takes about a second. Five of them drive real
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
# Needs the .NET 11 runtime on the target machine. Smallest output, ~5 files.
dotnet publish TaskbarNetMonitor.csproj -c Release -r win-x64 --self-contained false -o publish

# One self-contained exe, no runtime needed on the target machine (~115 MB).
dotnet publish TaskbarNetMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Then `publish/TaskbarNetMonitor.exe` is the thing to hand out. Copy the whole `publish`
folder for the framework-dependent build.

Trimming (`-p:PublishTrimmed=true`) is not supported here: WinForms is not trim-compatible,
so the resulting build is not trusted to run. A normal single-file publish is fine.

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
Core/                      config, styles, fonts, geometry, traffic sampling
Interop/                   Win32 declarations
Rendering/                 layout and the widget renderer
UI/                        layered window, taskbar widget, settings dialog, host
Assets/TaskbarNetMonitor.ico   app icon
tools/make_icon.py         regenerates the icon from the design grid
tests/TaskbarNetMonitor.Tests  xUnit tests
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
