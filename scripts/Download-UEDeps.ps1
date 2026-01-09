param(
    [string]$OS,
    [string]$OutputDirectory
)

# Script configuration
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Write-Host "Loading info from deps.json" -ForegroundColor Yellow
$depsFile = "$PSScriptRoot/deps.json"
$deps = Get-Content $depsFile | ConvertFrom-Json
$webClient = New-Object System.Net.WebClient

foreach($dep in $deps.$OS) {
    # Download the compressed pack
    $compressedData = $webClient.DownloadData($dep.Url)

    Write-Host "Downloaded $($compressedData.Length) bytes, decompressing..." -ForegroundColor Yellow

    # Decompress using GZip
    $compressedStream = New-Object System.IO.MemoryStream(,$compressedData)
    $gzipStream = New-Object System.IO.Compression.GZipStream($compressedStream, [System.IO.Compression.CompressionMode]::Decompress)
    $decompressedStream = New-Object System.IO.MemoryStream
    $decompressedData = $null

    try {
        $gzipStream.CopyTo($decompressedStream)
        $decompressedData = $decompressedStream.ToArray()
    }
    finally {
        $gzipStream.Dispose()
        $compressedStream.Dispose()
        $decompressedStream.Dispose()
    }

    Write-Host "Decompressed to $($decompressedData.Length) bytes" -ForegroundColor Yellow

    # Extract the specific file from the pack
    $startOffset = $dep.PackOffset
    $endOffset = $startOffset + $dep.Size

    if ($endOffset -gt $decompressedData.Length) {
        throw "File extends beyond pack boundaries"
    }

    $fileData = $decompressedData[$startOffset..($endOffset-1)]
    $Path = Join-Path $OutputDirectory $dep.Name
    
    Write-Host "Saving file to: $Path" -ForegroundColor Yellow
    [System.IO.File]::WriteAllBytes($Path, $fileData)
}
