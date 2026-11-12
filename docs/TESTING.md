# Testes

Resultado da última rodada completa: **401 testes, 0 falhas** (backend 282, frontend 74, E2E 45). Os 2 testes "ignorados" do Playwright são o gerador de screenshots, que só roda com `CAPTURE_SCREENSHOTS=1`..

| Camada | Ferramenta | Qtde | Comando |
|---|---|---:|---|
| Backend — unitários | xUnit | 111 | `dotnet test backend\HelpDeskPro.sln` |
| Backend — integração | xUnit + API real + PostgreSQL | 171 | (idem) |
| Frontend — componentes/telas | Vitest + Testing Library | 74 | `cd frontend; npm test` |
| E2E, segurança, acessibilidade, responsivo | Playwright + axe-core | 45 | `.\scripts\e2e.ps1` |

## Backend
- **Unitários:** máquina de estados, SLA (aviso, violação, estado, atraso), regras de autorização (`TicketAccess`), validação real de anexos, CSV seguro, fuso de Brasília.
- **Integração:** sobem a API de verdade contra o banco `helpdeskpro_test` (derivado da connection string do User Secrets; criado
  automaticamente; o banco de demonstração não é tocado). Cada teste cria os próprios usuários (`@teste.example`).
  Cobrem autenticação/refresh, CRUD, filtros e paginação, permissões, notas internas, anexos, notificações, SignalR, SLA, dashboard e relatórios.
- **Auditoria de segurança** (`SecurityAuditTests.cs`):
  - *Matriz de permissões:* inventário de **todos** os endpoints com o acesso esperado. Endpoint novo sem classificação quebra o teste.
    Valida 401 para anônimos em todas as rotas protegidas e 403 de solicitante/agente nas rotas administrativas.
  - *Tokens:* chave errada, emissor/audiência errados, expirado, `alg: none`, payload adulterado, papel divergente do cadastro,
    usuário desativado (perde acesso na hora) e troca de papel (token antigo cai).
  - *Entrada hostil:* over-posting, SQL injection, JSON malformado/tipos errados/payload gigante, ids inválidos, vazamento em erros.
  - *Transporte:* cabeçalhos de segurança, `no-store` em autenticação, Swagger só em Development, cookie de refresh.

## Credenciais de teste
Os testes de integração usam a constante fictícia `ApiFactory.TestPassword` para usuários de teste.
É um valor **exclusivo de teste**: não dá acesso a nenhum ambiente real e só vale contra o banco descartável `helpdeskpro_test`
(usado pela própria suíte) e contra usuários fictícios (`@teste.example`/`@nexora.example`). Ferramentas de varredura de segredos
podem sinalizá-lo como falso positivo. O desenvolvimento e o E2E **não** o usam: a senha de demonstração é aleatória e fica em
User Secrets (ou existe só durante a execução do `e2e.ps1`). Não reutilize esse valor fora dos testes.

## Frontend (Vitest)
API client (renovação de sessão, anti-concorrência, FormData, Problem Details), login, tema, menu por papel, guards 403/404/redirecionamento,
filtros combináveis com debounce, formulário de abertura (validação, anexos, SLA), permissões visuais do detalhe (guiadas pelo servidor),
confirmação de ações destrutivas, Kanban (arrastar, seletor por teclado, regras negadas pelo servidor).
Mocks existem **somente** nos testes.

## E2E (Playwright)
```powershell
.\scripts\e2e.ps1                      # tudo
.\scripts\e2e.ps1 core-flow security   # só alguns arquivos
```
O script recria o banco `helpdeskpro_e2e`, gera uma senha de demonstração aleatória (existe só durante a execução), aplica migrations e seed,
compila a API e executa o Playwright. A API sobe na porta 5090 e o **build de produção do frontend** é servido por `vite preview` na 5183 (sem conflito com o ambiente de desenvolvimento).
Relatório HTML em `frontend\playwright-report` (ignorado pelo Git). Execução sequencial: os cenários dividem o banco e usam tempo real.

| Arquivo | O que prova |
|---|---|
| `core-flow` | **Cenário obrigatório** com três navegadores isolados: solicitante abre → agente recebe em tempo real (fila + notificação) → assume → responde (e deixa nota interna) → solicitante recebe só a pública → agente resolve → solicitante acompanha → administrador vê os indicadores. Também reabertura e cancelamento com confirmação |
| `security` | IDOR (solicitante × solicitante, agente × outra equipe), nota interna invisível na UI e na API, 403 por papel, usuário desativado não autentica, refresh revogado, "encerrar todas as sessões", mensagem de login genérica, XSS armazenado |
| `a11y` | axe-core (WCAG 2.0/2.1/2.2 A e AA) em **todas as telas dos 3 papéis, nos temas claro e escuro**, inclusive menus e modais abertos e estados de erro |
| `keyboard` | login só com teclado, skip link, foco visível, foco preso em modal e devolvido ao fechar, abas WAI-ARIA, menus, tema, erros anunciados |
| `responsive` | sem rolagem horizontal da página em 375, 768 e 1366 px nas telas principais; menu em gaveta no celular |

## Histórico de estabilidade
Numa auditoria, o cenário `core-flow` falhou de forma intermitente **só na primeira execução após subir o ambiente** (o botão de enviar ficava preso em "carregando").
Causa: as mutações aguardavam o término das invalidações de cache, que se cancelavam mutuamente quando eventos em tempo real invalidavam as mesmas chaves.
Correção: `useApiMutation` não aguarda mais os refetches (`frontend/src/lib/query.ts`). Depois dela, o `core-flow` passou em 2 de 2 partidas a frio
e a suíte completa passou sem falhas. Amostra pequena: se a intermitência voltar, o trace do Playwright (`retain-on-failure`) mostra as requisições pendentes.

## Limites conhecidos
- O axe automatiza cerca de 30–40% das verificações WCAG; leitura por leitor de tela e testes com usuários não foram feitos.
- Só Chromium nos testes E2E. Safari/Firefox não foram exercitados.
- O arrastar-e-soltar do Kanban é coberto no Vitest (eventos de drop) e pelo seletor por teclado no E2E, não por arraste real no navegador.
- Sem testes de carga/desempenho.