#Requires -Version 5.1
<#
.SYNOPSIS
  Executa todas as camadas de teste: backend (xUnit), frontend (tsc + Vitest + build) e E2E (Playwright).
.EXAMPLE
  .\scripts\test-all.ps1            # tudo
  .\scripts\test-all.ps1 -SkipE2E   # sem o Playwright (mais rápido)
#>
[CmdletBinding(PositionalBinding = $false)]
param([switch]$SkipE2E)

$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$root = Split-Path -Parent $PSScriptRoot
$failed = @()

function Step($name, [scriptblock]$body) {
    Write-Host "`n=== $name ===" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { $script:failed += $name }
}

Step 'Backend (unitários + integração)' { Push-Location (Join-Path $root 'backend'); dotnet test HelpDeskPro.sln --nologo; Pop-Location }
Step 'Frontend: tipos' { Push-Location (Join-Path $root 'frontend'); npx tsc -b; Pop-Location }
Step 'Frontend: testes' { Push-Location (Join-Path $root 'frontend'); npx vitest run; Pop-Location }
Step 'Frontend: build' { Push-Location (Join-Path $root 'frontend'); npm run build --silent; Pop-Location }
if (-not $SkipE2E) { Step 'E2E (Playwright)' { & (Join-Path $PSScriptRoot 'e2e.ps1') } }

Write-Host ''
if ($failed.Count -eq 0) { Write-Host 'Todas as camadas passaram.' -ForegroundColor Green; exit 0 }
Write-Host ('Falharam: ' + ($failed -join ', ')) -ForegroundColor Red
exit 1
