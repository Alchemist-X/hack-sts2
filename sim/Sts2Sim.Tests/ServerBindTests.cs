using System.Net;
using System.Net.Sockets;
using Sts2Sim.Server;

namespace Sts2Sim.Tests;

// --bind / --port parsing, HttpListener prefixes, and a live listener on the all-interfaces wildcard.
public static class ServerBindTests
{
    private static void Throws(Action a, string what)
    {
        try
        {
            a();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return;
        }
        throw new InvalidOperationException($"expected a parse error: {what}");
    }

    public static void Test_Server_BindArgParsing()
    {
        var d = Program.ParseArgs(Array.Empty<string>());
        Assert.Equal("127.0.0.1", d.Bind, "default bind is loopback");
        Assert.Equal(8765, d.Port);
        Assert.Equal("tmp/play-crashes.jsonl", d.CrashLog);

        var o = Program.ParseArgs(new[] { "--bind", "0.0.0.0", "--port", "9001", "--no-crash-log", "--reveal-moves" });
        Assert.Equal("0.0.0.0", o.Bind);
        Assert.Equal(9001, o.Port);
        Assert.True(o.CrashLog == null, "--no-crash-log");
        Assert.True(o.RevealMoves, "--reveal-moves");

        Assert.Equal("::1", Program.ParseArgs(new[] { "--bind", "::1" }).Bind);
        Assert.Equal("[::]", Program.ParseArgs(new[] { "--bind", "[::]" }).Bind);
        Assert.Equal("10.1.2.3", Program.ParseArgs(new[] { "--bind", " 10.1.2.3 " }).Bind, "trimmed");
        Assert.Equal("sim-server", Program.ParseArgs(new[] { "--bind", "sim-server" }).Bind, "host name");
        Assert.True(Program.ParseArgs(new[] { "--help" }).Help);

        Throws(() => Program.ParseArgs(new[] { "--bind" }), "--bind without a value");
        Throws(() => Program.ParseArgs(new[] { "--bind", "" }), "empty bind");
        Throws(() => Program.ParseArgs(new[] { "--bind", "not an address!" }), "garbage bind");
        Throws(() => Program.ParseArgs(new[] { "--port", "0" }), "port 0");
        Throws(() => Program.ParseArgs(new[] { "--port", "70000" }), "port 70000");
        Throws(() => Program.ParseArgs(new[] { "--port", "x" }), "port x");
        Throws(() => Program.ParseArgs(new[] { "--frobnicate" }), "unknown flag");
        Assert.Equal(2, Program.Main(new[] { "--bind", "bad bind" }), "Main exits 2 on a bad --bind");
    }

    public static void Test_Server_BindPrefixes()
    {
        Assert.Equal("http://127.0.0.1:8765/|http://localhost:8765/", string.Join("|", HttpHost.Prefixes("127.0.0.1", 8765)));
        foreach (var any in new[] { "0.0.0.0", "::", "[::]", "*", "+" })
        {
            Assert.True(HttpHost.IsAnyAddress(any), any);
            Assert.Equal("http://+:8770/", string.Join("|", HttpHost.Prefixes(any, 8770)), any);
        }
        Assert.Equal("http://10.0.0.5:8765/", string.Join("|", HttpHost.Prefixes("10.0.0.5", 8765)));
        Assert.Equal("http://[::1]:8765/", string.Join("|", HttpHost.Prefixes("::1", 8765)));
        Assert.Equal("http://[::1]:8765/", string.Join("|", HttpHost.Prefixes("[::1]", 8765)));
        Assert.Equal("http://sim-server:8765/", string.Join("|", HttpHost.Prefixes("sim-server", 8765)));
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // The container entrypoint binds 0.0.0.0: the '+' prefix must answer a request whose Host header is
    // 127.0.0.1 (as a published port forwards it) and stop cleanly on cancellation.
    public static void Test_Server_BindAnyServesHealth()
    {
        var service = new PlayService(new PlayServiceOptions { Version = "bind-test", CrashLogPath = null });
        int port = FreePort();
        using var cts = new CancellationTokenSource();
        using var ready = new ManualResetEventSlim(false);
        int exit = -1;
        var thread = new Thread(() => exit = HttpHost.Serve(service, port, "0.0.0.0", cts.Token, ready.Set)) { IsBackground = true };
        thread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)) || !thread.IsAlive, "listener started");
        Assert.True(thread.IsAlive, $"listener exited early with {exit}");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var r = http.GetAsync($"http://127.0.0.1:{port}/health").GetAwaiter().GetResult();
            string body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Equal(200, (int)r.StatusCode, body);
            Assert.True(body.Contains("\"bind-test\"", StringComparison.Ordinal), body);
            var r2 = http.GetAsync($"http://localhost:{port}/health").GetAwaiter().GetResult();
            Assert.Equal(200, (int)r2.StatusCode, "Host: localhost also matches the wildcard");
        }
        finally
        {
            cts.Cancel();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "listener stops on cancellation");
        }
        Assert.Equal(0, exit, "clean stop exit code");
    }
}
