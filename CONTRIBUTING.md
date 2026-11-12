# Contribuindo

Este é um projeto de portfólio. Sugestões e correções são bem-vindas por issue ou pull request.

## Ambiente
Siga o [README](README.md) (Windows + PowerShell, .NET SDK 10, Node, PostgreSQL local; sem Docker).

```powershell
.\scripts\setup-database.ps1 -Port 5432
.\scripts\start-dev.ps1
```

Se a política de execução do PowerShell bloquear os scripts `.ps1`, libere somente a sessão atual (vale apenas para essa janela e some ao fechá-la;
não altere a política global de forma permanente):

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

## Antes de abrir um pull request
```powershell
.\scripts\test-all.ps1            # backend + frontend + E2E
.\scripts\test-all.ps1 -SkipE2E   # sem o Playwright
dotnet format backend\HelpDeskPro.sln --verify-no-changes
```

- Endpoint novo: autorização explícita e uma linha na matriz de `PermissionMatrixTests` (o teste falha se faltar).
- Interface nova: mantenha contraste nos dois temas, foco visível e navegação por teclado (o E2E com axe verifica).
- Atualize `docs/` quando comportamento, API ou segurança mudarem.
- Sem segredos no código; use User Secrets ou variáveis de ambiente.

Convenções completas em [AGENTS.md](AGENTS.md).
