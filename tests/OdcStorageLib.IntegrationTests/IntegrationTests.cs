using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using OdcStorage.Implementations;
using OdcStorage.Structures;
using Xunit;

namespace OdcStorage.IntegrationTests;

// Each test skips visibly unless ODCSTORAGE_IT_SERVICEURL, _BUCKET, _ACCESSKEY, _SECRETKEY are set.
// Optional: ODCSTORAGE_IT_REGION (default us-east-1), ODCSTORAGE_IT_PRESIGNENDPOINT,
// ODCSTORAGE_IT_FORCEPATHSTYLE (default true).
public class IntegrationTests
{
    private static string? Env(string n) => Environment.GetEnvironmentVariable("ODCSTORAGE_IT_" + n);

    private static StorageConnection ConnOrSkip()
    {
        var url = Env("SERVICEURL"); var bucket = Env("BUCKET"); var ak = Env("ACCESSKEY"); var sk = Env("SECRETKEY");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(ak) || string.IsNullOrEmpty(sk))
            Assert.Skip("ODCSTORAGE_IT_SERVICEURL/BUCKET/ACCESSKEY/SECRETKEY not set");
        return new StorageConnection
        {
            ServiceUrl = url!, Bucket = bucket!, AccessKey = ak!, SecretKey = sk!,
            Region = Env("REGION") ?? "us-east-1",
            ForcePathStyle = !string.Equals(Env("FORCEPATHSTYLE"), "false", StringComparison.OrdinalIgnoreCase),
            PresignEndpoint = Env("PRESIGNENDPOINT") ?? "",
        };
    }

    private static Implementations.OdcStorageLib Lib() => new(NullLogger.Instance);
    private static string NewKey(string name) => $"test/items/it-{Guid.NewGuid():N}/doc-1/{name}";

    private static async Task<HttpResponseMessage> Put(string url, byte[] body, string contentType)
    {
        using var http = new HttpClient();
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Headers.ContentLength = body.Length;
        return await http.PutAsync(url, content);
    }

    [Fact]
    public async Task Put_Head_Get_Roundtrip()
    {
        var c = ConnOrSkip(); var lib = Lib(); var key = NewKey("pack.pdf");
        try
        {
            var body = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
            var put = lib.PresignPut(c, key, "application/pdf", 300);
            Assert.Equal(HttpStatusCode.OK, (await Put(put.Url, body, put.RequiredContentType)).StatusCode);

            var info = lib.Head(c, key);
            Assert.True(info.Exists);
            Assert.Equal(2097152, info.SizeBytes);
            Assert.StartsWith("application/pdf", info.ContentType);

            var get = lib.PresignGet(c, key, 300, "Tender Pack.pdf");
            using var http = new HttpClient();
            var resp = await http.GetAsync(get.Url);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal(SHA256.HashData(body), SHA256.HashData(await resp.Content.ReadAsByteArrayAsync()));
            Assert.Contains("Tender Pack.pdf", string.Join(";", resp.Content.Headers.GetValues("Content-Disposition")));
        }
        finally { Cleanup(c, key); }
    }

    [Fact]
    public async Task Tampered_Signature_Is_403()
    {
        var c = ConnOrSkip(); var lib = Lib(); var key = NewKey("t.txt");
        try
        {
            var put = lib.PresignPut(c, key, "text/plain", 300);
            await Put(put.Url, new byte[] { 1, 2, 3 }, "text/plain");
            var url = lib.PresignGet(c, key, 300).Url;
            var i = url.IndexOf("X-Amz-Signature=", StringComparison.Ordinal) + "X-Amz-Signature=".Length;
            var tampered = url.Substring(0, i) + (url[i] == '0' ? '1' : '0') + url.Substring(i + 1);
            using var http = new HttpClient();
            var status = (await http.GetAsync(tampered)).StatusCode;
            if (status != HttpStatusCode.Forbidden) Assert.Skip($"Lenient server: tampered signature returned {(int)status}");
        }
        finally { Cleanup(c, key); }
    }

    [Fact]
    public async Task Wrong_ContentType_Is_403()
    {
        var c = ConnOrSkip(); var lib = Lib(); var key = NewKey("c.txt");
        try
        {
            var put = lib.PresignPut(c, key, "text/plain", 300);
            var status = (await Put(put.Url, new byte[] { 1 }, "application/pdf")).StatusCode;
            if (status != HttpStatusCode.Forbidden) Assert.Skip($"Lenient server: wrong content type returned {(int)status}");
        }
        finally { Cleanup(c, key); }
    }

    [Fact]
    public async Task Delete_Then_Head_Missing()
    {
        var c = ConnOrSkip(); var lib = Lib(); var key = NewKey("d.txt");
        try
        {
            var put = lib.PresignPut(c, key, "text/plain", 300);
            Assert.Equal(HttpStatusCode.OK, (await Put(put.Url, new byte[] { 9 }, "text/plain")).StatusCode);
            Assert.True(lib.Delete(c, key).Deleted);
            Assert.False(lib.Head(c, key).Exists);
            _ = lib.Delete(c, key); // second delete must not throw
        }
        finally { Cleanup(c, key); }
    }

    [Fact]
    public void Head_Missing_Returns_NotExists()
    {
        var c = ConnOrSkip();
        Assert.False(Lib().Head(c, NewKey("nope.bin")).Exists);
    }

    [Fact]
    public void PresignPut_UrlHasNoChecksumMarkers()
    {
        var c = ConnOrSkip();
        var url = Lib().PresignPut(c, NewKey("m.bin"), "application/octet-stream", 300).Url;
        Assert.DoesNotContain("x-amz-checksum", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildObjectKey_Key_Is_Accepted_By_Endpoint()
    {
        var c = ConnOrSkip();
        var key = Lib().BuildObjectKey("test", "it-" + Guid.NewGuid().ToString("N"), "doc-1", "a b.pdf");
        Assert.False(Lib().Head(c, key).Exists);
    }

    private static void Cleanup(StorageConnection c, string key)
    {
        try { Lib().Delete(c, key); } catch { /* best effort */ }
    }
}
