#!/usr/bin/env pwsh
param (
    [string]$OutputDirectory,
    [string]$OS,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$PSDefaultParameterValues['*:ExecutionPolicy'] = 'Bypass'
$repoRoot = Split-Path -Parent $PSScriptRoot

if(!$OutputDirectory) {
    $OutputDirectory = "$repoRoot/.work/build"
}

if(!$OS) {
    $OS = if ($IsWindows) { 'windows' } elseif ($IsLinux) { 'linux' } else { throw "Unsupported OS: $($PSVersionTable.OS)" }
}

Remove-Item $OutputDirectory -Recurse -Force -ErrorAction SilentlyContinue -ProgressAction SilentlyContinue

$depsDirectory = "$repoRoot/src/deps"
Remove-Item $depsDirectory -Recurse -Force -ErrorAction SilentlyContinue -ProgressAction SilentlyContinue

& "$PSScriptRoot/Download-UEDeps.ps1" -OS $OS -OutputDirectory $depsDirectory

$runtime = if ($OS -eq 'windows') { 'win-x64' } elseif ($OS -eq 'linux') { 'linux-x64' } else { throw "Unsupported OS: $OS" }

$buildArgs = @(
    "$repoRoot/src"
    '--self-contained'
    '--configuration', 'Release'
    '--runtime', $runtime
    '--output', $OutputDirectory
    '--property:PublishSingleFile=true'
)

if ($Version) {
    $buildArgs += "--property:Version=$Version"
}

$normalizedOutputDir = $OutputDirectory.Replace('\', '/')
Write-Host "Closing cue4mcp processes run from $normalizedOutputDir"

$processes = Get-Process | Where-Object { $_.ProcessName -like "*cue4mcp*" -and $_.Path.Replace('\', '/').StartsWith($normalizedOutputDir, 'OrdinalIgnoreCase') } | Select-Object Id, ProcessName, Path
if ($processes) {
    foreach ($proc in $processes) {
        Write-Host " - Id: $($proc.Id), Name: $($proc.ProcessName)"
    }
    
    $processes | Stop-Process -Force
    Start-Sleep -Seconds 2
}

if (Test-Path $OutputDirectory) {
    Write-Host "Deleting existing output directory: $normalizedOutputDir"
    Remove-Item $OutputDirectory -Recurse -Force -ErrorAction Continue
}

Write-Host "> dotnet publish $($buildArgs -join ' ')"
dotnet publish @buildArgs

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}
