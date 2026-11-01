namespace Sts2Sim.Tests;

public static class W1Pending
{
    public static int Count;

    public static void Run(string test, string dependsOnArea, Action body) => Run(test, dependsOnArea, body, Console.Out);

    internal static void Run(string test, string dependsOnArea, Action body, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(output);
        try
        {
            body();
        }
        catch (NotImplementedException ex)
        {
            Count++;
            output.WriteLine($"PENDING {test} ({dependsOnArea}): {ex.Message}");
        }
    }
}

public static class W1PendingTests
{
    public static void Test_W1Pending_CountsNotImplementedAndPropagatesOtherExceptions()
    {
        int before = W1Pending.Count;
        var output = new StringWriter();
        try
        {
            bool ran = false;
            W1Pending.Run("self-test pass", "none", () => ran = true, output);
            Assert.True(ran, "the body runs");
            Assert.Equal(before, W1Pending.Count, "a body that completes is not pending");
            Assert.Equal(string.Empty, output.ToString(), "a body that completes prints nothing");

            W1Pending.Run("self-test stub", "some area", () => throw new NotImplementedException("stub message"), output);
            Assert.Equal(before + 1, W1Pending.Count, "a NotImplementedException counts as pending");
            Assert.Equal("PENDING self-test stub (some area): stub message" + Environment.NewLine, output.ToString());

            var thrown = Assert.Throws<InvalidOperationException>(() => W1Pending.Run("self-test failure", "some area", () => throw new InvalidOperationException("real failure"), output));
            Assert.Equal("real failure", thrown.Message);
            Assert.Equal(before + 1, W1Pending.Count, "any other exception propagates and is not counted");
            Assert.Equal("PENDING self-test stub (some area): stub message" + Environment.NewLine, output.ToString(), "a propagated exception prints no PENDING line");
        }
        finally
        {
            W1Pending.Count = before;
        }
    }
}
