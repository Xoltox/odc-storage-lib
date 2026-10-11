using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RFPStorage.Internal;

internal static class ObjectKeys
{
    private static readonly Regex IdPattern = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);
    private static readonly string[] Stages = { "dev", "test", "prod" };
    public const int MaxFileNameChars = 180;

    public static ArgumentException Err(string msg) => new("RFPStorage: " + msg);

    public static void ValidateKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) throw Err("objectKey is empty.");
        if (key.StartsWith('/')) throw Err("objectKey must not start with '/'.");
        if (key.EndsWith('/')) throw Err("objectKey must not end with '/'.");
        if (key.Contains("//", StringComparison.Ordinal)) throw Err("objectKey must not contain '//'.");
        if (key.Contains("..", StringComparison.Ordinal)) throw Err("objectKey must not contain '..'.");
        if (key.Contains('\\')) throw Err("objectKey must not contain a backslash.");
        foreach (var c in key)
            if (char.IsControl(c)) throw Err("objectKey must not contain control characters.");
        if (Encoding.UTF8.GetByteCount(key) > 1024) throw Err("objectKey exceeds 1024 bytes.");
    }

    public static void ValidateContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) throw Err("contentType is empty.");
        if (contentType.Length > 255) throw Err("contentType exceeds 255 characters.");
        foreach (var c in contentType)
            if (char.IsControl(c)) throw Err("contentType must not contain control characters.");
    }

    /// <summary>Cut to maxChars without splitting a surrogate pair.</summary>
    public static string SafeCut(string s, int maxChars)
    {
        if (maxChars <= 0) return string.Empty;
        if (s.Length <= maxChars) return s;
        if (char.IsHighSurrogate(s[maxChars - 1])) maxChars--;
        return s.Substring(0, maxChars);
    }

    public static void ValidateExpiry(int expirySeconds)
    {
        if (expirySeconds < 60 || expirySeconds > 3600)
            throw Err("expirySeconds must be between 60 and 3600.");
    }

    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw Err("fileName is empty.");
        string normalized;
        try { normalized = fileName.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { throw Err("fileName is not valid Unicode."); }
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
            sb.Append(c == '/' || c == '\\' || char.IsControl(c) ? '_' : c);
        }
        var s = sb.ToString().TrimStart('.');
        if (s.Length == 0) throw Err("fileName is empty after sanitising.");
        if (s.Length > MaxFileNameChars)
        {
            var ext = Path.GetExtension(s);
            if (ext.Length > 20) ext = string.Empty;
            s = SafeCut(s, MaxFileNameChars - ext.Length) + ext;
        }
        // Sanitised names must still pass key validation (no "..").
        return s.Replace("..", "_");
    }

    public static string Build(string stage, string rfpId, string documentId, string fileName)
    {
        if (Array.IndexOf(Stages, stage) < 0) throw Err("stage must be dev, test or prod.");
        if (!IdPattern.IsMatch(rfpId ?? string.Empty)) throw Err("rfpId must match [A-Za-z0-9_-]{1,64}.");
        if (!IdPattern.IsMatch(documentId ?? string.Empty)) throw Err("documentId must match [A-Za-z0-9_-]{1,64}.");
        var key = $"{stage}/rfp/{rfpId}/{documentId}/{SanitizeFileName(fileName)}";
        ValidateKey(key);
        return key;
    }

    /// <summary>ASCII-safe fallback name for Content-Disposition.</summary>
    public static string AsciiFileName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
            sb.Append(c < 0x20 || c > 0x7E || c == '"' || c == '\\' || c == '/' || c == ';' ? '_' : c);
        return sb.ToString();
    }
}
