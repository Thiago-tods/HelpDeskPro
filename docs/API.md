# API REST (`/api/v1`)

Swagger/OpenAPI: `GET /openapi/v1.json` (somente Development). Erros seguem Problem Details
(400 validação, 401, 403, 404, 422 regra de negócio, 429, 500).
Status trafegam como códigos: `NEW`, `IN_PROGRESS`, `WAITING_FOR_USER`, `RESOLVED`, `CLOSED`, `CANCELLED`.

## Autenticação
`POST /auth/login`, `/auth/refresh`, `/auth/logout`, `/auth/logout-all`, `GET /auth/me`,
`POST /auth/forgot-password`, `/auth/reset-password`. Ver `SECURITY.md`.

## Chamados
| Método | Rota | Quem | Descrição |
|---|---|---|---|
| GET | `/tickets` | todos (escopo) | Lista paginada |
| POST | `/tickets` | todos | Abre chamado (equipe vem da categoria) |
| GET | `/tickets/{id}` | escopo | Detalhe + `allowedStatuses`, `canAssignToMe`, `canEdit` |
| PATCH | `/tickets/{id}` | conforme papel | Título, descrição, categoria, prioridade, equipe |
| PATCH | `/tickets/{id}/status` | conforme papel | `{ "status": "RESOLVED" }` |
| PATCH | `/tickets/{id}/assignment` | ADMIN / AGENT | `{ "assignedToId": "guid" \| null }` |
| GET | `/tickets/{id}/history` | escopo | Histórico cronológico |

Filtros de `GET /tickets`: `search`, `status` (lista por vírgula), `priorityId`, `categoryId`, `teamId`,
`assignedToId`, `requesterId` (só equipe), `unassigned`, `mine`, `createdFrom`, `createdTo`.
Ordenação: `sortBy` = createdAt | updatedAt | ticketNumber | status | priority | title; `sortDir` = asc | desc.
Paginação: `page` (>=1), `pageSize` (1..100). Resposta: `{ items, page, pageSize, totalCount, totalPages }`.

Chamados fora do escopo do usuário respondem **404** (não revela existência). Não há DELETE: cancelar é a exclusão lógica.

### Escopo
ADMIN vê tudo. AGENT vê chamados das equipes que integra e os atribuídos a si. REQUESTER vê apenas os próprios.

### Regras
- Status: transições validadas no domínio. NEW -> IN_PROGRESS só ocorre pela atribuição.
- Solicitante: cancela, responde (WAITING_FOR_USER -> IN_PROGRESS), reabre e fecha (RESOLVED). Edita só enquanto NEW.
- Agente: muda status/edita apenas chamados atribuídos a si. Assume chamados livres da própria equipe ou libera os seus.
- Admin: atribui a qualquer agente ativo da equipe do chamado; muda equipe.

## Catálogo
| Rota | Leitura | Escrita |
|---|---|---|
| `/categories` (+ `PUT /{id}`, `PATCH /{id}/active`) | autenticados (só ativas, exceto admin) | ADMIN |
| `/priorities` (idem; prazos em minutos) | autenticados | ADMIN |
| `/teams` (idem) | ADMIN/AGENT | ADMIN |
| `/teams/{id}/members` (GET/POST, `DELETE /{userId}`) | ADMIN; AGENT da equipe | ADMIN |
| `GET /users/agents` | ADMIN/AGENT | - |
| `GET /users` | ADMIN | - |

Alterar prazos de uma prioridade afeta só chamados criados depois.

## Mensagens
`GET/POST /tickets/{id}/messages` — corpo `{ "content": "...", "isInternal": false }`.
- Solicitante nunca recebe nem cria notas internas (filtro aplicado na consulta).
- Agente: nota interna em qualquer chamado do escopo; resposta pública só se for o responsável.
- Admin: tudo. Apenas resposta pública de agente/admin conta como primeira resposta de SLA.
- Resposta do solicitante a um chamado `WAITING_FOR_USER` o devolve a `IN_PROGRESS`.
- Chamados finalizados rejeitam mensagens (422).

## Anexos
`GET /tickets/{id}/attachments` (metadados), `POST` (multipart, campo `file`), `GET /tickets/{id}/attachments/{attachmentId}` (download).
- Extensões: png, jpg, jpeg, gif, pdf, txt, csv, docx, xlsx. Limite padrão 10 MB e 20 por chamado (`Attachments:*`).
- O conteúdo real é verificado (assinatura; UTF-8 para texto; estrutura OpenXML para docx/xlsx) e o tipo gravado é o detectado.
- Armazenamento em `%LOCALAPPDATA%\HelpDeskPro\attachments` (configurável), nome aleatório sem extensão.
- Download sempre como `attachment`, com `X-Content-Type-Options: nosniff`.
- Agente precisa ser o responsável para anexar.

## Notificações
`GET /notifications` (`unreadOnly`, `page`, `pageSize`), `GET /notifications/unread-count`, `PATCH /notifications/{id}/read`, `POST /notifications/read-all`.
Tipos: `NEW_TICKET`, `TICKET_ASSIGNED`, `NEW_MESSAGE`, `STATUS_CHANGED` (`SLA_NEAR_DUE` e `SLA_BREACHED` na Fase 6).

## Tempo real (SignalR) — `/hubs/tickets`
Autenticação por JWT. O navegador envia `?access_token=` (aceito apenas em `/hubs`); clientes .NET usam o cabeçalho.
- Grupos definidos pelo servidor: `user:{id}`, `team:{id}` (agentes, conforme vínculo) e `admins`.
- Evento `ticketEvent`: `{ type, ticketId, ticketNumber, messageId?, isInternal }`, com `type` em `ticket.created|status|assigned|updated|message|attachment`.
  Não carrega conteúdo: o cliente relê via HTTP, que aplica a autorização. Notas internas não vão ao solicitante.
- Evento `notification`: o `NotificationDto` do usuário.
- Se o hub estiver fora do ar, tudo continua por HTTP.

## SLA
Calculado no backend (horas corridas, sem feriados e sem pausa em `WAITING_FOR_USER`). Prazos vêm da prioridade no momento da abertura
(ou da última mudança de prioridade do chamado).

`slaState` (em listagem e detalhe): `ON_TRACK`, `AT_RISK` (a partir de 80% do prazo), `OVERDUE` (ativo e vencido),
`MET` / `MISSED` (resolvido/fechado dentro/fora do prazo) e `NOT_APPLICABLE` (cancelado).
`GET /tickets?overdue=true` lista só os ativos vencidos. O detalhe traz `sla.firstResponseBreachedAt` e `sla.resolutionBreachedAt`.

**Monitor** (`SlaBackgroundService`, a cada `Sla:CheckIntervalSeconds`, padrão 60; `0` desliga): avisa uma vez ao passar de 80% do prazo
(`SLA_NEAR_DUE`) e registra cada violação uma vez (`SLA_BREACHED`), com histórico de autor "Sistema" e valor `FIRST_RESPONSE` ou `RESOLUTION`.
Aviso vai ao responsável (ou à equipe, se livre); violação também aos administradores. Resposta ou resolução tardia registra a violação na hora.

## Dashboard — `GET /dashboard/summary?days=30` (1..365)
Mesmas regras de escopo da listagem (admin: tudo; agente: seu escopo; solicitante: os próprios; `byAgent` vazio para solicitante).
- Retrato atual: `total`, `open` (NEW), `inProgress`, `waitingForUser`, `resolved`, `closed`, `cancelled`, `overdue`.
- Janela de `days` dias (por data de criação, fuso America/Sao_Paulo): `avgFirstResponseMinutes`, `avgResolutionMinutes`,
  `byPriority`, `byCategory`, `byAgent` e `timeline` (um ponto por dia: criados e resolvidos).

## Relatórios e auditoria (somente ADMIN)
- `GET /reports/sla?from&to` — por prioridade: total, primeira resposta/resolução cumpridas e descumpridas, médias e `complianceRate`.
- `GET /reports/agents?from&to` — por agente: atribuídos, ativos, resolvidos, atrasados e tempo médio de resolução.
- `GET /reports/tickets/export?from&to` — CSV (`;`, UTF-8 com BOM, datas no fuso de Brasília, até 10.000 linhas).
  Células iniciadas por `= + - @` recebem apóstrofo (proteção contra injeção de fórmulas).
- `GET /audit` — histórico global paginado: `ticketNumber`, `actorId`, `action`, `from`, `to`, `page`, `pageSize`.
Período padrão: últimos 30 dias.

## Usuários, perfil e configurações
- `GET /users` (ADMIN), `POST /users` (cria com perfil e senha inicial), `PUT /users/{id}` (nome e perfil), `PATCH /users/{id}/active`.
  Regras: o administrador não altera o próprio perfil nem se desativa; mudar o perfil ou desativar encerra as sessões do usuário;
  quem deixa de ser agente sai das equipes. `GET /users/agents` (ADMIN/AGENT) lista agentes ativos.
- `PATCH /auth/me` altera o nome. `POST /auth/change-password` (`currentPassword`, `newPassword`) revoga todas as sessões e devolve uma sessão nova.
- `GET /settings` (ADMIN) — parâmetros de execução em modo somente leitura (nunca expõe segredos).

## Pendentes
Nenhum endpoint do escopo original. Próxima fase: frontend.