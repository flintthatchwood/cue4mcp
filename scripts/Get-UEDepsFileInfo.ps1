<#
.SYNOPSIS
    PowerShell script to parse Unreal Engine gitdeps files and download dependencies.

.DESCRIPTION
    This PowerShell script provides functionality to parse Commit.gitdeps.xml files
    and download specific files from the remote pack storage. If the gitdeps file
    doesn't exist locally, it can automatically download it from the Unreal Engine
    Git repository.

.PARAMETER GitdepsPath
    Path to the Commit.gitdeps.xml file. If the file doesn't exist, it will be
    downloaded from the Git repository.

.PARAMETER FilePath
    Path of the file to download from the gitdeps

.PARAMETER OutputPath
    Directory where the downloaded file will be saved

.PARAMETER FindFileNames
    Find all file names starting with the specified pattern

.PARAMETER GitRepository
    Git repository URL to download gitdeps from if not found locally.
    Default: https://github.com/EpicGames/UnrealEngine.git

.PARAMETER GitBranch
    Git branch to use when downloading gitdeps file.
    Default: release

.PARAMETER GitdepsRelativePath
    Relative path to the gitdeps file within the Git repository.
    Default: Engine/Build/Commit.gitdeps.xml

.EXAMPLE
    .\Get-UEDepsFile.ps1 -FilePath "Engine/Source/Runtime/OodleDataCompression/Sdks/2.9.8/lib/Win64/oodle2.dll" -OutputPath "."
    
.EXAMPLE
    .\Get-UEDepsFile.ps1 -FindFileNames "Engine/Source/Runtime/OodleDataCompression/Sdks/"

.EXAMPLE
    .\Get-UEDepsFile.ps1 -GitdepsPath "custom-gitdeps.xml" -FilePath "some/file/path" -OutputPath "."

.NOTES
    Author: GitHub Copilot (PowerShell port of Python original)
    License: GPL-3.0-or-later
    Requires: PowerShell 5.1 or later, Git CLI (if downloading gitdeps)
#>

[CmdletBinding(DefaultParameterSetName = 'Export')]
param(
    [Parameter(Mandatory = $false)]
    [string]$GitdepsPath = "$PSScriptRoot\..\.work\Commit.gitdeps.xml",
    
    [Parameter(Mandatory = $true, ParameterSetName = 'Export')]
    [string[]]$FilePaths,
    
    [Parameter(Mandatory = $true, ParameterSetName = 'Find')]
    [string[]]$FindFileNames,
    
    [Parameter(Mandatory = $false)]
    [string]$GitRepository = "https://github.com/EpicGames/UnrealEngine.git",
    
    [Parameter(Mandatory = $false)]
    [string]$GitBranch = "release",
    
    [Parameter(Mandatory = $false)]
    [string]$GitdepsRelativePath = "Engine/Build/Commit.gitdeps.xml"
)

# Script configuration
$ErrorActionPreference = "Stop"

# Function to check if Git is available
function Test-GitAvailable {
    try {
        $null = & git --version 2>$null
        return $true
    }
    catch {
        return $false
    }
}

# Function to download gitdeps file using Git
function Get-GitdepsFromGit {
    param(
        [string]$OutputPath
    )
    
    Write-Host "Gitdeps file not found locally, attempting to download from Git..." -ForegroundColor Yellow
    
    if (-not (Test-GitAvailable)) {
        throw "Git CLI is not available. Please install Git and ensure it's in your PATH, or provide a local gitdeps file."
    }
    
    # Create a temporary directory for git operations
    $tempDir = Join-Path $env:TEMP "get-ue-deps-$(Get-Random)"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    
    try {
        $originalLocation = Get-Location
        Set-Location $tempDir
        
        Write-Host "Setting up temporary Git repository..." -ForegroundColor Yellow
        
        # Initialize git repository
        & git init -q 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Failed to initialize git repository" }
        
        # Add remote
        & git remote add origin $GitRepository 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Failed to add remote repository" }
        
        # Enable sparse checkout
        & git config core.sparseCheckout true 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Failed to configure sparse checkout" }
        
        # Set up sparse checkout to only get the gitdeps file
        $sparseCheckoutFile = Join-Path $tempDir ".git\info\sparse-checkout"
        Set-Content -Path $sparseCheckoutFile -Value $GitdepsRelativePath
        
        # Fetch the specific branch (shallow clone)
        Write-Host "Fetching gitdeps file from branch '$GitBranch'..." -ForegroundColor Yellow
        & git fetch origin $GitBranch --depth=1 -q 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Failed to fetch from remote repository. Check your internet connection and repository access." }
        
        # Checkout the branch
        & git checkout "origin/$GitBranch" -q 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Failed to checkout branch '$GitBranch'" }
        
        # Copy the gitdeps file to the output location
        $sourceFile = Join-Path $tempDir $GitdepsRelativePath
        if (-not (Test-Path $sourceFile)) {
            throw "Gitdeps file not found in repository at path: $GitdepsRelativePath"
        }
        
        Copy-Item $sourceFile $OutputPath -Force
        Write-Host "Successfully downloaded gitdeps file to: $OutputPath" -ForegroundColor Green
        
        Set-Location $originalLocation
    }
    catch {
        Set-Location $originalLocation
        throw "Failed to download gitdeps file: $($_.Exception.Message)"
    }
    finally {
        # Clean up temporary directory
        if (Test-Path $tempDir) {
            try {
                Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
            catch {
                Write-Host "Warning: Could not fully clean up temporary directory: $tempDir" -ForegroundColor Yellow
            }
        }
    }
}

# Function to parse gitdeps XML and create a simple data structure
function Read-GitdepsXml {
    param([string]$FilePath)
    
    if (-not (Test-Path $FilePath)) {
        throw "File path does not exist: $FilePath"
    }
    
    Write-Host "Loading and parsing XML file..." -ForegroundColor Yellow
    [xml]$xml = Get-Content $FilePath
    
    $baseUrl = $xml.DependencyManifest.BaseUrl
    
    # Parse files
    $files = @{}
    foreach ($file in $xml.DependencyManifest.Files.File) {
        $files[$file.Name] = [PSCustomObject]@{
            Name = $file.Name
            Hash = $file.Hash
            IsExecutable = $file.PSObject.Properties.Name -contains "IsExecutable" -and $file.IsExecutable -eq "true"
        }
    }
    
    # Parse blobs
    $blobs = @{}
    foreach ($blob in $xml.DependencyManifest.Blobs.Blob) {
        $blobs[$blob.Hash] = [PSCustomObject]@{
            Hash = $blob.Hash
            Size = [int]$blob.Size
            PackHash = $blob.PackHash
            PackOffset = [int]$blob.PackOffset
        }
    }
    
    # Parse packs
    $packs = @{}
    foreach ($pack in $xml.DependencyManifest.Packs.Pack) {
        $packs[$pack.Hash] = [PSCustomObject]@{
            Hash = $pack.Hash
            Size = [int]$pack.Size
            CompressedSize = [int]$pack.CompressedSize
            RemotePath = $pack.RemotePath
        }
    }
    
    return [PSCustomObject]@{
        BaseUrl = $baseUrl
        Files = $files
        Blobs = $blobs
        Packs = $packs
    }
}

# Function to find file names matching a pattern
function Find-FileNames {
    param(
        [PSCustomObject]$GitdepsData,
        [string[]]$NamePatterns
    )
    
    $matchingFiles = @()
    foreach ($fileName in $GitdepsData.Files.Keys) {
        foreach ($NamePattern in $NamePatterns) {
            if ($fileName -like "*$NamePattern*") {
                $matchingFiles += $fileName
            }
        }
    }
    
    return $matchingFiles
}

# Function to get file URL and metadata
function Get-FileUrl {
    param(
        [PSCustomObject]$GitdepsData,
        [string]$FilePath
    )
    
    # Get file info
    $file = $GitdepsData.Files[$FilePath]
    if (-not $file) {
        throw "File not found: $FilePath"
    }
    
    # Get blob info
    $blob = $GitdepsData.Blobs[$file.Hash]
    if (-not $blob) {
        throw "Blob not found for hash: $($file.Hash)"
    }
    
    # Get pack info
    $pack = $GitdepsData.Packs[$blob.PackHash]
    if (-not $pack) {
        throw "Pack not found for hash: $($blob.PackHash)"
    }
    
    $url = "$($GitdepsData.BaseUrl)/$($pack.RemotePath)/$($blob.PackHash)"
    
    return [PSCustomObject]@{
        Name = Split-Path $FilePath -Leaf
        Path = $FilePath
        Url = $url
        Size = $blob.Size
        PackOffset = $blob.PackOffset
    }
}

Write-Host "=== Parse-CommitGitdepsXml PowerShell Script ===" -ForegroundColor Cyan
Write-Host "Gitdeps Path: $GitdepsPath" -ForegroundColor White

if (-not (Test-Path $GitdepsPath)) {
    Get-GitdepsFromGit -OutputPath $GitdepsPath
}

try {
    # Load and parse the XML file
    $gitdepsData = Read-GitdepsXml -FilePath $GitdepsPath
    Write-Host "Loaded gitdeps with $($gitdepsData.Files.Count) files, $($gitdepsData.Blobs.Count) blobs, $($gitdepsData.Packs.Count) packs" -ForegroundColor Green
    
    if ($PSCmdlet.ParameterSetName -eq 'Find') {
        Write-Host "Finding files with pattern: $FindFileNames" -ForegroundColor Yellow
        $fileNames = Find-FileNames -GitdepsData $gitdepsData -NamePattern $FindFileNames
        
        Write-Host "Found $($fileNames.Count) files:" -ForegroundColor Green
        foreach ($fileName in $fileNames) {
            Write-Host "  $fileName"
        }
    }
    else {
        Write-Host "File Paths: $FilePaths" -ForegroundColor White
        
        foreach ($FilePath in $FilePaths) {
            Write-Host "Getting info for file $FilePath..." -ForegroundColor Yellow
            $fileInfo = Get-FileUrl -GitdepsData $gitdepsData -FilePath $FilePath
            $fileInfo
        }
    }
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Stack trace: $($_.Exception.StackTrace)" -ForegroundColor Red
    exit 1
}
