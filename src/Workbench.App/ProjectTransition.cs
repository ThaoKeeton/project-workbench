namespace Workbench.App;

internal sealed record ProjectSwitchResult(bool Succeeded, bool Pending, string? ErrorCode);
internal enum ProjectTransitionState { Idle, Preparing, StoppingCurrent, StartingTarget, PersistingSelection, Recovering, Blocked }
internal sealed record ProjectTransitionSnapshot(ProjectTransitionState State, string? CurrentProjectId, string? TargetProjectId, string? ErrorCode);
