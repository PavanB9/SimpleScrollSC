# Builds and runs ScrollShot using the repo-local .NET SDK.
# Output exe is placed under: ScrollShot\bin\Release\net8.0-windows\win-x64\publish\ScrollShot.exe

$ErrorActionPreference = 'Stop'

Set-Location -LiteralPath $PSScriptRoot

$dotnet = Join-Path $PSScriptRoot '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
  throw "Repo-local dotnet not found at: $dotnet"
}

Write-Host "Publishing ScrollShot (Release)..." -ForegroundColor Cyan
& $dotnet publish '.\ScrollShot\ScrollShot.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

$exe = Join-Path $PSScriptRoot 'ScrollShot\bin\Release\net8.0-windows\win-x64\publish\ScrollShot.exe'
if (-not (Test-Path -LiteralPath $exe)) {
  throw "Publish succeeded but exe not found at: $exe"
}

Write-Host "Starting: $exe" -ForegroundColor Green
Start-Process -FilePath $exe
