# Progresso

## Fase 0 - Ambiente
Concluída. .NET 10 SDK instalado via winget (10.0.404).

## Fase 1 - Estrutura
Solução .NET, 6 projetos, frontend Vite + React 19 + TS strict.

## Versões
Node 26.13.0, npm 11.20.0, .NET SDK 10.0.404, React 19.3.1, Vite 8.3.8, TypeScript 7.0.2, Tailwind 4.3.5. Lista completa e atual: README.md.

## Fase 2 - Banco e domínio
Entidades, máquina de estados, SLA, DbContext, Identity, migration InitialCreate, seed, 29 testes unitários (aprovados).
Pendente: executar `scripts/setup-database.ps1` para criar o banco e rodar o seed (requer senha do PostgreSQL).
Pacotes: EF Core 10.0.15, Npgsql.EFCore.PostgreSQL 10.0.3, dotnet-ef 10.0.15.

Banco criado em localhost:5432 (PostgreSQL 18). Migration e seed aplicados e validados: 9 usuarios, 25 chamados, 56 mensagens, 77 historicos. Seed re-executado sem duplicar.

## Fase 3 - Autenticacao e autorizacao
Identity + JWT + refresh rotativo em cookie HttpOnly, politicas, CORS, rate limit, Problem Details. Ver docs/SECURITY.md.

## Fase 4 - Chamados
CRUD de chamados com escopo por papel, status/atribuicao/historico, categorias, prioridades, equipes e membros. Ver docs/API.md. Migrations AddCategoryDefaultTeam e AddHistorySequence aplicadas.

## Fase 5 - Comunicacao
Mensagens e notas internas, anexos validados, notificacoes persistidas, hub SignalR com grupos definidos pelo servidor. 183 testes (84 unit + 99 integracao). Smoke test com Kestrel real confirmou negociacao do hub com token na query.

## Fase 6 - SLA e dashboard
Estado de SLA calculado no backend, avisos e violacoes via monitor (worker), dashboard por escopo, relatorios de SLA/agentes, exportacao CSV segura e auditoria. Migration SlaTracking aplicada (com backfill). 242 testes (111 unit + 131 integracao). Worker validado em Kestrel real: 18 violacoes registradas nos dados de demonstracao.

## Fase 7 - Frontend
React 19 + TS strict + Tailwind (tokens claro/escuro) + Radix (Dialog/Dropdown) + React Query + RHF/Zod + Recharts + SignalR.
Telas: login/recuperacao/redefinicao; solicitante (inicio, abrir, meus chamados, detalhe, notificacoes, perfil); agente (painel, fila, atribuidos,
kanban, detalhe com notas internas e historico); admin (visao geral, usuarios, equipes, categorias, prioridades, SLA, relatorios, auditoria, configuracoes);
403/404/erro. Backend ganhou gestao de usuarios, perfil/troca de senha e /settings (somente leitura).
Testes: 65 de frontend (Vitest) e 256 de backend. E2E (Playwright) e acessibilidade automatizada ficam para a Fase 8.

## Fase 8 - Testes e seguranca
Matriz de permissoes de todos os endpoints, testes de token/entrada hostil/cabecalhos, Swagger UI, E2E Playwright (45) com axe nos dois temas,
teclado e responsivo. Correcoes: invalidacao imediata de token (desativacao/papel), encoder JSON, cabecalhos de seguranca, contraste, foco, semantica de menus,
transbordo no celular. Resultado: 401 testes (282 backend, 74 frontend, 45 E2E), 0 falhas, 0 vulnerabilidades de dependencias.

## Fase 9 - Documentacao e portfolio
README completo (apresentacao, screenshots reais, funcionalidades, stack com versoes, instalacao, configuracao, usuarios de demonstracao, testes, limitacoes),
ARCHITECTURE.md (diagramas, decisoes, trade-offs), DATABASE.md (ER, tabelas, migrations), AGENTS.md atualizado, scripts start-dev/test-all e geracao de screenshots.
Revisao de qualidade: lint sem erros, dotnet format limpo, sem segredos/TODOs, arquivos do template removidos.

## Estado final
Todas as 9 fases concluidas. Pendencias conscientes: ver "Limitacoes conhecidas" no README e "Riscos residuais" em SECURITY.md.
Repositorio Git inicializado com um commit inicial (branch main). Nenhum remote configurado e nenhum push realizado (por regra do projeto).