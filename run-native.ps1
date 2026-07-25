[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet run --project (Join-Path $projectRoot 'src/Empire.Game/Empire.Game.csproj') --configuration $Configuration
exit $LASTEXITCODE
