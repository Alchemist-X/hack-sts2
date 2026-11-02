using System.Net;
using System.Text;

namespace Sts2Sim.Server;

// Thin System.Net.HttpListener front end over PlayService (127.0.0.1 only).
public static class HttpHost
{
    public static int Serve(PlayService service, int port)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Prefixes.Add($"http://localhost:{port}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.Error.WriteLine($"cannot listen on 127.0.0.1:{port}: {ex.Message}");
            return 1;
        }
        Console.WriteLine($"Sts2Sim.Server {service.Options.Version} listening on http://127.0.0.1:{port}/ (crash log: {service.Options.CrashLogPath ?? "off"})");
        using var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
            try
            {
                listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
        };
        while (!stop.IsSet)
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
                break;
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
