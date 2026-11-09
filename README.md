# Banco resiliente — Claude Code Agent Team

Pacote inicial com 8 agentes, skills especializadas, governança de escrita e documentação de evolução.

## Instalação
1. Extraia o pacote na raiz do repositório do projeto.
2. Revise `CLAUDE.md`, `.claude/settings.json` e permissões locais.
3. Execute `python3 scripts/validate-agent-team.py`.
4. Abra Claude Code na raiz e aceite o trust do workspace para habilitar hooks dos subagentes.
5. Inicie com: `Leia CLAUDE.md e PROJECT-STATUS. Faça o Arquiteto de Soluções e o Arquiteto de Software produzirem C1 e C2; em paralelo, Segurança e Privacidade revisam os limites de confiança. Registre todas as delegações e respostas no JSONL.`

## Agentes
- solution-architect
- software-architect
- developer
- test-analyst
- security-privacy
- observability-specialist
- performance-analyst
- devops-platform

## Skills
`c4-model`, `adr-authoring`, `project-evolution-log`, `financial-resilience`, `api-bearer-auth`, `dotnet-csharp14`, `testing-xunit`, `testing-playwright`, `testing-k6`, `security-review`, `lgpd-privacy`, `observability-otel`, `performance-engineering`, `platform-nginx-docker-kubernetes`, `resilience-chaos`, `agent-communication`.

## Governança
Os especialistas são read-only por configuração de ferramentas. `developer` tem hook que permite Write/Edit apenas em `src/`, `infra/` e `deploy/`. `test-analyst` tem hook que permite Write/Edit apenas em `tests/`. Os hooks protegem chamadas das ferramentas Write/Edit; não são uma sandbox completa para comandos shell arbitrários. Por isso, mantenha permissões de Bash restritas, não use `--dangerously-skip-permissions` e revise diffs/commits. Para enforcement forte em ambiente regulado, complemente com permissões de filesystem/containers, branch protection e CI.

## Comunicação
O orquestrador registra delegações e respostas usando `python3 scripts/log-agent-message.py` em `docs/governance/agent-communications.jsonl`. Agentes não escrevem diretamente nesse arquivo. Use `docs/governance/COMMUNICATION-PROTOCOL.md`. Para múltiplos agentes/sessões concorrentes, centralize append com lock.

## Testes
- Unitários: xUnit.
- Integração: Playwright .NET (`Microsoft.Playwright`), APIRequestContext e browser quando aplicável.
- Carga: k6.

## Importante
O pacote define equipe e processo; não implementa ainda o core bancário. C# 14/.NET 10 e versões de dependências devem ser confirmadas no ambiente de build antes de criar a solution.
