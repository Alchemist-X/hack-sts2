using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

// Read-only compatibility check. Never initializes the mod/game, creates a Godot
// object, applies a Harmony patch, or writes to the install or profile directories.
if (args.Length != 2)
    throw new ArgumentException("usage: RecorderContractCheck <Sts2Recorder.dll> <game-assembly-directory>");
string modPath = Path.GetFullPath(args[0]);
string gameDir = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(gameDir, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(modPath);
var hooks = mod.GetType("Sts2Recorder.Game.DecisionHooks", throwOnError: true)!;
const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
var plan = (IEnumerable)hooks.GetMethod("GetPatchPlan", Flags)!.Invoke(null, null)!;
int targetCount = 0;
var failures = new List<string>();
foreach (object entry in plan)
{
    var row = entry.GetType();
    var type = (Type)row.GetProperty("Type")!.GetValue(entry)!;
    var name = (string)row.GetProperty("Method")!.GetValue(entry)!;
    var originals = type.GetMethods(Flags).Where(m => m.Name == name && !m.IsAbstract).ToArray();
    if (originals.Length != 1)
    {
        failures.Add($"{type.Name}.{name}: expected one concrete target, found {originals.Length}");
        continue;
    }
    targetCount++;
    Type? stateType = null;
    if (row.GetProperty("Prefix")!.GetValue(entry) is string beforeName)
    {
        var state = hooks.GetMethod(beforeName, Flags)!.GetParameters().FirstOrDefault(p => p.Name == "__state");
        if (state != null)
        {
            if (!state.IsOut) failures.Add($"{type.Name}.{name}: prefix __state must be out");
            stateType = state.ParameterType.GetElementType();
        }
    }
    foreach (string side in new[] { "Prefix", "Postfix", "Finalizer" })
    {
        if (row.GetProperty(side)!.GetValue(entry) is not string patchName) continue;
        var patch = hooks.GetMethod(patchName, Flags) ?? throw new Exception("missing patch: " + patchName);
        foreach (var parameter in patch.GetParameters())
        {
            Type? supplied = parameter.Name switch
            {
                "__instance" => type,
                "__result" => originals[0].ReturnType,
                "__state" => stateType,
                _ => originals[0].GetParameters().FirstOrDefault(p => p.Name == parameter.Name)?.ParameterType,
            };
            var expected = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
            if (supplied == null || !expected.IsAssignableFrom(supplied))
                failures.Add($"{type.Name}.{name} -> {patchName}: incompatible injected parameter {parameter.Name}");
        }
    }
}

var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDir, "sts2.dll"));
var funnels = mod.GetType("Sts2Recorder.Game.HumanFunnels", throwOnError: true)!;
foreach (var (typeName, method, parameterCount, prefix, postfix) in new[]
{
    ("MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry", "OnTryPurchaseWrapper", 2, "MerchantPurchasePrefix", "MerchantPurchasePostfix"),
    ("MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardRemovalEntry", "OnTryPurchaseWrapper", 3, "MerchantPurchasePrefix", "CardRemovalPurchasePostfix"),
    ("MegaCrit.Sts2.Core.Rewards.Reward", "SelectUnsynchronized", 0, "RewardSelectPrefix", "RewardSelectPostfix"),
})
{
    var type = game.GetType(typeName, throwOnError: true)!;
    var original = type.GetMethods(Flags).Single(m => m.Name == method && m.GetParameters().Length == parameterCount);
    var before = funnels.GetMethod(prefix, Flags)!;
    var after = funnels.GetMethod(postfix, Flags)!;
    var beforeState = before.GetParameters().Single(p => p.Name == "__state");
    var afterState = after.GetParameters().Single(p => p.Name == "__state");
    if (!beforeState.IsOut || beforeState.ParameterType.GetElementType() != afterState.ParameterType)
        failures.Add($"{method}: mismatched Harmony __state carrier");
    foreach (var patch in new[] { before, after })
        foreach (var parameter in patch.GetParameters().Where(p => p.Name != "__state"))
        {
            Type? supplied = parameter.Name switch
            {
                "__instance" => type,
                "__result" => original.ReturnType,
                _ => original.GetParameters().FirstOrDefault(p => p.Name == parameter.Name)?.ParameterType,
            };
            if (supplied == null || !parameter.ParameterType.IsAssignableFrom(supplied))
                failures.Add($"{type.Name}.{method}: incompatible {patch.Name} parameter {parameter.Name}");
        }
    targetCount++;
}
int fieldCount = 0;
foreach (var (shortName, fields) in new[]
{
    ("NCardRewardSelectionScreen", new[] { "_options", "_extraOptions" }),
    ("NChooseACardSelectionScreen", new[] { "_cards" }),
    ("NChooseABundleSelectionScreen", new[] { "_bundles" }),
    ("NChooseARelicSelection", new[] { "_relics" }),
    ("NDeckUpgradeSelectScreen", new[] { "_cards", "_prefs", "_selectedCards" }),
    ("NDeckTransformSelectScreen", new[] { "_cards", "_prefs", "_selectedCards" }),
    ("NDeckCardSelectScreen", new[] { "_cards", "_prefs", "_selectedCards" }),
    ("NDeckEnchantSelectScreen", new[] { "_cards", "_prefs", "_selectedCards" }),
    ("NSimpleCardSelectScreen", new[] { "_cards", "_prefs", "_selectedCards" }),
    ("NCombatPileCardSelectScreen", new[] { "_grid", "_prefs", "_selectedCards" }),
    ("NCardGrid", new[] { "_cards" }),
    ("NPlayerHand", new[] { "_prefs", "_selectedCards" }),
})
{
    string ns = shortName switch
    {
        "NPlayerHand" => "MegaCrit.Sts2.Core.Nodes.Combat.",
        "NCardGrid" => "MegaCrit.Sts2.Core.Nodes.Cards.",
        "NChooseARelicSelection" => "MegaCrit.Sts2.Core.Nodes.Screens.",
        _ => "MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.",
    };
    var type = game.GetType(ns + shortName, throwOnError: true)!;
    foreach (var field in fields)
    {
        FieldInfo? found = null;
        for (var current = type; current != null && found == null; current = current.BaseType)
            found = current.GetField(field, Flags | BindingFlags.DeclaredOnly);
        if (found == null) failures.Add($"missing field {shortName}.{field}");
        else fieldCount++;
    }
}
if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
Console.WriteLine($"PASS: {targetCount} decision-hook targets/signatures and {fieldCount} candidate fields resolve. No game runtime invoked.");
