using System.Net;
using System.Text;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using RFPStorage.Internal;
using RFPStorage.Structures;
using Xunit;

namespace RFPStorage.UnitTests;

public class ReviewFixTests
{
    private sealed class CaptureLogger : ILogger
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> kv)
                foreach (var p in kv) Lines.Add(p.Key + "=" + p.Value);
        }
    }

    private static StorageConnection Conn(string url = "https://s3.example.test", string? presign = "", bool insecure = false) => new()
    {
        ServiceUrl = url, Region = "", AccessKey = "TESTKEYID", SecretKey = "test-secret-placeholder",
        Bucket = "my-bucket", ForcePathStyle = true, PresignEndpoint = presign!, AllowInsecureHttp = insecure,
    };

    private static Implementations.RFPStorageLib Lib(ILogger? l = null) => new(l ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

    // 1. contentType
    [Theory]
    [InlineData("application/pdf", true)]
    [InlineData("text/plain\r\nX-Evil: 1", false)]
    [InlineData("text/plain\n", false)]
    [InlineData("text/plain\u0000", false)]
    [InlineData("text/plain\u007f", false)]
    [InlineData("", false)]
    public void ContentTypeRows(string ct, bool ok)
    {
        if (ok) Assert.NotEmpty(Lib().PresignPut(Conn(), "dev/k.pdf", ct, 300).Url);
        else Assert.StartsWith("RFPStorage:", Assert.Throws<ArgumentException>(() => Lib().PresignPut(Conn(), "dev/k.pdf", ct, 300)).Message);
    }

    [Fact]
    public void ContentType255Ok256Rejected()
    {
        var ok = "a/" + new string('b', 253);
        Assert.NotEmpty(Lib().PresignPut(Conn(), "dev/k.pdf", ok, 300).Url);
        Assert.Throws<ArgumentException>(() => Lib().PresignPut(Conn(), "dev/k.pdf", ok + "c", 300));
    }

    // 2. AllowInsecureHttp
    [Fact]
    public void InsecureHttpAnyHostWhenFlagTrue()
    {
        var r = Lib().PresignPut(Conn("http://secure-gateway:8333", "http://devbox.test:8333", insecure: true), "dev/k.pdf", "text/plain", 300);
        Assert.StartsWith("http://devbox.test:8333/", r.Url);
        Assert.NotEmpty(Lib().PresignGet(Conn("http://secure-gateway:8333", insecure: true), "dev/k.pdf", 300).Url);
    }

    [Fact]
    public void InsecureHttpRejectedWhenFlagFalse()
    {
        Assert.Throws<ArgumentException>(() => Lib().PresignGet(Conn("http://secure-gateway:8333"), "dev/k.pdf", 300));
        Assert.Throws<ArgumentException>(() => Lib().PresignGet(Conn(presign: "http://devbox.test:8333"), "dev/k.pdf", 300));
    }

    // 3. surrogate pairs
    [Fact]
    public void TruncationNeverSplitsSurrogatePair()
    {
        var name = new string('a', 177) + "\U0001F600\U0001F600.pdf"; // cut point lands inside a pair
        var s = ObjectKeys.SanitizeFileName(name);
        Assert.True(s.Length <= ObjectKeys.MaxFileNameChars);
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i])) Assert.True(i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]));
            if (char.IsLowSurrogate(s[i])) Assert.True(i > 0 && char.IsHighSurrogate(s[i - 1]));
        }
        Assert.EndsWith(".pdf", s);
        Assert.Equal("ab", ObjectKeys.SafeCut("ab\U0001F600", 3));
    }

    // 4. key shape, Cf, NFC
    [Theory]
    [InlineData("dev/rfp/r/d/")]
    [InlineData("dev//rfp/r/d/f.pdf")]
    [InlineData("a//b")]
    public void SlashShapesRejected(string key) =>
        Assert.Throws<ArgumentException>(() => ObjectKeys.ValidateKey(key));

    [Fact]
    public void FormatCharsStrippedAndNfcApplied()
    {
        Assert.Equal("invoice.pdf", ObjectKeys.SanitizeFileName("inv‮oice​.pdf"));
        Assert.Equal("café.pdf", ObjectKeys.SanitizeFileName("café.pdf"));
        Assert.Throws<ArgumentException>(() => ObjectKeys.SanitizeFileName("​‮"));
    }

    // 5. PresignEndpoint path
    [Theory]
    [InlineData("http://localhost:8333/prefix")]
    [InlineData("http://localhost:8333/?x=1")]
    [InlineData("https://host.test/base/")]
    public void PresignEndpointWithPathRejected(string ep) =>
        Assert.Throws<ArgumentException>(() => Lib().PresignGet(Conn(presign: ep), "dev/k.pdf", 300));

    [Fact]
    public void PresignEndpointTrailingSlashOnlyAccepted() =>
        Assert.NotEmpty(Lib().PresignGet(Conn(presign: "http://localhost:8333/"), "dev/k.pdf", 300).Url);

    // 8. logging never leaks
    [Fact]
    public void LoggerNeverSeesCredentialsOrQuery()
    {
        var log = new CaptureLogger();
        var lib = Lib(log);
        var put = lib.PresignPut(Conn(), "dev/rfp/r/d/f.pdf", "application/pdf", 300);
        lib.PresignGet(Conn(), "dev/rfp/r/d/f.pdf", 300, "x.pdf");
        Assert.NotEmpty(log.Lines);
        foreach (var l in log.Lines)
        {
            Assert.DoesNotContain("test-secret-placeholder", l);
            Assert.DoesNotContain("TESTKEYID", l);
            Assert.DoesNotContain("X-Amz", l);
            Assert.DoesNotContain("?", l);
        }
        Assert.DoesNotContain(log.Lines, l => l.Contains(new Uri(put.Url).Query.Substring(1, 20)));
    }

    // 8. null / default Region and PresignEndpoint
    [Fact]
    public void NullRegionAndPresignEndpointWork()
    {
        var c = new StorageConnection
        {
            ServiceUrl = "https://s3.example.test", AccessKey = "k", SecretKey = "s", Bucket = "b", ForcePathStyle = true,
        };
        Assert.Null(c.Region); Assert.Null(c.PresignEndpoint);
        var r = Lib().PresignPut(c, "dev/k.pdf", "text/plain", 300);
        Assert.Contains("us-east-1", Uri.UnescapeDataString(r.Url));
        Assert.StartsWith("https://s3.example.test/b/", r.Url);
    }

    // 8. Head / Delete branches via a local HttpListener
    private sealed class Server : IDisposable
    {
        private readonly HttpListener _l = new();
        public string Url { get; }
        public Server(Func<string, HttpStatusCode> status)
        {
            var port = new Random().Next(20000, 40000);
            for (; ; port++)
            {
                try { _l.Prefixes.Clear(); _l.Prefixes.Add($"http://127.0.0.1:{port}/"); _l.Start(); break; }
                catch (HttpListenerException) { }
            }
            Url = $"http://127.0.0.1:{port}";
            _ = Task.Run(async () =>
            {
                while (_l.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _l.GetContextAsync(); } catch { return; }
                    ctx.Response.StatusCode = (int)status(ctx.Request.HttpMethod);
                    if (ctx.Response.StatusCode == 200 && ctx.Request.HttpMethod == "HEAD")
                    {
                        ctx.Response.ContentType = "application/pdf";
                        ctx.Response.ContentLength64 = 7;
                    }
                    ctx.Response.Close();
                }
            });
        }
        public void Dispose() { try { _l.Stop(); _l.Close(); } catch { } }
    }

    [Fact]
    public void Head404IsNotExists()
    {
        using var s = new Server(_ => HttpStatusCode.NotFound);
        var i = Lib().Head(Conn(s.Url), "dev/k.pdf");
        Assert.False(i.Exists);
        Assert.Equal(0, i.SizeBytes);
    }

    [Fact]
    public void Head200Exists()
    {
        using var s = new Server(_ => HttpStatusCode.OK);
        var i = Lib().Head(Conn(s.Url), "dev/k.pdf");
        Assert.True(i.Exists);
        Assert.Equal(7, i.SizeBytes);
        Assert.StartsWith("application/pdf", i.ContentType);
    }

    [Fact]
    public void HeadNon2xxNon404Throws()
    {
        using var s = new Server(_ => HttpStatusCode.Forbidden);
        Assert.Throws<AmazonS3Exception>(() => Lib().Head(Conn(s.Url), "dev/k.pdf"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void DeleteStatusBranches(HttpStatusCode code, bool deleted)
    {
        using var s = new Server(_ => code);
        Assert.Equal(deleted, Lib().Delete(Conn(s.Url), "dev/k.pdf").Deleted);
    }

    [Fact]
    public void DeleteForbiddenThrows()
    {
        using var s = new Server(_ => HttpStatusCode.Forbidden);
        Assert.Throws<AmazonS3Exception>(() => Lib().Delete(Conn(s.Url), "dev/k.pdf"));
    }

    [Fact]
    public void HeadUsesServiceUrlNotPresignEndpoint()
    {
        using var s = new Server(_ => HttpStatusCode.NotFound);
        // PresignEndpoint points nowhere reachable; Head must still hit ServiceUrl.
        Assert.False(Lib().Head(Conn(s.Url, presign: "http://localhost:1"), "dev/k.pdf").Exists);
    }

    [Fact]
    public void ValidateKeyAcceptsNormalKey() => ObjectKeys.ValidateKey("dev/rfp/r/d/" + Encoding.UTF8.GetString(new byte[] { 0x61 }));
}
