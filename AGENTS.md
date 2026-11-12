# AGENTS.md

Convenções para quem (pessoa ou agente de IA) altera este repositório. Visão geral do projeto: [README.md](README.md).

## Arquitetura
- Backend em C# / .NET 10, Clean Architecture enxuta: `Domain` ← `Application` ← `Infrastructure`, composta em `Api`.
  O Domain não depende de nada. Regras de negócio e a máquina de estados vivem no Domain; permissões em `TicketAccess` (Application) e no serviço.
- Nenhuma lógica de negócio no frontend: o servidor devolve o que cada usuário pode fazer (`allowedStatuses`, `canEdit`, `slaState`…).
- Todo endpoint novo precisa de autorização explícita e de uma linha na matriz de `PermissionMatrixTests` (o teste falha se faltar).
- Acesso a chamados e dados derivados sempre via `TicketScope` (proteção IDOR; fora do escopo = 404).

## Código
- Frontend: React 19, TypeScript `strict`, **aspas simples**, componentes pequenos, estado de servidor com TanStack Query, formulários com RHF + Zod.
- C#: namespaces com escopo de arquivo, `dotnet format` limpo (`dotnet format backend\HelpDeskPro.sln --verify-no-changes`). Migrations são código gerado.
- Comentários só onde explicam o "porquê" (decisões, riscos). Sem comentários redundantes, sem `console.log`/`TODO` esquecidos.
- Sem Docker, sem mocks fora de testes, sem dados hardcoded em dashboards, sem segredos no código (use User Secrets / variáveis de ambiente).
- Timestamps em UTC no banco; apresentação em `America/Sao_Paulo`, idioma pt-BR.
- Mudança de interface: manter contraste nos dois temas, foco visível e navegação por teclado (o E2E com axe cobre isso).

## Comandos (PowerShell)
```powershell
.\scripts\setup-database.ps1 -Port 5432   # banco + segredos + seed
.\scripts\start-dev.ps1                   # API :5080 + frontend :5173
.\scripts\test-all.ps1                    # backend + frontend + E2E
dotnet ef migrations add <Nome> --project backend\src\HelpDeskPro.Infrastructure --startup-project backend\src\HelpDeskPro.Api --output-dir Persistence\Migrations
```

## Antes de entregar uma mudança
1. `dotnet test backend\HelpDeskPro.sln` e `npm test` / `npx tsc -b` em `frontend` passam.
2. Se tocou em interface, fluxo ou permissões: `.\scripts\e2e.ps1` passa.
3. Documentação em `docs/` atualizada quando comportamento, API ou segurança mudarem.

## Autoria Git (regra absoluta)
- O proprietário do repositório é o **único autor** dos commits (nome `Thiago-Tods`; e-mail conforme configurado por ele).
- **Proibido** adicionar `Co-authored-by` (Claude, Claude Code, Anthropic ou similares), trailers equivalentes, ou qualquer atribuição/contribuição a ferramentas de IA em commits, metadados ou arquivos.
- Não alterar a identidade Git (global ou local).
- Não criar commits, não fazer push, não adicionar remote, não reescrever histórico (amend, rebase, force push), não criar releases nem fazer deploy sem autorização expressa do proprietário.
- Antes de qualquer commit autorizado: conferir `git config user.name`, `git config user.email`, `git status`, `git diff --check`, `git diff --cached`, trailers, autor e committer.
