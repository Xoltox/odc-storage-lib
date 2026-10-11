using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using RFPStorage.Interfaces;
using RFPStorage.Internal;
using RFPStorage.Structures;

namespace RFPStorage.Implementations;

public class RFPStorageLib : IRFPStorageLib
{
    private readonly ILogger _logger;

    public RFPStorageLib(ILogger logger)
    {
        _logger = logger;
    }

    public PresignResult PresignPut(StorageConnection connection, string objectKey, string contentType, int expirySeconds)
    {
        ObjectKeys.ValidateKey(objectKey);
        ObjectKeys.ValidateExpiry(expirySeconds);
        ObjectKeys.ValidateContentType(contentType);
        using var client = S3ClientFactory.CreateForPresign(connection);
        var expires = DateTime.UtcNow.AddSeconds(expirySeconds);
        var url = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = connection.Bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expires,
            ContentType = contentType,
            Protocol = S3ClientFactory.PresignProtocol(connection),
        });
        _logger.LogInformation("RFPStorage PresignPut key={Key} expirySeconds={Expiry}", objectKey, expirySeconds);
        return new PresignResult { Url = url, Method = "PUT", RequiredContentType = contentType, ExpiresAtUtc = expires };
    }

    public PresignResult PresignGet(StorageConnection connection, string objectKey, int expirySeconds, string? downloadFileName = null)
    {
        ObjectKeys.ValidateKey(objectKey);
        ObjectKeys.ValidateExpiry(expirySeconds);
        using var client = S3ClientFactory.CreateForPresign(connection);
        var expires = DateTime.UtcNow.AddSeconds(expirySeconds);
        var req = new GetPreSignedUrlRequest
        {
            BucketName = connection.Bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = expires,
            Protocol = S3ClientFactory.PresignProtocol(connection),
        };
        if (!string.IsNullOrWhiteSpace(downloadFileName))
        {
            var clean = ObjectKeys.SanitizeFileName(downloadFileName);
            req.ResponseHeaderOverrides.ContentDisposition =
                "attachment; filename=\"" + ObjectKeys.AsciiFileName(clean) + "\"; filename*=UTF-8''" + Uri.EscapeDataString(clean);
        }
        var url = client.GetPreSignedURL(req);
        _logger.LogInformation("RFPStorage PresignGet key={Key} expirySeconds={Expiry}", objectKey, expirySeconds);
        return new PresignResult { Url = url, Method = "GET", RequiredContentType = string.Empty, ExpiresAtUtc = expires };
    }

    public ObjectInfo Head(StorageConnection connection, string objectKey)
    {
        ObjectKeys.ValidateKey(objectKey);
        using var client = S3ClientFactory.Create(connection);
        try
        {
            var r = client.GetObjectMetadataAsync(connection.Bucket, objectKey).GetAwaiter().GetResult();
            _logger.LogInformation("RFPStorage Head key={Key} exists=true", objectKey);
            return new ObjectInfo
            {
                Exists = true,
                SizeBytes = r.ContentLength,
                ContentType = r.Headers.ContentType ?? string.Empty,
                ETag = r.ETag ?? string.Empty,
                LastModifiedUtc = (r.LastModified ?? DateTime.UnixEpoch).ToUniversalTime(),
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("RFPStorage Head key={Key} exists=false", objectKey);
            return new ObjectInfo { Exists = false, ContentType = string.Empty, ETag = string.Empty };
        }
    }

    public DeleteResult Delete(StorageConnection connection, string objectKey)
    {
        ObjectKeys.ValidateKey(objectKey);
        using var client = S3ClientFactory.Create(connection);
        try
        {
            var r = client.DeleteObjectAsync(connection.Bucket, objectKey).GetAwaiter().GetResult();
            var code = (int)r.HttpStatusCode;
            if (code == 404)
            {
                _logger.LogInformation("RFPStorage Delete key={Key} deleted=false", objectKey);
                return new DeleteResult { Deleted = false };
            }
            if (code < 200 || code > 299)
                throw new InvalidOperationException("RFPStorage: unexpected delete status " + code + ".");
            _logger.LogInformation("RFPStorage Delete key={Key} deleted=true", objectKey);
            return new DeleteResult { Deleted = true };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("RFPStorage Delete key={Key} deleted=false", objectKey);
            return new DeleteResult { Deleted = false };
        }
    }

    public string BuildObjectKey(string stage, string rfpId, string documentId, string fileName)
        => ObjectKeys.Build(stage, rfpId, documentId, fileName);
}
