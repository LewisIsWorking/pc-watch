# Security and privacy

PC Watch reads your machine's performance counters and process list. This document states exactly
what it reads, what it shows, and what leaves your computer — and how each claim is enforced rather
than merely promised.

## What leaves your computer

Only requests to GitHub, and only these three (as of v1.2.0, 2026-09-21):

| When | Request | You can prevent it |
|---|---|---|
| On launch | one HTTPS GET to `api.github.com`, asking whether a newer release exists | yes, permanently — see below |
| You click **Check for updates** in the tray menu | the same GET | it happens only when you click, even with automatic checks off |
| You accept **Install it now** | a download of that release's asset from GitHub | it happens only when you accept |

Each carries a `User-Agent` of `PcWatch/<version>` and nothing else — no process names, no machine
details, no identifier, no analytics.

Turn the automatic check off permanently:

```powershell
PcWatch.exe --no-update-check
```

or set `"CheckForUpdates": false` in `%APPDATA%\PcWatch\settings.json`. The opt-out is honoured
**before** the request is made: a check that fires and then discards the answer has already told
GitHub the app is running, which is the part being opted out of.

There is no telemetry, no crash reporting and no other network code in the project.

### Installing an update

Accepting an update never runs anything unverified. The download is checked against the **SHA-256
checksum GitHub publishes for that asset** before anything on disk is touched; a mismatched file is
deleted. A release with no published checksum is not installed at all — the download page opens
instead, and nothing is fetched. The same happens for any copy that is not the published single-file
exe (a development build, for instance).

Only `PcWatch.exe` at the root of the archive is extracted, by exact name, so an archive entry such
as `..\something.exe` cannot write outside the working folder. The running exe is never overwritten:
it is renamed to `PcWatch.exe.old`, the new one takes its place, and the next launch deletes the old
copy. If putting the new file in place fails, the original is renamed back.

## What it deliberately does NOT read

**Process command lines.** Task Manager shows them, they look like an obvious upgrade for a process
list, and they routinely contain `--token=`, `--password=` and connection strings. PC Watch takes
process **names only**, from a `CreateToolhelp32Snapshot` — an API that cannot return a command line
even by accident.

It also never reads window titles, environment variables, file contents, or anything on disk beyond
free space on the system drive.

`Environment.UserName` appears once, in the name of a local mutex used for single-instance
detection. It is never displayed, written to the report, or transmitted.

## What "Copy report" puts on your clipboard

The report is designed to be pasted into a bug report, so treat everything in it as published:

- CPU model, core count, memory size, uptime, disk free space
- GPU model, wattage, temperature
- **Process names, PIDs, memory and age**, and the parent chain that launched them

Process names can be revealing in themselves — a project codename in an executable name, or which
VPN, chat and development tools you run. Read it before you paste it.

It contains no file paths, no username and no machine name, and that is **enforced by the self-test**
(see below) rather than assumed.

## What it can change

The only destructive action is the **kill button**. It refuses processes whose termination bugchecks
Windows (`csrss`, `wininit`, `winlogon`, `services`, `smss`, `lsass`, `svchost`, and others), warns
before ending anything that takes work with it, and re-checks the process name against the live
process before killing — pids get recycled, and the row you clicked was rendered up to a second ago.

It never elevates or installs a driver. It writes to `%APPDATA%\PcWatch\` (its settings), and — only
when you accept an update — to `%TEMP%\PcWatch-update\` (the download) and to the folder holding
`PcWatch.exe` (the new exe, and the old one until the next launch removes it). If that folder is not
writable by you, the update is refused and nothing is changed.

## How these claims are enforced

```powershell
PcWatch.exe --self-test              # 73 checks as of v1.2.0, exit code 0 or 1
pwsh -File app\check-no-leaks.ps1    # scans the BUILT BINARY and tracked files
```

`--self-test` renders a report from a **live snapshot of the real machine** and fails if it contains
a filesystem path, the current username, the machine name, anything shaped like a command-line
argument or credential, or an expanded environment variable. A fixture would only ever contain what
the test author put in it, and would keep passing no matter what the renderer started including.

`check-no-leaks.ps1` scans the **compiled artefact**, not the source. It was written after the
published 1.1.0 binary was found to contain the builder's home directory in its debug directory —
a string the compiler writes by default, which appears nowhere in the repository and which a
source-only check would have missed entirely. Builds now use `DebugType=embedded`, a `PathMap` onto
a neutral `/src/` root, and `ContinuousIntegrationBuild` for reproducibility.

The needles are derived from the machine running the check (`$env:USERNAME`, `$env:COMPUTERNAME`),
so it protects whoever builds it. A hardcoded name would silently stop finding anything the moment
somebody else cloned the project.

## Reporting a problem

Open an issue at https://github.com/LewisIsWorking/pc-watch/issues. Please do not include an
unreviewed `Copy report` dump.
