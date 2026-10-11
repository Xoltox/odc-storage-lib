using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using OdcStorage.Implementations;
using OdcStorage.Interfaces;
using OdcStorage.Internal;
using OdcStorage.Structures;
using Xunit;

namespace OdcStorage.UnitTests;

public class LibTests
{
    private static StorageConnection Conn(string url = "https://s3.example.test", bool path = true, string? presign = null,
        string bucket = "my-bucket") => new()
    {
        ServiceUrl = url, Region = "", AccessKey = "TESTKEYID", SecretKey = "test-secret-placeholder",
        Bucket = bucket, ForcePathStyle = path, PresignEndpoint = presign ?? "",
    };

    private static IOdcStorageLib Lib() => new Implementations.OdcStorageLib(NullLogger.Instance);

    // ---- key validation (12 rows) ----
    public static TheoryData<string, bool> KeyRows => new()
    {
        { "/leading", false }, { "a/../b", false }, { "a\\b", false }, { "a/b\u0001c", false },
        { "", false }, { new string('a', 1025), false }, { new string('a', 1024), true },
        { "dev/items/r1/d1/file.pdf", true }, { "dev/items/r1/d1/café menu.pdf", true },
        { "with space/and spaces.txt", true }, { "a/b/c/d/e/f.bin", true }, { "tab\there", false },
    };

    [Theory, MemberData(nameof(KeyRows))]
    public void KeyValidation(string key, bool ok)
    {
        if (ok) ObjectKeys.ValidateKey(key);
        else Assert.StartsWith("OdcStorage:", Assert.Throws<ArgumentException>(() => ObjectKeys.ValidateKey(key)).Message);
    }

    [Fact]
    public void KeyOver1024BytesUtf8Rejected()
    {
        var k = new string('é', 600); // 1200 bytes
        Assert.Throws<ArgumentException>(() => ObjectKeys.ValidateKey(k));
    }

    // ---- BuildObjectKey (8 rows) ----
    public static TheoryData<string, string, string, string, string?> BuildRows => new()
    {
        { "dev", "grp1", "doc1", "Tender Pack.pdf", "dev/items/grp1/doc1/Tender Pack.pdf" },
        { "prod", "r_1-x", "d-2", "a.docx", "prod/items/r_1-x/d-2/a.docx" },
        { "test", "r1", "d1", "../x.pdf", "test/items/r1/d1/_x.pdf" },
        { "dev", "r1", "d1", ".hidden", "dev/items/r1/d1/hidden" },
        { "staging", "r1", "d1", "a.pdf", null },
        { "dev", "a/b", "d1", "a.pdf", null },
        { "dev", "r1", "", "a.pdf", null },
        { "dev", "r1", "d1", "a/b\\c.pdf", "dev/items/r1/d1/a_b_c.pdf" },
    };

    [Theory, MemberData(nameof(BuildRows))]
    public void BuildObjectKeyTable(string stage, string grp, string doc, string file, string? expected)
    {
        var lib = Lib();
        if (expected is null)
            Assert.StartsWith("OdcStorage:", Assert.Throws<ArgumentException>(() => lib.BuildObjectKey(stage, grp, doc, file)).Message);
        else
            Assert.Equal(expected, lib.BuildObjectKey(stage, grp, doc, file));
    }

    [Fact]
    public void LongNameCappedExtensionKept()
    {
        var key = Lib().BuildObjectKey("dev", "r1", "d1", new string('n', 300) + ".pdf");
        var name = key.Split('/').Last();
        Assert.Equal(180, name.Length);
        Assert.EndsWith(".pdf", name);
    }

    [Fact]
    public void GroupIdOver64Rejected() =>
        Assert.Throws<ArgumentException>(() => Lib().BuildObjectKey("dev", new string('a', 65), "d1", "a.pdf"));

    // ---- expiry (6 rows x put/get) ----
    public static TheoryData<int, bool> ExpiryRows => new()
    {
        { 59, false }, { 60, true }, { 3600, true }, { 3601, false }, { 0, false }, { -5, false },
    };

    [Theory, MemberData(nameof(ExpiryRows))]
    public void ExpiryPut(int s, bool ok)
    {
        if (ok) Assert.NotEmpty(Lib().PresignPut(Conn(), "dev/k.pdf", "application/pdf", s).Url);
        else Assert.StartsWith("OdcStorage:", Assert.Throws<ArgumentException>(() => Lib().PresignPut(Conn(), "dev/k.pdf", "application/pdf", s)).Message);
    }

    [Theory, MemberData(nameof(ExpiryRows))]
    public void ExpiryGet(int s, bool ok)
    {
        if (ok) Assert.NotEmpty(Lib().PresignGet(Conn(), "dev/k.pdf", s).Url);
        else Assert.Throws<ArgumentException>(() => Lib().PresignGet(Conn(), "dev/k.pdf", s));
    }

    // ---- presign PUT ----
    [Fact]
    public void PresignPutShape()
    {
        var r = Lib().PresignPut(Conn(), "dev/items/r1/d1/a b.pdf", "application/pdf", 300);
        Assert.StartsWith("https://s3.example.test/my-bucket/dev/items/r1/d1/", r.Url);
        Assert.Contains("X-Amz-Signature=", r.Url);
        Assert.Contains("X-Amz-Expires=300", r.Url);
        var signed = Uri.UnescapeDataString(Query(r.Url)["X-Amz-SignedHeaders"]);
        Assert.Contains("content-type", signed);
        foreach (var bad in new[] { "x-amz-checksum", "x-amz-sdk-checksum-algorithm", "STREAMING" })
            Assert.DoesNotContain(bad, r.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("PUT", r.Method);
        Assert.Equal("application/pdf", r.RequiredContentType);
    }

    [Fact]
    public void VirtualHostStyleWhenPathStyleFalse()
    {
        var r = Lib().PresignPut(Conn(path: false), "dev/k.pdf", "application/pdf", 300);
        Assert.DoesNotContain("/my-bucket/", r.Url);
    }

    // ---- presign GET ----
    [Fact]
    public void PresignGetWithoutDownloadName()
    {
        var r = Lib().PresignGet(Conn(), "dev/k.pdf", 300);
        Assert.DoesNotContain("response-content-disposition", r.Url);
        Assert.Equal("GET", r.Method);
    }

    [Fact]
    public void PresignGetWithDownloadName()
    {
        var r = Lib().PresignGet(Conn(), "dev/k.pdf", 300, "Tender Pack.pdf");
        var cd = Uri.UnescapeDataString(Query(r.Url)["response-content-disposition"]);
        Assert.Contains("attachment", cd);
        Assert.Contains("filename=\"Tender Pack.pdf\"", cd);
        Assert.Contains("filename*=UTF-8''Tender%20Pack.pdf", cd);
        Assert.DoesNotContain("x-amz-checksum", r.Url, StringComparison.OrdinalIgnoreCase);
    }

    // ---- PresignEndpoint ----
    [Fact]
    public void PresignEndpointHostUsed()
    {
        var r = Lib().PresignPut(Conn("https://gateway.internal.test", presign: "http://localhost:8333"), "dev/k.pdf", "text/plain", 300);
        var u = new Uri(r.Url);
        Assert.Equal("localhost", u.Host);
        Assert.Equal(8333, u.Port);
        Assert.Equal("http", u.Scheme);
    }

    [Fact]
    public void PresignEndpointSignatureMatchesThatHost()
    {
        var conn = Conn("https://gateway.internal.test", presign: "http://localhost:8333");
        var viaPresign = Lib().PresignGet(conn, "dev/k.pdf", 300);
        // Same credentials, same params, but ServiceUrl = the browser host: signature must equal
        // the one produced with PresignEndpoint set (same X-Amz-Date second is required; compare by re-signing).
        var q1 = Query(viaPresign.Url);
        var direct = Lib().PresignGet(Conn("http://localhost:8333"), "dev/k.pdf", 300);
        var q2 = Query(direct.Url);
        // Signature depends on timestamp and expiry; when both fall in the same second they must match.
        if (q1["X-Amz-Date"] == q2["X-Amz-Date"] && q1["X-Amz-Expires"] == q2["X-Amz-Expires"])
            Assert.Equal(q2["X-Amz-Signature"], q1["X-Amz-Signature"]);
        // Signature against a different host must differ.
        var other = Lib().PresignGet(Conn("https://gateway.internal.test"), "dev/k.pdf", 300);
        var q3 = Query(other.Url);
        if (q1["X-Amz-Date"] == q3["X-Amz-Date"])
            Assert.NotEqual(q3["X-Amz-Signature"], q1["X-Amz-Signature"]);
        Assert.Equal("localhost:8333", new Uri(viaPresign.Url).Authority);
    }

    [Fact]
    public void PresignEndpointEmptyFallsBackToServiceUrl()
    {
        var r = Lib().PresignPut(Conn("https://gateway.internal.test", presign: ""), "dev/k.pdf", "text/plain", 300);
        Assert.Equal("gateway.internal.test", new Uri(r.Url).Host);
    }

    [Fact]
    public void PresignEndpointNonLocalHttpRejected() =>
        Assert.Throws<ArgumentException>(() => Lib().PresignPut(Conn(presign: "http://example.test"), "dev/k.pdf", "text/plain", 300));

    // ---- connection validation ----
    [Fact]
    public void MissingBucketRejected() =>
        Assert.StartsWith("OdcStorage:", Assert.Throws<ArgumentException>(() => Lib().PresignGet(Conn(bucket: ""), "k", 300)).Message);

    [Fact]
    public void EmptyServiceUrlRejected() =>
        Assert.Throws<ArgumentException>(() => Lib().PresignGet(Conn(url: ""), "k", 300));

    [Fact]
    public void HttpNonLocalRejected() =>
        Assert.Throws<ArgumentException>(() => Lib().PresignGet(Conn(url: "http://s3.example.test"), "k", 300));

    [Theory]
    [InlineData("http://localhost:8333")]
    [InlineData("http://127.0.0.1:8333")]
    public void HttpLocalAccepted(string url) =>
        Assert.NotEmpty(Lib().PresignGet(Conn(url: url), "dev/k.pdf", 300).Url);

    [Fact]
    public void ChecksumSettingsAreWhenRequired()
    {
        using var c = S3ClientFactory.Create(Conn());
        Assert.Equal(Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED, c.Config.RequestChecksumCalculation);
        Assert.Equal(Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED, c.Config.ResponseChecksumValidation);
    }

    // ---- reflection ----
    private static readonly HashSet<Type> Supported = new()
    {
        typeof(string), typeof(int), typeof(long), typeof(bool), typeof(DateTime),
        typeof(StorageConnection), typeof(PresignResult), typeof(ObjectInfo), typeof(DeleteResult),
    };

    [Fact]
    public void InterfaceExposesExactlyFiveSupportedActions()
    {
        var methods = typeof(IOdcStorageLib).GetMethods();
        Assert.Equal(new[] { "BuildObjectKey", "Delete", "Head", "PresignGet", "PresignPut" },
            methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        foreach (var m in methods)
        {
            Assert.Contains(m.ReturnType, Supported);
            foreach (var p in m.GetParameters())
            {
                Assert.False(p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(Task<>));
                Assert.Contains(p.ParameterType, Supported);
            }
            Assert.NotEqual(typeof(Task), m.ReturnType);
        }
    }

    [Fact]
    public void StructureFieldsAreSupportedTypes()
    {
        foreach (var t in new[] { typeof(StorageConnection), typeof(PresignResult), typeof(ObjectInfo), typeof(DeleteResult) })
            foreach (var p in t.GetProperties())
                Assert.Contains(p.PropertyType, Supported);
    }

    private static Dictionary<string, string> Query(string url) =>
        new Uri(url).Query.TrimStart('?').Split('&')
            .Select(kv => kv.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
}
