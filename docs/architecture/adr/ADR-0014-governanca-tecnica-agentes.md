# ADR-0014 — Governança técnica do time de agentes (complementa ADR-0001)

- Status: **Proposed — requer decisão explícita do usuário** (altera permissões, hooks e escopos; nenhum agente pode aprovar)
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de security-privacy (SEC-001..014), devops-platform (OPS-001..011), solution-architect (SA-006..009), software-architect (SW-014)
- Revisores: usuário

## Contexto
A ADR-0001 define a segregação de escrita, mas hoje ela é aplicada só no cliente e de forma parcial:
- O guard cobre apenas Write/Edit. `developer` e `test-analyst` têm Bash sem restrição (SEC-001/OPS-003).
- O guard só bloqueia com exit 2. Entradas malformadas podem gerar exit 1 e permitir a escrita (SEC-002; **não verificado**).
- A hipótese de interpretador ausente **não se reproduz neste host**: verificação do orquestrador em 2026-11-09, `python3` = 3.15.2.
- Os hooks de ciclo de vida gravam fora do repositório (`%LOCALAPPDATA%\Temp` via Git Bash; verificado).
- O log JSONL não tem lock no Windows, cadeia de hash, `inReplyTo` nem redação (SEC-004/005/006, OPS-007).
- Não há CODEOWNERS, CI nem branch protection (OPS-006, SEC-010); `master` e `main` divergem.
- Os escopos são inconsistentes: `deploy/` no guard e ausente do CLAUDE.md; `developer` bloqueado em `docs/`, embora o CLAUDE.md permita documentação ligada à implementação; ninguém pode alterar `scripts/`, `.claude/` e `.github/` (OPS-005, SEC-003, SA-007).
- A skill `secure-implementation` referenciada não existe (OPS-009, SEC-011, SA-008).
- `bypassPermissions` não está desabilitado (SEC-012).

## Problema
Tornar a segregação de funções tecnicamente aplicável e auditável, sem depender da disciplina dos agentes.

## Alternativas consideradas
1. **Manter só os hooks no cliente.** Insuficiente.
2. **Hooks no cliente reforçados + controles no servidor (CODEOWNERS, branch protection, CI path-segregation) + trilha tamper-evident.** Recomendada.
3. **Sandbox de SO/container por agente** (complementar; o suporte em Windows nativo precisa ser verificado; WSL2/container como opção).

## Decisão (proposta, aguardando o usuário)
1. **Hook PreToolUse para Bash** fail-closed, com allowlist por papel (ex.: `dotnet build/test`, `git status/diff`), negando redirecionamento para fora do escopo, `git commit/push` e interpretadores com `-c`.
2. **Guard robusto:** try/except global → exit 2; validação de tipos; raiz via `CLAUDE_PROJECT_DIR`; normalização de caixa no Windows; rejeitar ADS (`::$DATA`); interpretador e caminho absolutos.
3. **Escopos alinhados** ao CLAUDE.md em decisão única:
   - `developer`: `src/`, `infra/`, `.github/` (CI) e `docs/` vinculados à implementação; `deploy/` removido ou incorporado a `infra/`.
   - `test-analyst`: `tests/`.
   - `scripts/` e `.claude/`: somente com autorização explícita do usuário, caso a caso.
4. **Log de comunicação:** lock portátil (`msvcrt`/lockfile), escrita atômica, `--in-reply-to`, allowlist de `from`/`to`, cadeia `prevHash`, detector de segredos/PAN/CPF/JWT e verificador no CI. Lifecycle registrado no repositório (`.claude/logs/`, ignorado pelo git) ou como evento `status`.
5. **Servidor:** CODEOWNERS por caminho; proteção de `main` (PR + aprovação humana, 2 para `infra/` e ledger, checks obrigatórios, commits assinados, sem force-push); check de CI "path-segregation" com trailer `Agent:`; unificar `master` → `main`.
6. `permissions.disableBypassPermissionsMode: "disable"` (idealmente em managed settings). Deny ancorado: `Edit(/docs/governance/agent-communications.jsonl)` (SEC-005 afirma que regras `Write(path)` não casam; **verificar na documentação antes de aplicar**).
7. **Skill** `secure-implementation`: criar ou substituir por `security-review`. Validador do time checa tools por papel, hooks, skills e CRLF.
8. **Suíte de testes de governança** em `tests/governance/` (test-analyst). O framework para testar scripts Python precisa de decisão (pytest vs. xUnit invocando processo), porque o CLAUDE.md fixa xUnit para unidade.
9. Política para WebFetch/WebSearch: não enviar conteúdo do repositório em consultas (OPS-011).

## Consequências positivas
Segregação verificável; trilha tamper-evident; conformidade com a validação prevista na ADR-0001.

## Consequências negativas e trade-offs
Mais atrito no fluxo dos agentes; manutenção de allowlists; parte do controle depende da configuração do GitHub, que é responsabilidade do usuário.

## Segurança, privacidade e conformidade
TM-034, TM-035, TM-023; SEC-REQ-030.

## Plano de migração e rollback
Aplicar por etapas: guard → Bash hook → log → servidor. Cada etapa reversível por commit.

## Critérios de validação
Testes negativos: developer com `echo x > tests/x.txt` → bloqueado; test-analyst escrevendo em `src/` via Bash → bloqueado; stdin malformado no guard → exit 2; 50 appends concorrentes → 50 linhas válidas; linha alterada no JSONL → verificador falha; PR fora do escopo → CI falha.

## Referências e ADRs relacionados
ADR-0001. Findings agrupados em `docs/architecture/etapa1-consolidacao.md` §3 (G-01..G-14).
