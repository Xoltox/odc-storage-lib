# Builds dist/OdcStorageLib.zip with the publish folder CONTENTS at the zip root.
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$root = $PSScriptRoot
$pub = Join-Path $root 'publish'
$dist = Join-Path $root 'dist'
$zip = Join-Path $dist 'OdcStorageLib.zip'
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }
dotnet publish (Join-Path $root 'OdcStorageLib.csproj') -c Release -r linux-x64 --self-contained false -o $pub
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$pubFull = (Resolve-Path $pub).Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
$fs = [IO.File]::Open($zip, [IO.FileMode]::Create)
$archive = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($f in Get-ChildItem $pubFull -Recurse -File) {
    $entry = $f.FullName.Substring($pubFull.Length + 1).Replace([string][IO.Path]::DirectorySeparatorChar, '/')
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
} finally { $archive.Dispose(); $fs.Dispose() }
Write-Host "Wrote $zip"
