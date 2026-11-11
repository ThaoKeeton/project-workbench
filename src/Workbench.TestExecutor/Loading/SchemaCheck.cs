using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Workbench.TestExecutor;

internal static class SchemaCheck
{
    public static bool Matches(JsonSchema schema, JsonNode node)
        => schema.Evaluate(JsonSerializer.SerializeToElement(node)).IsValid;
}
