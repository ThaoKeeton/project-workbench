using Workbench.Sdk;
using Workbench.TestDependency;

namespace Workbench.TestPlugin;

public sealed class WorkspaceLifecyclePlugin : IWorkbenchPlugin
{
    private WorkspaceLifecycleGate _gate = null!;
    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _gate = WorkspaceLifecycleGate.For(context.DataDirectory);
        _gate.Start();
        ((IPluginProjectContext)context).RegisterBeforeProjectLeave(async _ =>
        {
            _gate.Events.Enqueue("prepare");
            _gate.Prepared.TrySetResult();
            await _gate.PrepareRelease.Task;
            if (_gate.FailPreparation) throw new HostileException();
        });
        await _gate.InitializeRelease.Task;
        if (_gate.FailInitialization) throw new HostileException();
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _gate.Events.Enqueue("stop");
        _gate.StopStarted.TrySetResult();
        await _gate.StopRelease.Task;
        _gate.Retire();
    }
    private sealed class HostileException : Exception
    {
        public override string Message => throw new InvalidOperationException("synthetic-never-output");
        public override string ToString() => throw new InvalidOperationException("synthetic-never-output");
        public override Exception GetBaseException() => throw new InvalidOperationException("synthetic-never-output");
    }
}
