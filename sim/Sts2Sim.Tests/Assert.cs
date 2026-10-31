namespace Sts2Sim.Tests;

public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message)
    {
    }
}

public static class Assert
{
    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            Fail($"Expected <{Format(expected)}> but was <{Format(actual)}>", message);
        }
    }

    public static void NotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
        {
            Fail($"Did not expect <{Format(actual)}>", message);
        }
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null)
    {
        var e = expected.ToList();
        var a = actual.ToList();
        if (e.Count != a.Count)
        {
            Fail($"Expected {e.Count} items but was {a.Count}: [{string.Join(", ", a.Select(Format))}]", message);
        }
        for (int i = 0; i < e.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(e[i], a[i]))
            {
                Fail($"Mismatch at index {i}: expected <{Format(e[i])}> but was <{Format(a[i])}>", message);
            }
        }
    }

    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            Fail("Expected true but was false", message);
        }
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
        {
            Fail("Expected false but was true", message);
        }
    }

    public static void Null(object? value, string? message = null)
    {
        if (value != null)
        {
            Fail($"Expected null but was <{Format(value)}>", message);
        }
    }

    public static T NotNull<T>(T? value, string? message = null) where T : class
    {
        if (value == null)
        {
            Fail("Expected non-null", message);
        }
        return value!;
    }

    public static TException Throws<TException>(Action action, string? message = null) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            Fail($"Expected {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}", message);
        }
        Fail($"Expected {typeof(TException).Name} but nothing was thrown", message);
        return null!;
    }

    public static void Fail(string reason, string? message = null)
    {
        throw new AssertionException(message == null ? reason : $"{message}: {reason}");
    }

    private static string Format<T>(T value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        ulong u => $"{u} (0x{u:X16})",
        _ => value.ToString() ?? "null"
    };
}
