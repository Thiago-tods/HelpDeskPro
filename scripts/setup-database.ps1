#Requires -Version 5.1
<#
.SYNOPSIS
  Cria o banco local, grava segredos no User Secrets, aplica migrations e executa o seed.
.EXAMPLE
  .\scripts\setup-database.ps1 -Port 5432
#>
param(
    [int]$Port = 5432,
    [string]$DbHost = 'localhost',
    [string]$PgUser = 'postgres',
    [string]$Database = 'helpdeskpro'
)

$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root 'backend\src\HelpDeskPro.Api'

$psql = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $psql) { throw 'psql.exe não encontrado em C:\Program Files\PostgreSQL.' }

$secure = Read-Host "Senha do usuário '$PgUser' no PostgreSQL (porta $Port)" -AsSecureString
$pgPassword = [System.Net.NetworkCredential]::new('', $secure).Password
$env:PGPASSWORD = $pgPassword

try {
    $exists = & $psql.FullName -h $DbHost -p $Port -U $PgUser -tAc "SELECT 1 FROM pg_database WHERE datname='$Database'"
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao conectar ao PostgreSQL. Verifique porta e senha.' }
    if ($exists -ne '1') {
        & $psql.FullName -h $DbHost -p $Port -U $PgUser -c "CREATE DATABASE $Database ENCODING 'UTF8'"
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar o banco.' }
    }
    else { Write-Host "Banco '$Database' já existe. Mantendo." }
}
finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }

# Senha inicial aleatória dos usuários de demonstração (fica só no User Secrets).
$bytes = New-Object byte[] 18
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$seedPassword = 'Hd!' + [Convert]::ToBase64String($bytes).Replace('/', 'x').Replace('+', 'y')

Push-Location $api
try {
    dotnet user-secrets init | Out-Null
    $conn = "Host=$DbHost;Port=$Port;Database=$Database;Username=$PgUser;Password=$pgPassword"
    dotnet user-secrets set 'ConnectionStrings:Default' $conn | Out-Null
    $existing = dotnet user-secrets list | Select-String '^Seed:InitialPassword'
    if (-not $existing) { dotnet user-secrets set 'Seed:InitialPassword' $seedPassword | Out-Null }

    if (-not (dotnet user-secrets list | Select-String '^Jwt:Key')) {
        $k = New-Object byte[] 48
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($k)
        dotnet user-secrets set 'Jwt:Key' ([Convert]::ToBase64String($k)) | Out-Null
    }

    dotnet run --no-launch-profile -- migrate
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao aplicar migrations.' }
    dotnet run --no-launch-profile -- seed
    if ($LASTEXITCODE -ne 0) { throw 'Falha no seed.' }
}
finally { Pop-Location }

Write-Host ''
Write-Host 'Concluído. Para ver a senha dos usuários de demonstração:'
Write-Host "  cd backend\src\HelpDeskPro.Api; dotnet user-secrets list"
