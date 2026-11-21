namespace Workbench.TestExecutor;

public sealed record ComputerUseSession(string Id);

public sealed record ComputerUseStart(string Instruction);

public sealed record ComputerUseObservation(MaskedImage Screenshot, string Instruction);

public sealed record ComputerUseAction(string Kind, int? X, int? Y, string? Text, string? Key);

public sealed record ComputerUseTurn(IReadOnlyList<ComputerUseAction> Actions, string Summary);

public interface IComputerUseProvider
{
    string Name { get; }

    Task<ComputerUseSession> StartAsync(ComputerUseStart start, CancellationToken cancellationToken);

    Task<ComputerUseTurn> ObserveAsync(ComputerUseSession session, ComputerUseObservation observation, CancellationToken cancellationToken);

    Task EndAsync(ComputerUseSession session, CancellationToken cancellationToken);
}

public sealed class ComputerUseException : Exception
{
    public ComputerUseException(string code) : base(code) => Code = code;

    public string Code { get; }
}

public sealed class FakeComputerUseProvider : IComputerUseProvider
{
    private readonly Func<ComputerUseObservation, ComputerUseTurn>? _script;

    public FakeComputerUseProvider(Func<ComputerUseObservation, ComputerUseTurn>? script = null) => _script = script;

    public string Name => "fake";

    public int StartCalls { get; private set; }

    public int ObserveCalls { get; private set; }

    public int EndCalls { get; private set; }

    public byte[]? LastPng { get; private set; }

    public string? LastInstruction { get; private set; }

    public Task<ComputerUseSession> StartAsync(ComputerUseStart start, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCalls++;
        return Task.FromResult(new ComputerUseSession(Guid.NewGuid().ToString("N")));
    }

    public Task<ComputerUseTurn> ObserveAsync(ComputerUseSession session, ComputerUseObservation observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObserveCalls++;
        LastPng = observation.Screenshot.Png;
        LastInstruction = observation.Instruction;
        var turn = _script?.Invoke(observation) ?? new ComputerUseTurn([new ComputerUseAction("click", 1, 1, null, null)], "synthetic");
        return Task.FromResult(turn);
    }

    public Task EndAsync(ComputerUseSession session, CancellationToken cancellationToken)
    {
        EndCalls++;
        return Task.CompletedTask;
    }
}
