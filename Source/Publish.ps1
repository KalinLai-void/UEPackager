param([string[]]$Runtimes = @('win-x64','win-arm64','osx-x64','osx-arm64','linux-x64','linux-arm64'), [string]$OutputDirectory = (Join-Path $PSScriptRoot '../Releases'))
$ErrorActionPreference='Stop'
foreach ($runtime in $Runtimes) {
    $single = if ($runtime.StartsWith('win-')) { 'true' } else { 'false' }
    & dotnet publish (Join-Path $PSScriptRoot 'PackagingAutomation.csproj') -c Release -r $runtime --self-contained true "-p:PublishSingleFile=$single" -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $OutputDirectory $runtime)
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $runtime" }
}
