using Amazon.Runtime;
using Amazon.S3;
using OdcStorage.Structures;

namespace OdcStorage.Internal;

internal static class S3ClientFactory
{
    private static void ValidateUrl(string name, string? url, bool allowInsecure, bool noPath = false)
    {
        if (string.IsNullOrWhiteSpace(url)) throw ObjectKeys.Err(name + " is empty.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw ObjectKeys.Err(name + " is not a valid URL.");
        if (noPath && (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)))
            throw ObjectKeys.Err(name + " must not contain a path, query or fragment.");
        if (uri.Scheme != "https" && uri.Scheme != "http") throw ObjectKeys.Err(name + " must be https or http.");
        var local = uri.Host == "localhost" || uri.Host == "127.0.0.1";
        if (uri.Scheme != "https" && !(allowInsecure || (local && uri.Scheme == "http")))
            throw ObjectKeys.Err(name + " must be https (http only for localhost or when AllowInsecureHttp is set).");
    }

    public static void Validate(StorageConnection c)
    {
        if (string.IsNullOrWhiteSpace(c.ServiceUrl)) throw ObjectKeys.Err("connection.ServiceUrl is empty.");
        if (string.IsNullOrWhiteSpace(c.Bucket)) throw ObjectKeys.Err("connection.Bucket is empty.");
        if (string.IsNullOrWhiteSpace(c.AccessKey)) throw ObjectKeys.Err("connection.AccessKey is empty.");
        if (string.IsNullOrWhiteSpace(c.SecretKey)) throw ObjectKeys.Err("connection.SecretKey is empty.");
        ValidateUrl("connection.ServiceUrl", c.ServiceUrl, c.AllowInsecureHttp);
        if (!string.IsNullOrWhiteSpace(c.PresignEndpoint))
            ValidateUrl("connection.PresignEndpoint", c.PresignEndpoint, c.AllowInsecureHttp, noPath: true);
    }

    /// <summary>Client for server-side calls (head, delete): signs against ServiceUrl.</summary>
    public static AmazonS3Client Create(StorageConnection c)
    {
        Validate(c);
        return Build(c, c.ServiceUrl);
    }

    /// <summary>Client for presigning: signs against PresignEndpoint when set, else ServiceUrl.</summary>
    public static AmazonS3Client CreateForPresign(StorageConnection c)
    {
        Validate(c);
        return Build(c, string.IsNullOrWhiteSpace(c.PresignEndpoint) ? c.ServiceUrl : c.PresignEndpoint);
    }

    /// <summary>The SDK presigns with https unless told otherwise; follow the scheme of the signing endpoint.</summary>
    public static Amazon.S3.Protocol PresignProtocol(StorageConnection c)
    {
        var url = string.IsNullOrWhiteSpace(c.PresignEndpoint) ? c.ServiceUrl : c.PresignEndpoint;
        return new Uri(url).Scheme == "http" ? Amazon.S3.Protocol.HTTP : Amazon.S3.Protocol.HTTPS;
    }

    private static AmazonS3Client Build(StorageConnection c, string url)
    {
        var region = string.IsNullOrWhiteSpace(c.Region) ? "us-east-1" : c.Region;
        var config = new AmazonS3Config
        {
            ServiceURL = url,
            ForcePathStyle = c.ForcePathStyle,
            AuthenticationRegion = region,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        return new AmazonS3Client(new BasicAWSCredentials(c.AccessKey, c.SecretKey), config);
    }
}
