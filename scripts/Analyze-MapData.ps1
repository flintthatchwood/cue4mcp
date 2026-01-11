param(
    [Parameter(Mandatory=$false)]
    [string]$MapPath = ".work/ark",
    
    [Parameter(Mandatory=$false)]
    [string]$OutputPath = ".work/analysis.json",
    
    [Parameter(Mandatory=$false)]
    [string]$MapPattern = "*.json"
)

# Create output directory if it doesn't exist
$OutputDirectory = Split-Path $OutputPath
if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

Write-Host "Starting map data analysis..." -ForegroundColor Cyan
Write-Host "Map path: $MapPath" -ForegroundColor Gray
Write-Host "Output path: $OutputPath" -ForegroundColor Gray

# Find all map files
$mapFiles = Get-ChildItem -Path $MapPath -Recurse -File | 
    Where-Object { $_.FullName -like $MapPattern }

Write-Host "Found $($mapFiles.Count) map files to analyze" -ForegroundColor Green

$processedCount = 0
$exportTypeStats = @{}

foreach ($mapFile in $mapFiles) {
    $processedCount++
    
    if ($processedCount % 100 -eq 0) {
        Write-Host "[$processedCount/$($mapFiles.Count)] Processing..." -ForegroundColor Yellow
    }
    
    try {
        # Read and parse JSON
        $mapData = Get-Content -Path $mapFile.FullName -Raw | ConvertFrom-Json -AsHashtable
        
        $exportGroups = $mapData.Exports | Group-Object -Property Type

        # Process each export
        foreach ($exportGroup in $exportGroups) {
            $exportType = $exportGroup.Name
            if (!$exportType) {
                $exportType = "<Unknown>"
            }
            
            # Track export type in this map
            if (-not $exportTypeStats.ContainsKey($exportType)) {
                $exportTypeStat = $exportTypeStats[$exportType] = @{ FileCount = 0; ExportCount = 0; Keys = @{}; Properties = @{} }
            } 

            $exportTypeStat = $exportTypeStats[$exportType]
            $exportTypeStat.FileCount++
            $exportTypeStat.ExportCount += $exportGroup.Count

            foreach ($export in $exportGroup.Group) {
                foreach($key in $export.Keys) {
                    if (-not $exportTypeStat.Keys.ContainsKey($key)) {
                        $exportTypeStat.Keys[$key] = [ordered]@{ Count = 1 }
                        if ($key -ne 'Properties') {
                            $exportTypeStat.Keys[$key].Sample = $export[$key]
                        }   
                    } else {
                        $exportTypeStat.Keys[$key].Count++
                    }
                }
                
                foreach ($key in $export.Properties.Keys) {
                    if (-not $exportTypeStat.Properties.ContainsKey($key)) {
                        $exportTypeStat.Properties[$key] = [ordered]@{ Count = 1; Sample = $export.Properties[$key] }
                    } else {
                        $exportTypeStat.Properties[$key].Count++
                    }
                }
            }
        }       
    } catch {
        Write-Host "  ERROR processing $relativePath : $_" -ForegroundColor Red
    }
}

Write-Host "`nGenerating overall summary..." -ForegroundColor Cyan

$exportTypes = $exportTypeStats.Keys | ForEach-Object {
    $exportType = $_
    $stat = $exportTypeStats[$_]

    $keys = $stat.Keys.Keys | ForEach-Object {
        [ordered]@{ Name = $_; Count = $stat.Keys[$_].Count; Sample = $stat.Keys[$_].Sample }
    }

    $properties = $stat.Properties.Keys | ForEach-Object {
        [ordered]@{ Name = $_; Count = $stat.Properties[$_].Count; Sample = $stat.Properties[$_].Sample }
    }
    
    [ordered]@{
        ExportType = $exportType
        FileCount = $stat.FileCount
        ExportCount = $stat.ExportCount
        Keys = $keys | Sort-Object { $_.Count } -Descending 
        Properties = $properties | Sort-Object { $_.Count } -Descending 
    }
}

# Create summary report
$summary = [ordered]@{
    TotalMapsAnalyzed = $processedCount
    TotalExportTypes = $exportTypeStats.Count
    ExportTypes = $exportTypes | Sort-Object { $_.FileCount } -Descending
}

# Save overall summary
$summary | ConvertTo-Json -Depth 10 | Set-Content -Path $OutputPath
Write-Host "Overall summary saved to: $OutputPath" -ForegroundColor Green
