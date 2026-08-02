using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: McpHeadlessPatcher <input.dll> <output.dll>");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(input)!);
var gameData = Environment.GetEnvironmentVariable("STS2_GAME_DATA_DIR");
if (!string.IsNullOrWhiteSpace(gameData))
    resolver.AddSearchDirectory(gameData);
using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters
{
    ReadSymbols = false,
    InMemory = true,
    AssemblyResolver = resolver,
});
var type = assembly.MainModule.Types.SingleOrDefault(t => t.FullName == "STS2_MCP.McpMod")
    ?? throw new InvalidOperationException("STS2_MCP.McpMod was not found");
var method = type.Methods.SingleOrDefault(m => m.Name == "TryApplyHarmonyPatches")
    ?? throw new InvalidOperationException("TryApplyHarmonyPatches was not found");

// Harmony.PatchAll() scans every patch type while the Godot mod loader is still
// inside its initializer.  On --headless v0.107.1 this never returns, preventing
// the HTTP listener and all later mods from starting.  The patches only inject
// settings/fast-mode UI conveniences; the state and action APIs do not depend on
// them, so a headless-only build can safely make this method a no-op.
method.Body.ExceptionHandlers.Clear();
method.Body.Variables.Clear();
method.Body.Instructions.Clear();
method.Body.InitLocals = false;
method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

// A shared read-only runtime can host many game processes only if the listener
// port is process-local.  The upstream DLL reads STS2_MCP.conf beside itself,
// which forced every worker to own a private 2.2 GiB app bundle.  Replace that
// loader with an environment lookup in the sandbox-only DLL:
//
//   STS2_MCP_PORT=<worker port>  -> that port
//   variable absent             -> upstream default 15526
//
// The human-game DLL is never modified.  Int32.Parse intentionally rejects a
// malformed value at startup rather than silently binding the wrong worker.
var loadPort = type.Methods.SingleOrDefault(m => m.Name == "LoadPort")
    ?? throw new InvalidOperationException("LoadPort was not found");
if (loadPort.Parameters.Count != 0 || loadPort.ReturnType.FullName != "System.Int32")
    throw new InvalidOperationException($"unexpected LoadPort signature: {loadPort.FullName}");

var getEnvironmentVariable = assembly.MainModule.ImportReference(
    typeof(Environment).GetMethod(
        nameof(Environment.GetEnvironmentVariable),
        new[] { typeof(string) }
    ) ?? throw new InvalidOperationException("Environment.GetEnvironmentVariable(string) missing")
);
var parseInt32 = assembly.MainModule.ImportReference(
    typeof(int).GetMethod(nameof(int.Parse), new[] { typeof(string) })
    ?? throw new InvalidOperationException("Int32.Parse(string) missing")
);
loadPort.Body.ExceptionHandlers.Clear();
loadPort.Body.Variables.Clear();
loadPort.Body.Instructions.Clear();
loadPort.Body.InitLocals = false;
var parseEnvironmentPort = Instruction.Create(OpCodes.Call, parseInt32);
loadPort.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "STS2_MCP_PORT"));
loadPort.Body.Instructions.Add(Instruction.Create(OpCodes.Call, getEnvironmentVariable));
loadPort.Body.Instructions.Add(Instruction.Create(OpCodes.Dup));
loadPort.Body.Instructions.Add(Instruction.Create(OpCodes.Brtrue_S, parseEnvironmentPort));
loadPort.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
loadPort.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 15526));
loadPort.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
loadPort.Body.Instructions.Add(parseEnvironmentPort);
loadPort.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

assembly.Write(output, new WriterParameters { WriteSymbols = false });
Console.WriteLine(
    $"patched {input} -> {output}: disabled optional Harmony UI patches; "
    + "LoadPort now reads STS2_MCP_PORT"
);
return 0;
