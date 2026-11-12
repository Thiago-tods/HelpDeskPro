# Desenvolvimento (Windows / PowerShell)

## Banco e seed
```powershell
.\scripts\setup-database.ps1 -Port 5432
```
Pede a senha do PostgreSQL, cria o banco `helpdeskpro`, grava `ConnectionStrings:Default` e
`Seed:InitialPassword` (aleatória) no User Secrets, aplica migrations e roda o seed (idempotente).

Ver segredos: `cd backend\src\HelpDeskPro.Api; dotnet user-secrets list`

## Comandos manuais
```powershell
cd backend\src\HelpDeskPro.Api
dotnet run --no-launch-profile -- migrate
dotnet run --no-launch-profile -- seed
dotnet run            # http://localhost:5080
cd ..\..\..\frontend; npm run dev   # http://localhost:5173
```
Testes: `dotnet test backend\HelpDeskPro.sln` e `npm test` no frontend.

## Configurações úteis (User Secrets / variáveis de ambiente)
| Chave | Padrão | Uso |
|---|---|---|
| `Sla:CheckIntervalSeconds` | 60 | Intervalo do monitor de SLA (0 desliga). Variável: `Sla__CheckIntervalSeconds` |
| `Attachments:StoragePath` | `%LOCALAPPDATA%\HelpDeskPro\attachments` | Pasta dos anexos |
| `Attachments:MaxBytes` / `MaxPerTicket` | 10 MB / 20 | Limites de upload |
| `Cors:AllowedOrigins` | `http://localhost:5173` | Origens do frontend |
| `RateLimiting:AuthPermitPerMinute` | 10 | Limite por IP nos endpoints de auth |

O primeiro start com o worker ligado registra as violações de SLA dos chamados de demonstração antigos (esperado).
## E2E e Swagger
```powershell
.\scripts\e2e.ps1          # banco helpdeskpro_e2e recriado, senha efêmera; ver docs/TESTING.md
```
Com a API em Development: Swagger UI em `http://localhost:5080/swagger` (use o `accessToken` do login em "Authorize").
## Scripts (`scripts\`)
| Script | Função |
|---|---|
| `setup-database.ps1 -Port 5432` | Cria o banco, grava segredos no User Secrets, aplica migrations e seed |
| `start-dev.ps1` | Sobe API (:5080) e frontend (:5173) em janelas separadas |
| `test-all.ps1 [-SkipE2E]` | Backend + frontend (tipos, testes, build) + E2E |
| `e2e.ps1 [arquivos]` | Playwright contra o banco `helpdeskpro_e2e` (recriado a cada execução) |

## Regenerar os screenshots do README
```powershell
$env:CAPTURE_SCREENSHOTS = '1'; .\scripts\e2e.ps1 screenshots; Remove-Item Env:CAPTURE_SCREENSHOTS
```
As imagens vão para `docs\screenshots` (dados do seed, 1440x900 e 390x844).