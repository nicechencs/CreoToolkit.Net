#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$CatalogPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$CatalogPath = (Resolve-Path -LiteralPath $CatalogPath).Path
$SamplesRoot = Split-Path -Parent $CatalogPath
[xml]$catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding UTF8
$modules = @($catalog.SelectNodes('/Project/ItemGroup/CtkSampleModule'))
$apps = @($catalog.SelectNodes('/Project/ItemGroup/CtkSampleApp'))

function Assert-Unique {
    param([object[]]$Nodes, [string]$Attribute, [string]$Kind)
    $duplicates = @(
        $Nodes | Group-Object { [string]$_.GetAttribute($Attribute) } |
            Where-Object { [string]::IsNullOrWhiteSpace($_.Name) -or $_.Count -ne 1 }
    )
    if ($duplicates.Count -ne 0) {
        throw "$Kind has missing/duplicate '$Attribute': $($duplicates.Name -join ', ')"
    }
}

if ($modules.Count -eq 0) { throw 'Sample catalog contains no CtkSampleModule items.' }
if ($apps.Count -eq 0) { throw 'Sample catalog contains no CtkSampleApp items.' }
Assert-Unique $modules 'Include' 'CtkSampleModule'
Assert-Unique $modules 'Order' 'CtkSampleModule'
Assert-Unique $modules 'ProjectRelativePath' 'CtkSampleModule'
Assert-Unique $modules 'RegistrationType' 'CtkSampleModule'
Assert-Unique $apps 'Include' 'CtkSampleApp'
Assert-Unique $apps 'Order' 'CtkSampleApp'
Assert-Unique $apps 'ProjectRelativePath' 'CtkSampleApp'
Assert-Unique $apps 'Type' 'CtkSampleApp'
Assert-Unique $apps 'Assembly' 'CtkSampleApp'

$typePattern = '^(?:[A-Za-z_][A-Za-z0-9_]*\.)+[A-Za-z_][A-Za-z0-9_]*$'
foreach ($module in $modules) {
    $relative = [string]$module.ProjectRelativePath
    $type = [string]$module.RegistrationType
    $arguments = [string]$module.RegistrationArguments
    if ([System.IO.Path]::IsPathRooted($relative)) { throw "Module path must be relative: $relative" }
    $project = [System.IO.Path]::GetFullPath((Join-Path $SamplesRoot $relative))
    if (-not $project.StartsWith($SamplesRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Module project is missing or outside samples/: $relative"
    }
    if ($type -notmatch $typePattern) { throw "Illegal registration type: $type" }
    if ([string]::IsNullOrWhiteSpace($arguments) -or $arguments -match '[;\r\n{}]') {
        throw "Illegal registration arguments for $($module.Include): $arguments"
    }
}

foreach ($app in $apps) {
    $relative = [string]$app.ProjectRelativePath
    $type = [string]$app.Type
    $assembly = [string]$app.Assembly
    $msgFile = [string]$app.MsgFile
    if ([System.IO.Path]::IsPathRooted($relative)) { throw "App path must be relative: $relative" }
    $project = [System.IO.Path]::GetFullPath((Join-Path $SamplesRoot $relative))
    if (-not $project.StartsWith($SamplesRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "App project is missing or outside samples/: $relative"
    }
    if ($type -notmatch $typePattern) { throw "Illegal app type: $type" }
    if ($assembly -notmatch '^[A-Za-z0-9_.-]+\.dll$') { throw "Illegal app assembly: $assembly" }
    if ($msgFile -notmatch '^[A-Za-z0-9_.-]+$') { throw "Illegal app msgFile: $msgFile" }

    [xml]$projectXml = Get-Content -LiteralPath $project -Raw -Encoding UTF8
    $assemblyNameNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/AssemblyName')
    $declaredAssembly = if ($null -eq $assemblyNameNode) { '' } else { [string]$assemblyNameNode.InnerText }
    if ([string]::IsNullOrWhiteSpace($declaredAssembly)) {
        $declaredAssembly = [System.IO.Path]::GetFileNameWithoutExtension($project)
    }
    if ($assembly -ne "$declaredAssembly.dll") {
        throw "App assembly '$assembly' does not match project output '$declaredAssembly.dll'."
    }
}

Write-Host "Sample catalog PASS ($($modules.Count) modules, $($apps.Count) apps)."
