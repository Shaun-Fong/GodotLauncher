using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GodotLauncher.Core.Tests;

internal sealed record TestRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers)
{
    public bool TryGetHeader(string name, out string value)
    {
        if (Headers.TryGetValue(name, out var found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

internal sealed record TestResponse(int StatusCode, byte[] Body, IReadOnlyDictionary<string, string>? Headers = null)
{
    public static TestResponse Text(int statusCode, string text) =>
        new(statusCode, Encoding.UTF8.GetBytes(text));

    public static TestResponse FromBody(int statusCode, byte[] body, string contentType = "application/octet-stream") =>
        new(statusCode, body, new Dictionary<string, string> { ["Content-Type"] = contentType });
}

/// <summary>
/// Tiny HTTP/1.1 server over <see cref="TcpListener"/> used to exercise the
/// downloader without depending on the network or on HttpListener privileges.
/// </summary>
internal sealed class TestHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<TestRequest, int, Task<TestResponse?>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private int _requestCount;

    public TestHttpServer(Func<TestRequest, Task<TestResponse?>> handler)
        : this((request, _) => handler(request))
    {
    }

    public TestHttpServer(Func<TestRequest, int, Task<TestResponse?>> handler)
    {
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public int RequestCount => Volatile.Read(ref _requestCount);

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                if (request is null)
                {
                    return;
                }

                var index = Interlocked.Increment(ref _requestCount) - 1;
                var response = await _handler(request, index).ConfigureAwait(false);
                if (response is null)
                {
                    // Simulate a dropped connection.
                    return;
                }

                await WriteResponseAsync(stream, response).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static async Task<TestRequest?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new List<byte>(1024);
        var single = new byte[1];
        while (buffer.Count < 64 * 1024)
        {
            var read = await stream.ReadAsync(single).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            buffer.Add(single[0]);
            var count = buffer.Count;
            if (count >= 4 &&
                buffer[count - 4] == (byte)'\r' &&
                buffer[count - 3] == (byte)'\n' &&
                buffer[count - 2] == (byte)'\r' &&
                buffer[count - 1] == (byte)'\n')
            {
                break;
            }
        }

        var text = Encoding.ASCII.GetString(buffer.ToArray());
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        var parts = lines[0].Split(' ');
        if (parts.Length < 2)
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < lines.Length; index++)
        {
            var separator = lines[index].IndexOf(':');
            if (separator > 0)
            {
                headers[lines[index][..separator].Trim()] = lines[index][(separator + 1)..].Trim();
            }
        }

        return new TestRequest(parts[0], parts[1], headers);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, TestResponse response)
    {
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(Reason(response.StatusCode)).Append("\r\n");
        if (response.Headers is null || !response.Headers.ContainsKey("Content-Length"))
        {
            builder.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
        }

        builder.Append("Connection: close\r\n");

        if (response.Headers is not null)
        {
            foreach (var (name, value) in response.Headers)
            {
                builder.Append(name).Append(": ").Append(value).Append("\r\n");
            }
        }

        builder.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString())).ConfigureAwait(false);
        await stream.WriteAsync(response.Body).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        206 => "Partial Content",
        304 => "Not Modified",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "Response",
    };
}
