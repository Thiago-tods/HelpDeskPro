#Requires -Version 5.1
<#
.SYNOPSIS
  Sobe API (http://localhost:5080) e frontend (http://localhost:5173) para desenvolvimento local, em janelas separadas.
.DESCRIPTION
  Pré-requisito: banco criado com scripts\setup-database.ps1. Feche as janelas (ou use Ctrl+C) para encerrar.
#>
[CmdletBinding(PositionalBinding = $false)]
param([switch]$NoBrowser)

$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$root = Split-Path -Parent $PSScriptRoot

foreach ($port in 5080, 5173) {
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
        throw "A porta $port já está em uso. Encerre o processo que a utiliza e tente de novo."
    }
}

$api = Join-Path $root 'backend\src\HelpDeskPro.Api'
$web = Join-Path $root 'frontend'

if (-not (Test-Path (Join-Path $web 'node_modules'))) {
    Write-Host 'Instalando dependências do frontend (primeira execução)...'
    Push-Location $web; npm install; Pop-Location
}

Write-Host 'Iniciando a API...'
Start-Process powershell -ArgumentList '-NoExit', '-Command', "`$env:ASPNETCORE_ENVIRONMENT='Development'; Set-Location '$api'; dotnet run --no-launch-profile --urls http://localhost:5080"

Write-Host 'Iniciando o frontend...'
Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$web'; npm run dev"

Write-Host ''
Write-Host 'API:      http://localhost:5080  (Swagger: /swagger)'
Write-Host 'Frontend: http://localhost:5173'
Write-Host 'Senha dos usuários de demonstração: cd backend\src\HelpDeskPro.Api; dotnet user-secrets list'

if (-not $NoBrowser) {
    Start-Sleep -Seconds 8
    Start-Process 'http://localhost:5173'
}
