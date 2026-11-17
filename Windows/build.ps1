param(
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectRoot 'ZapretControl.csproj'
$publish = Join-Path $projectRoot 'ZapretControl'

& dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed for win-x64' }

$archive = Join-Path $projectRoot 'ZapretControl-win-x64.zip'
$packageItems = Get-ChildItem -Path $publish -Force | Where-Object { $_.Name -notin @('telegram-proxy', '.updates') }
Compress-Archive -Path $packageItems.FullName -DestinationPath $archive -CompressionLevel Optimal -Force
Get-FileHash $archive -Algorithm SHA256 | Format-List
