# Build the distributable EXE (single file, framework-dependent).
# Output: publish\CsvDiffChecker.exe
# Requirement on the user's PC: .NET 10 Desktop Runtime (x64).
# Usage (PowerShell, at the repository root):
#   .\scripts\publish.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'publish'

dotnet test (Join-Path $root 'CsvDiffChecker.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed. The EXE was not built.' }

dotnet publish (Join-Path $root 'src\CsvDiffChecker.App\CsvDiffChecker.App.csproj') -c Release -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Get-Item (Join-Path $out 'CsvDiffChecker.exe') | Select-Object FullName, Length
