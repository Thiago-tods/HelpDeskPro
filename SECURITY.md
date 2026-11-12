# Política de segurança

HelpDesk Pro é um projeto de portfólio com dados fictícios, sem versão em produção. Ainda assim, relatos de vulnerabilidade são bem-vindos.

## Como relatar
Use **"Report a vulnerability"** na aba *Security* do repositório no GitHub (aviso privado). Não abra issue pública com detalhes exploráveis.
Descreva o impacto, os passos para reproduzir e a versão/commit afetados. Não inclua dados reais de terceiros.

## O que esperar
Respondo assim que possível, sem prazo contratual (projeto pessoal). Correções entram no ramo `main`.

## Escopo
- Dentro do escopo: autenticação/sessão, autorização e isolamento de dados (IDOR), upload de anexos, SignalR, injeção, XSS, exposição de segredos.
- Fora do escopo: ataques que exigem acesso ao computador do desenvolvedor, negação de serviço volumétrica e o uso de credenciais de demonstração em ambientes próprios.

## Segredos
Nenhum segredo é versionado: connection string, chave JWT e senha de demonstração ficam no User Secrets / variáveis de ambiente.
Se você encontrar alguma credencial no histórico, relate de forma privada.

Detalhes técnicos do modelo de segurança, testes e riscos residuais: [docs/SECURITY.md](docs/SECURITY.md).
