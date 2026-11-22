# Dynamic Tools

Aether supports **hot-reloadable tools** written in plain C#. Place any `.cs` file implementing `IDynamicTool` in this directory, and Aether will automatically compile and register it — no restart required.

## How it works

1. Drop a `.cs` file in `tools/` (or any subdirectory)
2. `DynamicToolWatcherService` detects the change
3. `DynamicToolCompiler` compiles it using Roslyn in-memory
4. All `IDynamicTool` implementations in the file are loaded into the tool registry
5. The LLM can call them like any built-in tool

## Writing a Dynamic Tool

```csharp
using Aether.Tooling.DynamicTool;

namespace MyTools;

public class MyTool : IDynamicTool
{
    public string Name => "my_tool";
    public string Description => "What my tool does";

    public string ParameterSchemaJson => @"{
        ""type"": ""object"",
        ""properties"": {
            ""input"": {
                ""type"": ""string"",
                ""description"": ""Input parameter""
            }
        },
        ""additionalProperties"": false
    }";

    public Task<string> ExecuteAsync(Dictionary<string, object> args)
    {
        var input = args.GetValueOrDefault("input")?.ToString() ?? "";
        return Task.FromResult($"Processed: {input}");
    }
}
```

## Sample Tools

- `sample/HelloWorldTool.cs` — minimal greeting tool
- `sample/CalculatorTool.cs` — basic arithmetic operations

## Notes

- Tools run in a collectible `AssemblyLoadContext` and can be updated by saving the file again.
- Compilation errors are logged but don't crash Aether — fix the file and save again.
- Tool execution has a 30-second timeout and 64KB output limit.
