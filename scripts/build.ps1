# build.ps1 - Build all projects to the build/ directory
# Core -> build/bin, Plugins -> build/plugins/<PluginName>

$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot/.."

$CoreProject = "$Root/WoofBot.Core/WoofBot.Core.csproj"
$PluginProjects = Get-ChildItem "$Root/plugins" -Recurse -Filter "*.csproj"

$BuildRoot = "$Root/build"

Write-Host "=== Building WoofBot.Core ===" -ForegroundColor Cyan
dotnet build $CoreProject -c Release -o "$BuildRoot/bin"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($plugin in $PluginProjects) {
    $pluginName = $plugin.BaseName
    Write-Host "=== Building $pluginName ===" -ForegroundColor Cyan
    dotnet build $plugin.FullName -c Release -o "$BuildRoot/plugins/$pluginName"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "=== Build completed ===" -ForegroundColor Green
