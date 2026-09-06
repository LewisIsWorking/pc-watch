# PC Watch roadmap

Things worth building, and why. Dated so a stale entry is obvious rather than authoritative.

---

## Inherited from Sneaky Process Killer (archived 2026-09-06)

`LewisIsWorking/SneakyProcessKiller` was a private WPF tool written in a single burst on
2026-08-19, then superseded by PC Watch. It is archived rather than deleted, and its code remains
readable at that repo.

PC Watch already absorbed its two most important lessons: the ancestry walk **rejects a parent
whose start time is later than its child's**, because a recycled pid otherwise makes the tool name
an innocent process as the culprit in a sentence indistinguishable from a correct one
(`ProcessAncestry.cs`); and it **reports rather than accuses**, because "this is abandoned" was
measured and found to fire on ordinary autostart apps.

These four capabilities did **not** make the jump, and are recorded here so archiving the repo does
not quietly lose them.

### 1. Family grouping, not name grouping

Task Manager says `chrome.exe` is using 17% across 83 processes. Neither that nor a top-N list by
name tells you those 83 share one launcher that exited three hours ago.

PC Watch's `SuspectAnalyzer` currently *advises* the reader to "group the list by name before
hunting individual rows", which is a note telling the user to do the work in their head. Grouping by
**family** (the ancestry tree, not the executable name) is what turns 83 rows of 0.2% into one row
of 17%, and 0.2% is below every threshold PC Watch has.

### 2. Kill by CATEGORY, re-resolved, never by pid

⛔ The sharpest of the four. A report is a snapshot of one moment. By the time it is read and a
button is pressed, Windows may have reissued those process ids to something else entirely.

`ProcessKiller` currently re-checks the process **name** before terminating, which narrows that
window without closing it. Sneaky Process Killer selected by category and re-resolved the category
against a **fresh snapshot at the moment the button was pressed**, so a recycled id could not be
inherited from a stale view.

### 3. Threshold-triggered alerting

PC Watch is a dashboard you open. It cannot tell you about a problem you are not already looking
at, which is most of them.

⚠️ The constraint that makes this hard is the one Sneaky Process Killer wrote down: **the watcher
must not cost what it measures.** Enumerating processes is not free, so the expensive work has to
happen only once a cheap threshold has already broken. A poller that samples everything every
second to decide whether to alert has become a cause of the slowness it reports.

### 4. Curated cleanup rules

Named, provable classes of waste, rather than "this has been running a long time":

- orphaned WebDriver trees, and browsers whose driver already exited
- hung `git` processes holding `index.lock`
- **leftover build workers**

⭐ That last one is not hypothetical. On 2026-09-04 this machine was carrying **56 orphaned MSBuild
worker nodes** using 5.6 GB, which read as ordinary `dotnet` processes and sat under every
threshold individually. `emulator-reaper.ps1` in this repo is a hand-rolled one-off doing for one
case what a rule engine would do generically.

A live build's workers must appear only with a warning and never pre-ticked, since telling a
running build from an abandoned one is the entire difficulty.

---

## Test coverage

Tracked by `coverage.ps1`, which enforces a ratchet floor. Target is 100% of production code, with
generated code and both test suites excluded from the denominator.

**92.1% line, 90.7% branch** as of 2026-09-06, across 295 tests.

What is left is mostly not reachable rather than merely untested:

- `Program.Main` ends in `Application.Run`, which blocks for the life of the app. Its decisions were
  extracted (`WantsSelfTest`, `ArgumentValue`, ...) and are covered; the wiring itself is not.
- Two `MessageBox.Show` calls, in `UpdatePrompt` and `LongRunningPanel`. A modal dialog does not
  fail a test, it HANGS it, so both were reduced to a single line with the decision either side
  extracted and covered.
- Hardware paths this machine cannot exercise: the no-NVIDIA fallback in `GpuTelemetry`, and WMI
  refusing in `MachineProbe`. The degradation CONTRACT is tested; the branch cannot be taken here.

## Standards

✅ **Both cleared 2026-09-06.** `ReportRenderer` was 219 lines and is now 113, split into
`ReportSections`. Every file in `app/` and `tests/` is under 200 lines.

⚠️ **The six `partial` classes are NOT a violation, and must not be "fixed".** This was recorded here
as debt on 2026-09-06 and that was wrong.

`GpuTelemetry`, `Native`, `ProcessTable` and `SelfTestRunner` use `[LibraryImport]`; `ReportPrivacy`
and `SuspectAnalyzer` use `[GeneratedRegex]`. Both source generators REQUIRE the containing type to
be `partial`, and every one of the six is a single hand-written file - none is split across two.

Proven rather than argued: removing the modifier from `ReportPrivacy` fails the build with

```
error CS0260: Missing partial modifier on declaration of type 'ReportPrivacy';
              another partial declaration of this type exists
```

That is the same category as the XAML `.g.cs` exception the standards already carve out, and for
the same stated reason: auto-generated and unavoidable. The banned pattern is hand-written code
split across several files to hide complexity, which shares private state, improves nothing, and
makes dependencies implicit. None of that applies here.
