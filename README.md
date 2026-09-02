# PC Watch

A pinnable desktop app showing **real** CPU load, with a live graph and what is causing it.

Built 2026-08-31, because three tools on this machine reported three different "CPU %" at the same
moment and none of them said which quantity it was measuring.

```
<repo>\bin\PcWatch.exe        <- run this
```

---

## Pin it to the taskbar

```powershell
pwsh -File <repo>\install.ps1                     # Start Menu entry
pwsh -File <repo>\install.ps1 -StartWithWindows   # ...and launch at logon
```

Then press <kbd>Win</kbd>, type **PC Watch**, right-click the result, **Pin to taskbar**.

Clicking the pin again **shows the running window** rather than starting a second copy. That is a
named mutex plus a named event: the second process signals the first and exits immediately.

| Interaction | Result |
|---|---|
| Click the pinned button | Opens, or brings the running window to the front |
| Tray icon (double-click) | Same |
| Tray right-click | Show · Copy report · Task Manager · Resource Monitor · Exit |
| Icon colour | green < 50%, amber < 80%, red above, **grey = not measured yet** |

Updates **every second**: headline figure, tray icon, two-minute rolling graph, process table.

It also remembers where you left it — including which monitor — and reopens there. `--monitor right`
(or `left`, `primary`, or a 1-based index) places it once; after that the saved position wins.

## What it shows

- **Real CPU load**, agreeing with Task Manager, from `GetSystemTimes`.
- **How it is running** — named indicators (CPU, memory, GPU, disk), each with a plain verdict.
  Deliberately **not** a single 0-100 score: two machines both scoring 72 can be unwell in
  completely different ways, and nobody can act on a 72. The overall word comes from the **worst**
  indicator, never an average.
- **Power draw** — GPU watts **measured** via NVML, CPU watts **estimated** from load and clearly
  labelled as such. AMD package power needs a kernel driver, so an honest estimate beats a figure
  that looks measured. The two are never added into one unlabelled number.
- **By program** — several processes sharing a name, added together. A machine at 100% CPU listing
  nothing above 12% is thirty `dotnet` processes at 2% each, and only grouping shows it.
- **Alive over a day**, with a kill button, an owner column, and a denylist that refuses the
  processes whose termination bugchecks Windows.
- **Who launched it**, before any advice about closing it.

---

## The measurement problem it was built to solve

Measured on this box, 2026-08-31, all within seconds of each other:

| Source | Reading | What it actually measures |
|---|---|---|
| Windhawk taskbar mod | **98%** | ⚠️ `% Processor Performance` — **clock speed ÷ base clock** |
| Task Manager | 67% | `% Processor Utility` — frequency-aware utilisation |
| `Get-Counter` | 88% | `% Processor Time` — plain busy-vs-idle |

`% Processor Performance` **is not a load metric.** Five consecutive samples: real load moved
90 → 100% while it sat at 98.6 every time. Load then halved to 50% and it *still* read 98. This
Ryzen 9 5900X has a 3701 MHz base and idles near it, so that counter reads ~98% forever.

PC Watch uses `GetSystemTimes`, the kernel call Task Manager is built on, so its number agrees with
Task Manager rather than being a fourth rival figure. Verified over matched 12-second windows
against `Get-Counter`: bias **1.1, 0.0, 0.1** points.

When the clock ratio is high but load is not, the window says so outright.

---

## It tells you who owns a process before telling you to close it

The analyzer flagged `qemu-system-x86_64` at 16% and advised closing it. Tracing the parents:

```
emulator <- bash <- bash <- bash <- claude.exe (pid 84528) <- pwsh <- WindowsTerminal
```

**Another agent session had launched it**, and `adb devices` showed it connected. Acting on that
advice would have killed live work. Load cannot separate a runaway leftover from something in active
use; ancestry can. Every flagged process carries a `launched by ...` line **above** the advice.

The owner is the first *named* owner in the chain (`claude`, `node`, `dotnet`, `rider64`, …), not
the nearest non-shell — otherwise the answer for the emulator is `emulator`, which is true and
useless.

It also reports **how much of the load the list explains** (`these 14 account for 60.5% of the 80%
in use (75%)`). Measured once at 91% CPU with the top twelve summing to 47%: the list was naming a
sixth of the problem while reading as a complete account.

---

## "On for 18 days" was wrong, and here is why

The app reported **18.8 days** for a PC that had been on for a day and a half. Measured:

| Source | Reading |
|---|---|
| `GetTickCount64` | 18.8 days |
| WMI `LastBootUpTime` | 18.8 days (13 Aug) |
| `HiberbootEnabled` | **1** — Fast Startup is ON |
| Kernel-Boot event 27 | 30 Aug 11:16:56, **boot type 0x1** (hiberboot) |
| `explorer.exe` start | 30 Aug 11:16:59 |

With Fast Startup, "shut down" **hibernates the kernel session** instead of stopping it, so neither
counter resets. They are not broken — they answer *"how long since a full boot"*, a different
question — and **they agreed with each other perfectly while both being useless for the one being
asked.** Two sources agreeing is not evidence that either is right.

`SystemUptime.cs` reads the System event log for the most recent boot or resume, which is the only
authoritative record. The window now shows `ON 1d 12h (since Sun 30 Aug 11:16)` and explains the
larger figure other tools display.

You can see the split in the process list itself: `System` and `MsMpEng` show `up 18d 20h`
(kernel-session, survived the hiberboot) while every user-session process shows `up 1d 12h`.

---

## Building

Needs the **.NET 11 SDK** (preview 7 or later). Nothing else — no NuGet dependencies at all.

```powershell
cd app
dotnet publish -c Release -r win-x64 -o ..\bin      # self-contained, single file
..\bin\PcWatch.exe --self-test                      # 55 checks, exit code 0 or 1
```

Published **self-contained** (~108 MB) because there is no system-wide .NET 11 runtime yet, and a
monitoring tool that only starts from a shell with `DOTNET_ROOT` set is not a tool anyone can pin.

To build against .NET 10 instead — every feature works except the .NET 11 visual styles opt-in:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PcWatchTfm=net10.0-windows -o ..\bin-net10
```

### Is .NET 11 faster?

Measured, 5 runs of `--self-test` each, same machine:

| Build | avg | min | max |
|---|---|---|---|
| .NET 10, framework-dependent | 3415 ms | 2914 | 4867 |
| .NET 11, self-contained | **2956 ms** | 2788 | 3099 |

About **13% faster on average and far more consistent** — the .NET 10 spread is nearly 2 seconds
wide, the .NET 11 spread about 300 ms. ⚠️ Startup dominates this: the workload is short, so much of what
is compared is runtime init rather than throughput, and the two builds differ in deployment mode as
well as runtime. Both numbers are real; the gap is not purely "11 vs 10".

`VisualStylesMode.Net11` is enabled and changed **nothing visible** — it restyles *stock* controls,
and this window is a custom-painted chart, two labels, a `ListView` and a monospace `RichTextBox`.

### It did surface a real behaviour change

`Environment.TickCount64` **excludes sleep on .NET 11**. Same code, same machine, seconds apart:

| Runtime | Reading | Equals |
|---|---|---|
| .NET 10.0.11 | 19.83 days | `GetTickCount64` (includes sleep) |
| .NET 11.0.0-preview.7 | 15.71 days | `QueryUnbiasedInterruptTime` (excludes sleep) |

The 4.12-day gap is time this PC spent asleep. Neither value is wrong — they answer different
questions — and nothing flagged the change. It was noticed only because a *boot counter appeared to
go backwards* between two builds. `Native.TimeSinceBootIncludingSleep` now P/Invokes
`GetTickCount64` explicitly, so both builds agree.

---

## Traps this code exists to avoid

**Per-process CPU is per-core.** The raw figure runs to `100 × cores`; on 24 cores one process
reports **880%**, which is 36.7% of the machine. Everything here is divided by core count, and the
self-test fails if any process exceeds 100%.

**The counter APIs are far too slow to tick on.** `Get-Counter` **2127 ms**, WMI
`Win32_PerfFormattedData` **1475 ms**, `GetSystemTimes` **under 1 ms**. Clock and memory avoid WMI
too, via `CallNtPowerInformation` and `GlobalMemoryStatusEx`, so a one-second tick is affordable.

**`Icon.FromHandle` leaks.** It does not own the `HICON` from `Bitmap.GetHicon()`, so disposing the
managed `Icon` orphans the handle. At one icon per second that is 3600 leaked GDI handles an hour
against a 10,000 quota: under three hours to death, comfortably long enough to look correct.
`TrayIconRenderer` owns the lifetime; the self-test renders 300 icons and fails above 100 growth.

**`Process` objects hold native handles.** Reading `TotalProcessorTime` opens one. Measured in the
PowerShell original: **+24 handles per 40 s**, the quota in about four hours. Disposed in a `using`.

**`AbandonedMutexException` is a success, not a refusal.** It means the wait succeeded and the last
owner died holding the mutex. Unhandled, crashing one instance killed the *next* launch too, about
ten seconds in, after startup work completed — so it appeared in the task list first.

**An assigned `ClientSize` does not arrive as assigned.** On this 125% display, asking for 800 tall
produced 640 — exactly 96/120 — and the process table was clipped. WinForms applies that conversion
under PerMonitorV2 regardless of `AutoScaleMode`. Found only by probing `GetWindowRect` directly;
the sizes in code and on screen never matched and nothing reported an error. See
`DashboardLayout.MeasuredClientSize`.

**`GetPositionFromCharIndex` clamps to the visible area.** It returns an in-view `Y` for text far
below the fold, so an overflow test built on it can never fire — a measurement that cannot detect
the condition it exists to detect. `ReportFitter` uses the control's **line count** instead, and
`ReportRenderer` emits the findings **before** the process table so that a residual error clips the
tail of a sorted list rather than the diagnosis. Four attempts to *calculate* the fit all failed;
ordering by importance retired the problem.

**A preserved scroll offset reads as a missing measurement.** Restoring the previous scroll position
each tick carried the view down as the report changed length, quietly hiding the `RAM` row. A header
line scrolled out of sight looks exactly like a value the app failed to collect.

**Screenshots capture the wrong window.** `CopyFromScreen` photographs whatever pixels occupy a
rectangle, and Windows blocks `SetForegroundWindow` from a background process. Two attempts produced
confident screenshots of a video and a browser. `capture.ps1` uses `PrintWindow` with
`PW_RENDERFULLCONTENT`, which asks the window to render itself and cannot capture anything else.

---

## Layout

| File | Role |
|---|---|
| `app/Program.cs` | Entry point, single-instance activation, `--self-test` |
| `app/MainForm.cs` | The window: headline, graph, report, tray |
| `app/CpuSampler.cs` | Delta-based sampling |
| `app/SuspectAnalyzer.cs` | The heuristics that turn a process list into a diagnosis |
| `app/ProcessAncestry.cs` | "Who launched this?" |
| `app/ProcessTable.cs` | Toolhelp snapshot: every process with its parent |
| `app/ReportRenderer.cs` | The text report |
| `app/CpuHistoryChart.cs` | Rolling graph |
| `app/TrayIconRenderer.cs` | Dynamic tray icon and handle lifetime |
| `app/Native.cs`, `Theme.cs`, `Models.cs` | Interop, palette, data |
| `app/make-icon.ps1` | Builds the multi-resolution `PcWatch.ico` |
| `app/capture.ps1`, `probe-window.ps1` | Screenshot and window diagnostics |
| `install.ps1` | Start Menu / startup shortcuts |
| `legacy-powershell/` | **Superseded.** See below. |

Every file is under 200 lines. No partial classes except the interop ones `LibraryImport` requires.

---

## Tests

```powershell
<repo>\bin\PcWatch.exe --self-test
```

23 checks, exit code 0 or 1. Each rule is fed the bug it was written for and asserted to **fire**,
then fed the near-miss and asserted to stay **silent**. The second half is the point: the first
draft flagged `vmmemWSL` at **0.9%** as HIGH while the machine's real problem sat at 14%, and a
panel that shouts about a hundredth of the CPU is a panel you stop reading.

It also pins two traps: pid `152` must not be swallowed as "already reported" by a claimed pid
`15264` (the dedup matches an integer set, never rendered text), and 88% CPU spread across many
small processes must **not** report "nothing obviously wrong".

---

## `legacy-powershell/` is superseded — do not edit it

The original was a tray-only PowerShell tool. It could not be pinned, because Windows offers
"Pin to taskbar" only for shortcuts to **executables** — never `.vbs` or `.ps1`.

Its logic was ported here and its tests became `--self-test`, so the evidence was not lost. It is
kept only as the reference the port was made from. **Two copies of the same heuristics drift, and a
half-updated copy reads as authoritative**, so fix things here and here only.

---

## Known limits

- **CPU only.** No disk-queue or GPU figure. If the app says CPU is fine and the machine still
  drags, that is the next place to look and this will not point you there.
- **Ownership is a heuristic.** The owner list in `ProcessAncestry` is finite; an unlisted launcher
  falls back to the nearest non-shell. The full chain is printed in brackets so you can overrule it.
- **The recycled-pid guard is partial.** It needs both start times, and Windows refuses them for
  some processes (System, Idle, other users). Those links are accepted unchecked.
- **`% Processor Utility` is not shown.** Task Manager's exact number is frequency-weighted with no
  cheap API. The plain busy figure is within a couple of points and is honest about what it counts.
