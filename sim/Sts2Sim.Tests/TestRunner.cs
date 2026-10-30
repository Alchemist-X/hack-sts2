using System.Diagnostics;
using System.Reflection;

namespace Sts2Sim.Tests;

public static class TestRunner
{
    public static int Main(string[] args)
    {
        var filters = args.Where(a => !a.StartsWith('-')).ToArray();
        bool verbose = args.Contains("-v") || args.Contains("--verbose");

        var tests = typeof(TestRunner).Assembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.Name.StartsWith("Test_", StringComparison.Ordinal)
                        && m.GetParameters().Length == 0
                        && !m.ContainsGenericParameters)
            .Select(m => (Method: m, Name: $"{m.DeclaringType!.Name}.{m.Name}"))
            .Where(t => filters.Length == 0 || filters.Any(f => t.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        int passed = 0;
        var failures = new List<(string Name, string Message)>();
        var total = Stopwatch.StartNew();

        foreach (var (method, name) in tests)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = method.Invoke(null, null);
                if (result is Task task)
                {
                    task.GetAwaiter().GetResult();
                }
                sw.Stop();
                passed++;
                Console.WriteLine($"PASS {name} ({sw.Elapsed.TotalMilliseconds:F1} ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                var inner = ex is TargetInvocationException { InnerException: { } innerEx } ? innerEx : ex;
                var message = $"{inner.GetType().Name}: {inner.Message}";
                failures.Add((name, message));
                Console.WriteLine($"FAIL {name} ({sw.Elapsed.TotalMilliseconds:F1} ms)");
                Console.WriteLine($"     {message}");
                if (verbose && inner.StackTrace != null)
                {
                    Console.WriteLine(inner.StackTrace);
                }
            }
        }

        total.Stop();
        Console.WriteLine();
        Console.WriteLine($"Summary: {passed} passed, {failures.Count} failed, {tests.Count} total ({total.Elapsed.TotalMilliseconds:F0} ms)");
        if (failures.Count > 0)
        {
            Console.WriteLine("Failed tests:");
            foreach (var (name, message) in failures)
            {
                Console.WriteLine($"  {name}: {message}");
            }
        }
        if (tests.Count == 0)
        {
            Console.WriteLine("No tests matched.");
            return 2;
        }
        return failures.Count == 0 ? 0 : 1;
    }
}
