namespace Workbench.TestExecutor;

/// <summary>
/// Local stand-in used by tests. It returns one legal script and never opens a socket.
/// </summary>
public sealed class FixedScriptModel : IScriptModel
{
    public const string Script = """
        {"schema":"workbench.test-executor.authored-script.v1","schemaVersion":1,"id":"CASE-AUTHORED","name":"切削后体积减小","steps":[{"id":"wait-start","type":"web","action":"wait-for","target":{"text":"开始"},"allowFallback":false},{"id":"start","type":"web","action":"click","target":{"text":"开始"},"expected":"已开始","allowFallback":false},{"id":"status","type":"web","action":"assert-text","target":{"selector":"#status"},"expected":"已开始","allowFallback":false},{"id":"snap-cut","type":"scene3d","action":"snapshot","target":{"selector":"#view"},"allowFallback":false},{"id":"before-cut","type":"scene3d","action":"assert-scene","target":{"selector":"#view","object":"stock"},"expected":"mesh.stock.volume=8±0.2;mesh.sphere.vertexCount=117","allowFallback":false},{"id":"cut-drag","type":"scene3d","action":"drag","target":{"selector":"#view","object":"stock"},"input":"world-path:0,2,0;0.4,2,0","expected":"mesh.stock.volume=4±0.3;mesh.sphere.vertexCount=117","allowFallback":false},{"id":"cut-diff","type":"scene3d","action":"diff","target":{"selector":"#view"},"expected":"counts.deformed=1±0;counts.moved=0±0","allowFallback":false}]}
        """;

    public int Calls { get; private set; }

    public string Name => "fixed-local";

    public bool Billed => false;

    public Task<ScriptModelReply> CompleteAsync(ScriptModelCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(new ScriptModelReply(Script, 0, 0));
    }
}
