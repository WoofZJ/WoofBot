# publish.ps1 - Publish all projects to the publish/ directory
# Core -> publish/bin, Plugins -> publish/plugins/<PluginName>
# Default: --runtime linux-x64 --self-contained

param(
    [string]$Runtime = "linux-x64",
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot/.."
$PublishRoot = "$Root/publish"

$CoreProject = "$Root/WoofBot.Core/WoofBot.Core.csproj"
$PluginProjects = Get-ChildItem "$Root/plugins" -Recurse -Filter "*.csproj"

# Clean publish directory
if (Test-Path $PublishRoot) {
    Remove-Item $PublishRoot -Recurse -Force
}

Write-Host "=== Publishing WoofBot.Core ===" -ForegroundColor Cyan
dotnet publish $CoreProject -c Release --runtime $Runtime --self-contained:$SelfContained -o "$PublishRoot/bin"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($plugin in $PluginProjects) {
    $pluginName = $plugin.BaseName
    Write-Host "=== Publishing $pluginName ===" -ForegroundColor Cyan
    dotnet publish $plugin.FullName -c Release --runtime $Runtime --no-self-contained -o "$PublishRoot/plugins/$pluginName"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "=== Publish completed (runtime: $Runtime) ===" -ForegroundColor Green
