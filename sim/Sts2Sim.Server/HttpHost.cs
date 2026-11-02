using System.Net;
using System.Text;

namespace Sts2Sim.Server;

// Thin System.Net.HttpListener front end over PlayService. Loopback only by default; --bind widens it
// (containers use 0.0.0.0).
public static class HttpHost
{
    public const string DefaultBind = "127.0.0.1";

    private static readonly HashSet<string> AnyAddress = new(StringComparer.Ordinal) { "0.0.0.0", "::", "[::]", "*", "+" };

    // True when the bind address means "every interface".
    public static bool IsAnyAddress(string bind) => AnyAddress.Contains(bind.Trim());

    // Normalises and checks a --bind value; throws ArgumentException on an unusable one.
    public static string ValidateBind(string bind)
    {
        string b = bind.Trim();
        if (b.Length == 0)
        {
            throw new ArgumentException("--bind needs a non-empty address");
        }
        if (IsAnyAddress(b))
        {
            return b;
        }
        string bare = b.StartsWith('[') && b.EndsWith(']') ? b[1..^1] : b;
        if (IPAddress.TryParse(bare, out _))
        {
            return b;
        }
        if (Uri.CheckHostName(b) == UriHostNameType.Dns)
        {
            return b;
        }
        throw new ArgumentException($"--bind '{bind}' is not an IP address, host name, 0.0.0.0, ::, * or +");
    }

    // HttpListener prefixes for a bind address. "Any" maps to the '+' wildcard (it accepts every Host header;
    // a literal 0.0.0.0 prefix would only match requests whose Host header is 0.0.0.0).
    public static IReadOnlyList<string> Prefixes(string bind, int port)
    {
        string b = ValidateBind(bind);
        if (IsAnyAddress(b))
        {
            return new[] { $"http://+:{port}/" };
        }
        if (b == DefaultBind)
        {
            return new[] { $"http://127.0.0.1:{port}/", $"http://localhost:{port}/" };
        }
        string bare = b.StartsWith('[') && b.EndsWith(']') ? b[1..^1] : b;
        if (IPAddress.TryParse(bare, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return new[] { $"http://[{bare}]:{port}/" };
        }
        return new[] { $"http://{b}:{port}/" };
    }

    public static int Serve(PlayService service, int port, string bind = DefaultBind)
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        // docker stop sends SIGTERM: stop the listener and exit 0 instead of being killed.
        using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM,
            ctx =>
            {
                ctx.Cancel = true;
                stop.Cancel();
            });
        return Serve(service, port, bind, stop.Token);
    }

    // Serves until the token is cancelled (tests use this overload). Returns 0 after a clean stop, 1 when the
    // listener cannot start.
    public static int Serve(PlayService service, int port, string bind, CancellationToken token, Action? started = null)
    {
        using var listener = new HttpListener();
        foreach (var prefix in Prefixes(bind, port))
        {
            listener.Prefixes.Add(prefix);
        }
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.Error.WriteLine($"cannot listen on {bind}:{port}: {ex.Message}");
            return 1;
        }
        string shown = IsAnyAddress(bind) ? $"http://0.0.0.0:{port}/ (all interfaces)" : $"http://{bind}:{port}/";
        Console.WriteLine($"Sts2Sim.Server {service.Options.Version} listening on {shown} (crash log: {service.Options.CrashLogPath ?? "off"}; reveal moves by default: {(service.Options.RevealMoves ? "on" : "off")})");
        using var reg = token.Register(() =>
        {
            try
            {
                listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
        });
        started?.Invoke();
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = listener.GetContext();
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break; // includes ObjectDisposedException after Stop()
            }
            ThreadPool.QueueUserWorkItem(_ => HandleOne(service, ctx));
        }
        Console.WriteLine("stopped");
        return 0;
    }

    private static void HandleOne(PlayService service, HttpListenerContext ctx)
    {
        ServiceResponse response;
        try
        {
            string? body = null;
            if (ctx.Request.HasEntityBody)
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                body = reader.ReadToEnd();
            }
            response = service.Handle(ctx.Request.HttpMethod, ctx.Request.Url?.PathAndQuery ?? "/", body);
        }
        catch (Exception ex)
        {
            response = PlayService.Error(500, $"{ex.GetType().Name}: {ex.Message}");
        }
        try
        {
            var bytes = Encoding.UTF8.GetBytes(response.Json);
            ctx.Response.StatusCode = response.Status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"response write failed: {ex.Message}");
        }
    }
}
