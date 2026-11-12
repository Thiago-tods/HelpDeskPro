#Requires -Version 5.1
<#
.SYNOPSIS
  Executa os testes E2E (Playwright) contra um banco exclusivo `helpdeskpro_e2e`, recriado a cada execução.
.DESCRIPTION
  - Lê a connection string do User Secrets da API e troca apenas o nome do banco (o banco de demonstração não é tocado).
  - Gera uma senha aleatória para os usuários de demonstração (só existe nesta execução).
  - Aplica migrations, roda o seed, compila a API e chama o Playwright.
.EXAMPLE
  .\scripts\e2e.ps1
  .\scripts\e2e.ps1 --grep "fluxo"         # argumentos extras vão para o Playwright
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Database = 'helpdeskpro_e2e',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$PlaywrightArgs
)

$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root 'backend\src\HelpDeskPro.Api'
$frontend = Join-Path $root 'frontend'

# 1) Connection string base (User Secrets) -> banco de E2E
Push-Location $api
$line = dotnet user-secrets list | Select-String '^ConnectionStrings:Default'
Pop-Location
if (-not $line) { throw 'ConnectionStrings:Default não encontrada no User Secrets. Rode scripts\setup-database.ps1 antes.' }
$base = ($line.Line -replace '^[^=]+=\s*', '')
$parts = @{}
$base -split ';' | ForEach-Object { $kv = $_ -split '=', 2; if ($kv.Count -eq 2) { $parts[$kv[0]] = $kv[1] } }
$e2eConnection = ($base -replace 'Database=[^;]+', "Database=$Database")

# 2) Recria o banco de E2E
$psql = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $psql) { throw 'psql.exe não encontrado.' }
$env:PGPASSWORD = $parts['Password']
$env:PGOPTIONS = '-c client_min_messages=warning'   # evita que avisos do psql (stderr) abortem o script
try {
    & $psql.FullName -h $parts['Host'] -p $parts['Port'] -U $parts['Username'] -d postgres -c "DROP DATABASE IF EXISTS $Database WITH (FORCE)" | Out-Null
    & $psql.FullName -h $parts['Host'] -p $parts['Port'] -U $parts['Username'] -d postgres -c "CREATE DATABASE $Database ENCODING 'UTF8'" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar o banco de E2E.' }
}
finally { Remove-Item Env:PGPASSWORD, Env:PGOPTIONS -ErrorAction SilentlyContinue }

# 3) Senha efêmera e variáveis de ambiente desta execução
$bytes = New-Object byte[] 18
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$env:E2E_PASSWORD = 'E2e!' + [Convert]::ToBase64String($bytes).Replace('/', 'x').Replace('+', 'y')
$env:E2E_CONNECTION = $e2eConnection
$env:ConnectionStrings__Default = $e2eConnection
$env:Seed__InitialPassword = $env:E2E_PASSWORD
$env:ASPNETCORE_ENVIRONMENT = 'Development'

try {
    # 4) Build, migrations e seed no banco de E2E
    dotnet build (Join-Path $api 'HelpDeskPro.Api.csproj') --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Falha no build da API.' }
    Push-Location $api
    try {
        dotnet run --no-build --no-launch-profile -- migrate
        if ($LASTEXITCODE -ne 0) { throw 'Falha nas migrations.' }
        dotnet run --no-build --no-launch-profile -- seed
        if ($LASTEXITCODE -ne 0) { throw 'Falha no seed.' }
    }
    finally { Pop-Location }

    # 5) Playwright
    Push-Location $frontend
    try {
        $ErrorActionPreference = 'Continue'   # avisos em stderr do Node não devem abortar; vale o código de saída
        npx playwright test @PlaywrightArgs
        $code = $LASTEXITCODE
    }
    finally { Pop-Location }
    if ($code -ne 0) { Write-Host "E2E terminou com falhas (código $code). Relatório: frontend\playwright-report" -ForegroundColor Yellow }
    exit $code
}
finally {
    foreach ($name in 'E2E_PASSWORD', 'E2E_CONNECTION', 'ConnectionStrings__Default', 'Seed__InitialPassword') {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
}
