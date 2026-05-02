# Builds and runs SimpleScrollSC using the repo-local .NET SDK.
# Output exe is placed under: SimpleScrollSC\bin\Release\net8.0-windows\win-x64\publish\SimpleScrollSC.exe

$ErrorActionPreference = 'Stop'

Set-Location -LiteralPath $PSScriptRoot

$dotnet = Join-Path $PSScriptRoot '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
  throw "Repo-local dotnet not found at: $dotnet"
}

Write-Host "Publishing SimpleScrollSC (Release)..." -ForegroundColor Cyan
& $dotnet publish '.\SimpleScrollSC\SimpleScrollSC.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

$exe = Join-Path $PSScriptRoot 'SimpleScrollSC\bin\Release\net8.0-windows\win-x64\publish\SimpleScrollSC.exe'
if (-not (Test-Path -LiteralPath $exe)) {
  throw "Publish succeeded but exe not found at: $exe"
}

Write-Host "Starting: $exe" -ForegroundColor Green
Start-Process -FilePath $exe
