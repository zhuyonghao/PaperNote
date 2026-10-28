param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."
dotnet publish src/PaperNote -c Release -r $Runtime --self-contained true -o "artifacts/$Runtime/PaperNote" -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Compress-Archive -Path "artifacts/$Runtime/PaperNote" -DestinationPath "artifacts/PaperNote-$Runtime.zip" -Force
