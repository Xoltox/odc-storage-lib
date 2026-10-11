using OutSystems.ExternalLibraries.SDK;
using RFPStorage.Structures;

namespace RFPStorage.Interfaces;

[OSInterface(Name = "RFPStorageLib",
    Description = "Presigned URL, head and delete operations over an S3-compatible endpoint. Bytes never pass through this library.",
    IconResourceName = "RFPStorage.Resources.icon.png")]
public interface IRFPStorageLib
{
    [OSAction(Description = "Presign a PUT URL. The browser must send exactly the given content type.", ReturnName = "Result")]
    PresignResult PresignPut(StorageConnection connection, string objectKey, string contentType, int expirySeconds);

    [OSAction(Description = "Presign a GET URL, optionally forcing a download file name.", ReturnName = "Result")]
    PresignResult PresignGet(StorageConnection connection, string objectKey, int expirySeconds, string? downloadFileName = null);

    [OSAction(Description = "HEAD an object. Exists is false when missing.", ReturnName = "Info")]
    ObjectInfo Head(StorageConnection connection, string objectKey);

    [OSAction(Description = "Delete an object. Deleted is false when the object was missing.", ReturnName = "Result")]
    DeleteResult Delete(StorageConnection connection, string objectKey);

    [OSAction(Description = "Build the standard object key stage/rfp/rfpId/documentId/fileName with a sanitised file name.", ReturnName = "ObjectKey")]
    string BuildObjectKey(string stage, string rfpId, string documentId, string fileName);
}
