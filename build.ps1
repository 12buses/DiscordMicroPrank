[CmdletBinding()]
param(
    [switch]$SkipSelfContained
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot

try {
    if (Test-Path dist) {
        Remove-Item dist -Recurse -Force
    }

    Write-Host 'Publishing framework-dependent build...' -ForegroundColor Cyan
    dotnet publish `
        -c Release `
        -r win-x64 `
        --self-contained false `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o dist\framework-dependent

    if (-not $SkipSelfContained) {
        Write-Host 'Publishing self-contained build...' -ForegroundColor Cyan
        dotnet publish `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -p:DebugType=None `
            -p:DebugSymbols=false `
            -o dist\self-contained
    }

    Write-Host ''
    Write-Host 'Done:' -ForegroundColor Green
    Get-ChildItem dist -Recurse -Filter MicPrank.exe |
        Select-Object FullName, @{ Name = 'SizeMB'; Expression = { [math]::Round($_.Length / 1MB, 1) } } |
        Format-Table -AutoSize
}
finally {
    Pop-Location
}
