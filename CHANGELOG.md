# Changelog

## 0.2.0 - 2026-10-11

The library is now generic. In ODC it is imported as `OdcStorageLib` instead of `RFPStorageLib`.

### Breaking changes

- ODC library name (`[OSInterface] Name`) and assembly: `RFPStorageLib` to `OdcStorageLib`. Existing consumers must re-add the library.
- Interface: `IRFPStorageLib` to `IOdcStorageLib`. Implementation class: `RFPStorageLib` to `OdcStorageLib`.
- Root namespace `RFPStorage` to `OdcStorage` (including the embedded icon resource name).
- Error message prefix `RFPStorage:` to `OdcStorage:`; log lines likewise start with `OdcStorage`.
- `BuildObjectKey` parameter `rfpId` to `groupId`.
- `BuildObjectKey` key layout `<stage>/rfp/<id>/<documentId>/<file>` to `<stage>/items/<groupId>/<documentId>/<file>`. Keys built by 0.1.0 are still valid for the other actions.
- Integration test env vars `RFPSTORAGE_IT_*` to `ODCSTORAGE_IT_*`.
- Project, test project and packaging script names changed (`OdcStorageLib.csproj`, `OdcStorageLib.UnitTests`, `OdcStorageLib.IntegrationTests`); the zip is `OdcStorageLib.zip` from the script. Action and structure names (`PresignPut`, `PresignGet`, `Head`, `Delete`, `BuildObjectKey`, `StorageConnection`, `PresignResult`, `ObjectInfo`, `DeleteResult`) are unchanged.

### Other changes

- README documents Delete `InvalidOperationException` and unwrapped non-404 SDK exceptions.
- README lists third-party licences with versions.
- Deterministic build (`Deterministic`, `ContinuousIntegrationBuild`, `PathMap`); binaries and PDB contain no local paths.
- `packages.lock.json` committed (includes the `net10.0/linux-x64` target); restore with `--locked-mode` verified.


## 0.1.0 - 2026-10-11

- Initial public release.
- Actions: PresignPut, PresignGet, Head, Delete, BuildObjectKey.
- ServiceUrl / PresignEndpoint split for browser-facing presigned URLs.
- AllowInsecureHttp development flag.
- Unit test suite and opt-in integration tests against any S3-compatible endpoint.
