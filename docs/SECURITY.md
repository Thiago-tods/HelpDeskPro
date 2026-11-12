# Segurança

## Autenticação
- Senhas: hash do ASP.NET Core Identity (PBKDF2). Mínimo 10 caracteres. Bloqueio por 5 falhas (5 min).
- Access token JWT (HS256, 15 min), claims `sub`, `email`, `name`, `role`, `jti`. Chave em User Secrets (`Jwt:Key`).
- Refresh token: 64 bytes aleatórios, entregue **somente** em cookie `hdp_refresh` (HttpOnly, SameSite=Lax,
  Path=/api/v1/auth, Secure quando HTTPS). No banco só existe o hash SHA-256.
- Rotação a cada refresh. Reapresentar um token já rotacionado revoga todas as sessões do usuário.
- Usuário desativado: login e refresh recusados; access tokens já emitidos expiram em até 15 min.
- Login devolve a mesma resposta 401 para senha errada, e-mail inexistente, usuário inativo ou bloqueado.

## CSRF
Cookie SameSite=Lax + exigência do cabeçalho `X-Requested-With` em refresh, logout e logout-all
(dispara preflight CORS, que só aceita a origem configurada).

## Autorização
Política padrão exige usuário autenticado (fallback). Políticas `Admin` e `Staff` (ADMIN/AGENT) por papel.
Regras por recurso (IDOR, notas internas) entram com os chamados (Fase 4/5).

## Outros controles
- CORS restrito a `Cors:AllowedOrigins` (padrão `http://localhost:5173`), com credenciais.
- Rate limiting: endpoints de auth, 10 req/min por IP (`RateLimiting:AuthPermitPerMinute`).
- Erros em Problem Details; erro 500 sem detalhes internos.
- Recuperação de senha em modo demonstrativo: o token só é devolvido em Development
  (`Auth:ExposeResetToken`). Em produção exigiria envio por e-mail. Reset revoga todas as sessões.

## Proteção IDOR e confidencialidade
Todo acesso a chamado, mensagem, anexo e histórico passa por `TicketScope` (admin: tudo; agente: equipes que integra
ou atribuídos; solicitante: próprios). Fora do escopo responde 404. Notificações só são lidas/alteradas pelo dono.
Notas internas são filtradas na consulta para solicitantes e excluídas dos eventos em tempo real.

## Anexos
Lista de extensões e tipos, verificação do conteúdo real, limite de tamanho e quantidade, nome de armazenamento
aleatório sem extensão, resolução de caminho com checagem de diretório raiz, nome original sanitizado,
download forçado como `attachment` e `nosniff`. Arquivos nunca são servidos como estáticos nem executados.

## SignalR
Token na query string apenas para caminhos `/hubs`. Grupos derivados do token e do banco (cliente não escolhe).
`CloseOnAuthenticationExpiration` encerra conexões quando o JWT expira; o cliente deve renovar o token e reconectar.

## Limitações conhecidas
- Mudanças de equipe de um agente só refletem nos grupos do hub após reconectar.
- Dois refresh simultâneos com o mesmo token podem ambos ter sucesso (sem controle de concorrência).
- Sem HTTPS local: cookie sem flag Secure em desenvolvimento.
## Auditoria da Fase 8
Escopo: matriz de permissões de todos os endpoints, tokens, entrada hostil, cabeçalhos, dependências e acessibilidade.

**Achados corrigidos**
1. *Token de acesso sobrevivia à desativação/mudança de papel por até 15 min.* Agora cada requisição valida no banco que a conta está ativa
   e que o papel do token coincide com o cadastro (`OnTokenValidated`); vale também para o hub SignalR.
2. *JSON não escapava `<`, `>` e `&`.* Não era explorável (`application/json` + `nosniff`), mas o encoder foi trocado por defesa em profundidade.
3. *Faltavam cabeçalhos de segurança.* Todas as respostas levam `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`,
   `Permissions-Policy`, CSP restritiva e `Cache-Control: no-store` nas rotas de autenticação.
4. *Swagger UI ausente* (apenas o JSON OpenAPI). Disponível em `/swagger` somente em Development, com esquema Bearer.
5. *Acessibilidade* (axe): contraste de texto de destaque sobre fundo suave (novo token `--accent-text`), painel do login, nome acessível do menu do usuário,
   semântica do menu de notificações, regiões roláveis sem acesso por teclado, foco inicial roubado pelo layout, foco perdido ao fechar modais, `aria-hidden` em menus.
6. *Layout:* painel de agente/administrador transbordava 36 px no celular.

**Dependências:** `dotnet list package --vulnerable` e `npm audit`: 0 vulnerabilidades. xUnit v2 é marcado "legacy" (apenas informativo).

**Riscos residuais (aceitos nesta fase)**
- Sem HTTPS local: o cookie só recebe `Secure` e não há HSTS. Em hospedagem real, usar HTTPS e HSTS.
- O frontend não define CSP/cabeçalhos próprios (é servido pelo Vite em desenvolvimento). Ao publicar, configurar CSP no servidor estático.
- Rate limiting em memória (instância única) e por IP; sem CAPTCHA nem 2FA.
- Anexos são validados por assinatura/estrutura, mas não passam por antivírus.
- Dois refresh simultâneos com o mesmo token podem ambos funcionar (sem controle de concorrência).
- Mudança de equipe de um agente só reflete nos grupos do SignalR após reconectar.
- A validação do token consulta o banco a cada requisição: adequado para esta escala; em escala maior, usar cache curto.