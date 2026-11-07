using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Workbench.Sdk;
using Workbench.Testing.Domain;

namespace Workbench.Testing;

internal sealed partial class TestingPage
{
    private void Changed(TestingControllerSnapshot snapshot) => _ = NotifyAsync();
    internal Task NotifyAsync(bool reopening = false) => _plugin.Owner.InternalAsync(async () =>
    {
        try { await DispatchAsync(() => { if (_active) Render(reopening); return Task.CompletedTask; }); }
        catch (Exception) { /* A retired/unavailable dispatcher cannot mutate a later page generation. */ }
    });
    internal Task RetireAsync() => _plugin.Owner.InternalAsync(() => DispatchAsync(() => { Retire(); return Task.CompletedTask; }));
    private void Retire()
    {
        if (!_active) return;
        _active = false; _generation++; Controller.Changed -= Changed; _plugin.Detach(this);
    }
    private Task DispatchAsync(Func<Task> action)
    {
        if (DispatcherQueue.HasThreadAccess) return action();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { await action(); done.TrySetResult(); }
            catch (Exception error) { done.TrySetException(error); }
        })) done.TrySetException(new InvalidOperationException("Native dispatcher unavailable."));
        return done.Task;
    }
    private void Render(bool reopening = false)
    {
        if (!_active) return;
        var snapshot = Controller.Snapshot; // Always latest; a queued old Changed value must never restore stale text.
        _rendering = true;
        try
        {
            var sourceIds = _sources.SelectedItems.OfType<ListViewItem>().Select(i => (string)i.Tag).ToHashSet(StringComparer.Ordinal);
            _sources.Items.Clear();
            foreach (var source in snapshot.Sources)
            {
                var item = new ListViewItem { Content = $"{source.Kind}: {source.Title} · {source.ItemCount} 项 · {source.Id}", Tag = source.Id };
                _sources.Items.Add(item); if (sourceIds.Contains(source.Id)) _sources.SelectedItems.Add(item);
            }
            _sets.Items.Clear();
            foreach (var set in snapshot.CaseSets)
            {
                var item = new ComboBoxItem { Content = $"{set.Name} · {set.CaseCount} 用例", Tag = set.Id };
                _sets.Items.Add(item); if (set.Id == snapshot.Draft?.Id) _sets.SelectedItem = item;
            }
            var draft = snapshot.Draft;
            if (_setId != draft?.Id) { _setId = draft?.Id; _caseId = null; _stepId = null; }
            _status.Text = draft is null ? "尚未选择用例集" : $"{(draft.IsDirty ? "有未保存编辑" : "已自动保存")} · 输入版本 {draft.Version} / 已保存 {draft.SavedVersion} · {(draft.IsSaving ? "正在写入" : draft.ErrorCode ?? snapshot.ErrorCode ?? "就绪")}";
            Set(_setName, draft?.Content.Name ?? "");
            _cases.Items.Clear();
            var cases = draft?.Content.Cases ?? [];
            var selected = cases.FirstOrDefault(c => c.Id == _caseId) ?? cases.FirstOrDefault();
            _caseId = selected?.Id;
            foreach (var c in cases)
            {
                var item = new ListViewItem { Content = $"{c.Title} · {(c.Steps.All(s => s.Action is not null) ? "虚拟映射" : "含手动步骤")}", Tag = c.Id };
                _cases.Items.Add(item); if (c.Id == _caseId) _cases.SelectedItem = item;
            }
            Set(_title, selected?.Title ?? ""); Set(_prerequisites, selected?.Prerequisites ?? "");
            var step = selected?.Steps.FirstOrDefault(s => s.Id == _stepId) ?? selected?.Steps.FirstOrDefault();
            _stepId = step?.Id; _steps.Items.Clear();
            if (selected is not null) foreach (var s in selected.Steps)
            {
                var item = new ComboBoxItem { Content = $"{selected.Steps.ToList().IndexOf(s) + 1}. {s.Instruction}", Tag = s.Id };
                _steps.Items.Add(item); if (s.Id == _stepId) _steps.SelectedItem = item;
            }
            Set(_instruction, step?.Instruction ?? ""); Set(_expected, step?.Expected ?? "");
            _mapping.Text = step is null ? "" : (step.Action is null ? "手动：不会自动通过" : $"SIMULATED {step.Action.Kind} {step.Action.ElementId} 值：{step.Action.Value}")
                + "\n" + string.Join("\n", step.Traces.Select(t => $"来源 {t.SnapshotId}@{t.SnapshotRevision} · 定位 {t.Locator} · 条件 {t.CriterionIndex}"));
            var admission = _plugin.Owner.AdmitsUsers || reopening;
            var editable = admission && !_busy && !snapshot.IsAuthoringBusy && !snapshot.IsPreparing && !snapshot.IsStopped;
            foreach (var editor in new[] { _setName, _title, _prerequisites, _instruction, _expected }) editor.IsEnabled = editable && draft is not null;
            foreach (var button in _authorActions) button.IsEnabled = editable;
            _sets.IsEnabled = _cases.IsEnabled = _steps.IsEnabled = _sources.IsEnabled = editable;
            _run.IsEnabled = editable && selected is not null && selected.Steps.All(s => s.Action is not null) && _plugin.CanStart(snapshot);
            _cancel.IsEnabled = admission && snapshot.OwnsRun && !snapshot.IsStopped;
            _retryDraft.Visibility = draft is { IsDirty: true, ErrorCode: not null } ? Visibility.Visible : Visibility.Collapsed;
            _preserveDraft.Visibility = draft is { IsDirty: true, ErrorCode: "RevisionConflict" } ? Visibility.Visible : Visibility.Collapsed;
            _retryRun.IsEnabled = editable && _plugin.CanStart(snapshot) && snapshot.SelectedRun?.State is TestRunState.Failed or TestRunState.Cancelled or TestRunState.Interrupted;
            _runs.Items.Clear();
            foreach (var run in snapshot.Runs)
            {
                var item = new ComboBoxItem { Content = $"{run.State} · {run.Id} · 父 {run.ParentRunId ?? "无"}", Tag = run.Id };
                _runs.Items.Add(item); if (run.Id == snapshot.SelectedRun?.Plan.Id) _runs.SelectedItem = item;
            }
            var current = snapshot.SelectedRun;
            _runDetails.Text = current is null ? "尚未选择执行" : $"SIMULATED {current.State} · run {current.Plan.Id} · 序列 {current.Sequence} · session {current.Plan.SessionId} · 冻结版本 {current.Plan.CaseRevision}\nhead {current.HeadHash}\n错误 {snapshot.ErrorCode ?? "无"}";
            var eventSequence = (_events.SelectedItem as ComboBoxItem)?.Tag as long?;
            if (_runId != current?.Plan.Id) { _runId = current?.Plan.Id; eventSequence = null; _eventKey = null; _frame.Source = null; }
            _events.Items.Clear();
            if (current is not null) foreach (var e in current.Events)
            {
                var item = new ComboBoxItem { Content = $"{e.Sequence}: {e.Kind} · {e.Outcome} · {e.ErrorCode}", Tag = e.Sequence };
                _events.Items.Add(item); if (e.Sequence == eventSequence) _events.SelectedItem = item;
            }
            if (_events.SelectedItem is null && current is not null)
                _events.SelectedItem = _events.Items.OfType<ComboBoxItem>().LastOrDefault(i => current.Events.Single(e => e.Sequence == (long)i.Tag).Kind == TestRunEventKind.StepFinished)
                    ?? _events.Items.LastOrDefault();
        }
        finally { _rendering = false; }
        SelectEvidence();
    }
    private static void Set(TextBox editor, string value) { if (editor.Text != value) editor.Text = value; }
    private void SelectEvidence()
    {
        var run = Controller.Snapshot.SelectedRun;
        if (run is null || _events.SelectedItem is not ComboBoxItem { Tag: long sequence }) return;
        var record = run.Events.Single(e => e.Sequence == sequence);
        _observed.Text = record.NativePlainTextState ?? $"{record.Kind} · {record.ErrorCode ?? ""}";
        var key = run.Plan.Id + "/" + record.Sequence;
        if (_eventKey == key) return;
        _eventKey = key;
        _frame.Source = null;
        _frameStatus.Text = "SIMULATED — 正在读取该事件的真实证据";
        if (record.EvidenceIds.Count == 0) { _frame.Source = null; _frameStatus.Text = "SIMULATED — 此事件没有证据"; return; }
        var generation = _generation;
        _ = _plugin.Owner.InternalAsync(async () =>
        {
            try
            {
                var evidence = await Controller.ReadEvidenceAsync(run.Plan.Id, record.EvidenceIds[0]).ConfigureAwait(false);
                await DispatchAsync(async () =>
                {
                    if (!_active || generation != _generation || _eventKey != key) return;
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    { writer.WriteBytes(evidence.Bytes); await writer.StoreAsync(); await writer.FlushAsync(); }
                    stream.Seek(0);
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    if (!_active || generation != _generation || _eventKey != key) return;
                    _frame.Source = bitmap;
                    _frameStatus.Text = $"SIMULATED · 已解码 {bitmap.PixelWidth}×{bitmap.PixelHeight} · {evidence.Receipt.Length} 字节 · SHA256 {evidence.Receipt.Sha256}";
                }).ConfigureAwait(false);
            }
            catch (Exception error) { await DispatchAsync(() => { if (_active && generation == _generation) ShowError(error); return Task.CompletedTask; }); }
        });
    }
}
