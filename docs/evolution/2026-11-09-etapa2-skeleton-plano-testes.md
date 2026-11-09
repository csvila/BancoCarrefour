# 2026-11-09 — Etapa 2: skeleton e plano de testes

## Delegações
| Agente | Tarefa | messageId | Resultado |
|---|---|---|---|
| developer | ETAPA2-SKELETON | bbb46da0 | Skeleton em `src/`; findings DEV-001..008 |
| test-analyst | ETAPA2-TEST-PLAN | 06c2f849 | `tests/TEST-PLAN.md` (154 casos); findings TA-001..012; handoffs H-01..H-10 |

## Skeleton
- Arquivos de build em `src/` (o guard do developer não permite escrita na raiz; decisão pendente DEV-001/TA-005).
- `.slnx`, `global.json` 10.0.403 (`latestPatch`), `TreatWarningsAsErrors`, NuGetAudit, `InvariantGlobalization`, CPM com pinning transitivo, `packageSourceMapping`.
- ServiceDefaults conforme a ADR-0006: JwtBearer padrão, só RS256/PS256/ES256, skew ≤ 60 s, fallback policy, validação no startup. Inclui também Problem Details, correlation-id, OTel sem query string nem headers, rejeição de token em query (400) e resiliência sem retry em métodos não seguros.
- **Evidência do orquestrador:** `dotnet build BancoCarrefour.slnx -c Release --no-incremental` (em `src/`) → 0 avisos, 0 erros. Nenhuma escrita fora de `src/`. Varredura de segredos sem achados.

## Plano de testes
- 154 casos: 85 P0, 60 P1, 9 P2. Fases: F0 (sobre o skeleton) 38, F1 35, F2 35, F3 33, FG 13.
- **Sondas de governança feitas pelo test-analyst** (em cópias no scratchpad, sem tocar no log real):
  - G-02 confirmado: o guard falha aberto quando recebe JSON com tipo inesperado.
  - G-04 confirmado: append concorrente perde linhas.

## Arquivos de build movidos para a raiz
- Decisão do usuário (messageId d792bc67) com autorização pontual de escrita na raiz. Delegação ao developer: 07023378.
- O developer moveu via `git mv` os seis arquivos de build de `src/` para a raiz, ajustando os caminhos do `.slnx`.
- **Evidência do orquestrador:** `dotnet build BancoCarrefour.slnx -c Release --no-incremental` na raiz → 0 avisos, 0 erros.
- Novos findings: DEV-009 (`.editorconfig` × Markdown), DEV-010 (contexto Docker na raiz), DEV-011 (dono das versões de pacotes de teste).

## Não realizado
- Nenhum teste implementado ou executado.
- Nenhum commit nesta etapa até aqui.
- Nenhuma alteração em `.claude/` ou `scripts/` (ADR-0014 segue Proposed).
