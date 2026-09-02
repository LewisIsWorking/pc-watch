namespace PcWatch;

/// <summary>
/// Processes alive for over a day, with a button to end the selected one.
/// </summary>
/// <remarks>
/// 2026-09-02. Sorted by memory, because "which of these old things is costing me something" is the
/// question a kill button answers. Age alone would put csrss at the top of every machine ever built.
///
/// ⚠️ The owner column is not decoration. The emulator this app once advised closing turned out to
/// belong to a live agent session; a kill button without an owner column would have made that a
/// single click. Ownership is resolved lazily and cached, since it costs a process-table snapshot.
/// </remarks>
public sealed class LongRunningPanel : UserControl
{
    private readonly ListView _list = new();
    private readonly Button _kill = new();
    private readonly Label _status = new();
    private readonly ProcessAncestry _ancestry;

    public LongRunningPanel(ProcessAncestry ancestry)
    {
        _ancestry = ancestry;
        BackColor = Theme.Panel;

        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "  ALIVE OVER A DAY   (sorted by memory)",
            ForeColor = Theme.Heading,
            Font = new Font("Consolas", 9.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.BackColor = Theme.Panel;
        _list.ForeColor = Theme.Body;
        _list.BorderStyle = BorderStyle.None;
        _list.Font = new Font("Consolas", 9f);
        _list.Columns.Add("Process", 150);
        _list.Columns.Add("PID", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Memory", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Age", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Launched by", 200);
        _list.SelectedIndexChanged += (_, _) => UpdateKillButton();

        _kill.Dock = DockStyle.Bottom;
        _kill.Height = 34;
        _kill.Text = "Select a process to end";
        _kill.Enabled = false;
        _kill.FlatStyle = FlatStyle.System;
        _kill.Click += OnKillClicked;

        _status.Dock = DockStyle.Bottom;
        _status.Height = 34;
        _status.ForeColor = Theme.Dim;
        _status.Font = new Font("Segoe UI", 8.5f);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = string.Empty;

        Controls.Add(_list);
        Controls.Add(_status);
        Controls.Add(_kill);
        Controls.Add(heading);
    }

    /// <summary>
    /// Refresh the rows, preserving the selection.
    /// </summary>
    /// <remarks>
    /// Rebuilt in place with BeginUpdate rather than cleared and re-added, because this runs once a
    /// second: a naive Clear() steals the selection every tick and the Kill button can never be
    /// reached. Selection is restored by PID, not by row index, since the sort order moves.
    /// </remarks>
    public void Update(IReadOnlyList<ProcessLoad> processes)
    {
        int? selectedPid = SelectedPid();

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (ProcessLoad p in processes)
            {
                var item = new ListViewItem(p.Name) { Tag = p };
                item.SubItems.Add(p.Id.ToString());
                item.SubItems.Add($"{p.MemoryMb:N0} MB");
                item.SubItems.Add(p.Age is { } age ? ReportRenderer.Age(age) : "-");
                item.SubItems.Add(OwnerFor(p.Id));

                if (!ProcessKiller.CanKill(p.Name).Allowed) item.ForeColor = Theme.Dim;
                _list.Items.Add(item);
                if (p.Id == selectedPid) item.Selected = true;
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        UpdateKillButton();
    }

    private string OwnerFor(int pid)
    {
        string? label = _ancestry.OwnerLabelFor(pid);
        if (label is null) return "-";

        // The panel has a column, not a paragraph: keep the owner, drop the trail in brackets.
        int bracket = label.IndexOf('[');
        return (bracket > 0 ? label[..bracket] : label).Replace("launched by ", "").Trim();
    }

    private int? SelectedPid() =>
        _list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is ProcessLoad p ? p.Id : null;

    private ProcessLoad? Selected() =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ProcessLoad : null;

    private void UpdateKillButton()
    {
        ProcessLoad? selected = Selected();
        if (selected is null)
        {
            _kill.Enabled = false;
            _kill.Text = "Select a process to end";
            return;
        }

        var (allowed, _) = ProcessKiller.CanKill(selected.Name);
        _kill.Enabled = allowed;
        _kill.Text = allowed
            ? $"End {selected.Name} (pid {selected.Id})"
            : $"{selected.Name} is protected - cannot be ended";
    }

    private void OnKillClicked(object? sender, EventArgs e)
    {
        if (Selected() is not { } target) return;

        var (allowed, warning) = ProcessKiller.CanKill(target.Name);
        if (!allowed) return;

        string owner = OwnerFor(target.Id);
        string body = $"End {target.Name} (pid {target.Id})?\n\n"
                    + $"Running for {(target.Age is { } a ? ReportRenderer.Age(a) : "unknown")}, "
                    + $"holding {target.MemoryMb:N0} MB.\n"
                    + $"Launched by: {owner}\n\n"
                    + (warning is null ? "" : warning + "\n\n")
                    + "This ends it immediately, with no chance to save.";

        if (MessageBox.Show(body, "End process", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        var (killed, message) = ProcessKiller.Kill(target.Id, target.Name);
        _status.ForeColor = killed ? Theme.Low : Theme.High;
        _status.Text = message;
    }
}
