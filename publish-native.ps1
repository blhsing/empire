[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime = 'win-x64'
)

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputPath = Join-Path $projectRoot "artifacts/$Runtime"

dotnet publish (Join-Path $projectRoot 'src/Empire.Game/Empire.Game.csproj') `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $outputPath `
    -p:PublishReadyToRun=false `
    -p:TieredCompilation=false

if ($LASTEXITCODE -eq 0) {
    Write-Host "原生版已輸出至：$outputPath"
}
exit $LASTEXITCODE
