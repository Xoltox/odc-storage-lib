# odc-storage-lib

S3-compatible object storage for OutSystems Developer Cloud (ODC): presigned URLs, head and delete, with bytes never passing through your app.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![ODC External Logic](https://img.shields.io/badge/ODC-External%20Logic-red)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Release](https://img.shields.io/github/v/release/Xoltox/odc-storage-lib)](https://github.com/Xoltox/odc-storage-lib/releases)

## What it does

`odc-storage-lib` is an ODC External Logic library (C#, .NET 10) that lets an ODC app work with any S3-compatible store (AWS S3, MinIO, SeaweedFS, Cloudflare R2 and similar). The library only signs URLs and issues small metadata calls. Uploads and downloads go directly between the browser and the storage endpoint, so large files never touch ODC.

## Features

- Presigned PUT and GET URLs with 60 to 3600 second expiry.
- PUT URLs pin the exact `Content-Type` the browser must send.
- Optional forced download file name on GET (`Content-Disposition`, with an ASCII fallback).
- `ServiceUrl` / `PresignEndpoint` split: server-side calls use one address, presigned URLs are signed for the address the browser actually uses.
- `AllowInsecureHttp` development flag for plain http on non-localhost hosts (for example a Private Gateway).
- HEAD and DELETE that report "missing" as a normal result instead of an error.
- Object key builder with strict validation and file name sanitising (path traversal, control characters, Unicode normalisation, length limits).
- Path-style addressing option and configurable region.
- Credentials are never logged.

## Exposed actions

| Action | Inputs | Returns |
|---|---|---|
| `PresignPut` | `connection`, `objectKey`, `contentType`, `expirySeconds` | `PresignResult` |
| `PresignGet` | `connection`, `objectKey`, `expirySeconds`, `downloadFileName` (optional) | `PresignResult` |
| `Head` | `connection`, `objectKey` | `ObjectInfo` (`Exists` is false when missing) |
| `Delete` | `connection`, `objectKey` | `DeleteResult` (`Deleted` is false when missing) |
| `BuildObjectKey` | `stage`, `rfpId`, `documentId`, `fileName` | text key |

## Structures

| Structure | Fields |
|---|---|
| `StorageConnection` | `ServiceUrl`, `Region`, `AccessKey`, `SecretKey`, `Bucket`, `ForcePathStyle`, `PresignEndpoint`, `AllowInsecureHttp` |
| `PresignResult` | `Url`, `Method`, `RequiredContentType`, `ExpiresAtUtc` |
| `ObjectInfo` | `Exists`, `SizeBytes`, `ContentType`, `ETag`, `LastModifiedUtc` |
| `DeleteResult` | `Deleted` |

## Install

1. Download `odc-storage-lib-v0.1.0.zip` from the [latest release](https://github.com/Xoltox/odc-storage-lib/releases/latest).
2. In the ODC Portal, open **External Logic**, upload the zip and publish it.
3. In ODC Studio, add the library as a dependency and select the actions you need.

The zip is a `linux-x64`, framework-dependent publish with the DLLs and `deps.json` at the archive root.

## Usage

Build a `StorageConnection` from secret site settings (never store it in an entity), then:

1. Server action: `PresignPut(connection, key, "application/pdf", 300)`.
2. Client: send an HTTP `PUT` of the file bytes to `Result.Url` with the `Content-Type` header set to exactly `Result.RequiredContentType`.
3. Server action: `Head(connection, key)` to confirm `Exists` and `SizeBytes`.
4. Later: `PresignGet(connection, key, 300, "Report.pdf")` and give `Result.Url` to the browser.
5. `Delete(connection, key)` when the object is no longer needed.

`BuildObjectKey` produces `<stage>/rfp/<rfpId>/<documentId>/<fileName>`, where stage is `dev`, `test` or `prod`, ids match `[A-Za-z0-9_-]{1,64}`, and the file name is sanitised (separators and control characters become `_`, leading dots are stripped, 180 characters maximum keeping the extension). It is a convenience; the other actions accept any valid key.

## Configuration

| Field | Notes |
|---|---|
| `ServiceUrl` | Endpoint for server-side calls (head, delete). https only; http allowed for `localhost` / `127.0.0.1`. |
| `Region` | Signing region. Empty means `us-east-1`. |
| `AccessKey`, `SecretKey` | Credentials. Keep in secret site settings. |
| `Bucket` | Bucket name. |
| `ForcePathStyle` | `true` for `/bucket/key` addressing (most self-hosted stores). |
| `PresignEndpoint` | Optional browser-facing scheme and host (no path, query or fragment). Presigned URLs are signed against it; head and delete still use `ServiceUrl`. Empty means `ServiceUrl`. |
| `AllowInsecureHttp` | DEV ONLY. Allows plain http for both URLs on any host. Never enable in production. |

Errors are `ArgumentException` with a message starting `RFPStorage:`. Configure CORS on your bucket so the browser may `PUT` and `GET` from your app origin.

## Building from source

Requires the .NET 10 SDK.

```
dotnet build
powershell -File generate_upload_package.ps1
```

The script runs `dotnet publish -c Release -r linux-x64 --self-contained false` and zips the output flat into `dist/RFPStorageLib.zip`.

## Testing

```
dotnet test --project tests/RFPStorageLib.UnitTests
```

`global.json` selects the Microsoft.Testing.Platform runner required on .NET 10. Unit tests are offline.

Integration tests run against a real endpoint and skip unless these environment variables are set:

- `RFPSTORAGE_IT_SERVICEURL`, `RFPSTORAGE_IT_BUCKET`, `RFPSTORAGE_IT_ACCESSKEY`, `RFPSTORAGE_IT_SECRETKEY` (required)
- `RFPSTORAGE_IT_REGION` (default `us-east-1`), `RFPSTORAGE_IT_PRESIGNENDPOINT` (optional), `RFPSTORAGE_IT_FORCEPATHSTYLE` (default `true`)

```
dotnet test --project tests/RFPStorageLib.IntegrationTests
```

Use a throwaway bucket; the tests write and delete objects under `test/rfp/`.

## Security notes

- Treat presigned URLs as bearer tokens: use short expiries and do not log them.
- The library never logs access or secret keys.
- Use https everywhere except local development. `AllowInsecureHttp` sends signed traffic unencrypted.
- Object keys reject `..`, backslashes, control characters, leading or trailing `/`, `//`, and anything over 1024 UTF-8 bytes.

## Limitations

- No multipart upload; a single presigned PUT per object.
- No list, copy or bulk delete actions.
- Expiry is limited to 60 to 3600 seconds.
- `BuildObjectKey` encodes an `rfp` path convention and a fixed stage set.
- Packaged for `linux-x64` (the ODC runtime).

## License

[MIT](LICENSE) - copyright 2026 Parth Sharma.
