using OutSystems.ExternalLibraries.SDK;

namespace OdcStorage.Structures;

[OSStructure(Description = "Connection settings for an S3-compatible endpoint. Fill from secret site settings; never store in an entity.")]
public struct StorageConnection
{
    [OSStructureField(Description = "Endpoint URL used for server-side calls (head, delete); https only (http allowed for localhost).", IsMandatory = true)]
    public string ServiceUrl { get; set; }

    [OSStructureField(Description = "Signing region. Empty means us-east-1.", IsMandatory = false)]
    public string Region { get; set; }

    [OSStructureField(Description = "Access key id.", IsMandatory = true)]
    public string AccessKey { get; set; }

    [OSStructureField(Description = "Secret access key.", IsMandatory = true)]
    public string SecretKey { get; set; }

    [OSStructureField(Description = "Bucket name.", IsMandatory = true)]
    public string Bucket { get; set; }

    [OSStructureField(Description = "Use path-style addressing (/bucket/key).", IsMandatory = true)]
    public bool ForcePathStyle { get; set; }

    [OSStructureField(Description = "Optional. Browser-facing endpoint (scheme and host the browser will use, e.g. http://localhost:8333). When set, presigned URLs are signed against it; head and delete still use ServiceUrl. Empty means use ServiceUrl.", IsMandatory = false)]
    public string PresignEndpoint { get; set; }

    [OSStructureField(Description = "DEV ONLY. When true, plain http is allowed for ServiceUrl and PresignEndpoint on any host (e.g. a Private Gateway host). Default false.", IsMandatory = false)]
    public bool AllowInsecureHttp { get; set; }
}

[OSStructure(Description = "A presigned URL and the constraints the caller must honour.")]
public struct PresignResult
{
    [OSStructureField(Description = "Presigned URL.")]
    public string Url { get; set; }

    [OSStructureField(Description = "HTTP method to use (PUT or GET).")]
    public string Method { get; set; }

    [OSStructureField(Description = "Content-Type header the PUT must send exactly. Empty for GET.")]
    public string RequiredContentType { get; set; }

    [OSStructureField(Description = "UTC expiry of the URL.")]
    public DateTime ExpiresAtUtc { get; set; }
}

[OSStructure(Description = "Object metadata from a HEAD request.")]
public struct ObjectInfo
{
    [OSStructureField(Description = "True when the object exists.")]
    public bool Exists { get; set; }

    [OSStructureField(Description = "Size in bytes.")]
    public long SizeBytes { get; set; }

    [OSStructureField(Description = "Content type.")]
    public string ContentType { get; set; }

    [OSStructureField(Description = "ETag.")]
    public string ETag { get; set; }

    [OSStructureField(Description = "Last modified, UTC.")]
    public DateTime LastModifiedUtc { get; set; }
}

[OSStructure(Description = "Result of a delete.")]
public struct DeleteResult
{
    [OSStructureField(Description = "True when the server answered 204; false when the object was missing (404).")]
    public bool Deleted { get; set; }
}
