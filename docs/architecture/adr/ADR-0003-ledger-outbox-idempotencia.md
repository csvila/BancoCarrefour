# ADR-0003 — Ledger de partidas dobradas, Outbox transacional e idempotência

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09)
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de software-architect, security-privacy, performance-analyst, solution-architect
- Revisores: usuário (aprovado em 2026-11-09)
- Agentes consultados: os cinco especialistas da Etapa 1

## Contexto
O ledger é a fonte oficial. A entrega de eventos é at-least-once. Comandos financeiros exigem idempotência (CLAUDE.md). Cada autorização de cartão é uma escrita (reserva), e escritas são 40–50% do tráfego (capacity-model §1.5).

## Problema
Garantir que cada comando financeiro produza exatamente um efeito contábil, que eventos só sejam publicados após o commit e que limites e saldo nunca sejam violados sob concorrência.

## Requisitos e restrições
NFR-040..045, NFR-050..053, NFR-060..062. TM-009/010/011/014/016/018/029. SW-006, SW-009, SW-010, PERF-005, PERF-009.

## Alternativas consideradas
1. **Idempotência só no gateway.** Rejeitada: não é atômica com o efeito.
2. **Store de idempotência separado (ex.: Redis).** Rejeitada: gravação dupla e perda em failover.
3. **Lançamento contábil na autorização de cartão, com reversão.** Alternativa a "hold como memo" (ver Decisão 5).
4. **Contador de limite no serviço de Risk.** Rejeitada: excede o limite sob concorrência (SW-006).
5. **Ledger dedicado (ex.: TigerBeetle).** Merece PoC futura; não é a base inicial (ADR-0009, D3).

## Decisão
1. **Core Banking** (Accounts + Ledger + Holds + consumo de limite + Idempotência + Outbox) grava **numa única transação por partição**: verificação de epoch, registro de idempotência, status da conta, disponível, consumo de limite, JournalEntry balanceado e mensagem de outbox.
2. **Invariantes:** Σ débitos = Σ créditos por entry e moeda; append-only (conta do serviço com permissão só de INSERT; UPDATE/DELETE bloqueados); hash encadeado por partição; correções apenas por reversão/compensação; data contábil pelo calendário de negócio.
3. **Saldos distintos:** contábil, disponível, bloqueado e reservado. `disponível = contábil − holds − bloqueado (+ limite contratado quando o produto prevê)`.
4. **Idempotência:**
   - Header `Idempotency-Key` obrigatório em todo comando financeiro (400 se ausente).
   - Escopo `(client_id, sujeito, operação, chave)` e hash SHA-256 do payload canônico.
   - Estados IN_PROGRESS/COMPLETED. O replay devolve a resposta gravada com `Idempotent-Replayed: true`.
   - **Payload divergente → 422** (`idempotency-key-reused`); **em andamento → 409** com `Retry-After` (CF-10).
   - Retenção ≥ maior janela de retry/reapresentação das contrapartes [VALIDAR], com particionamento temporal.
   - Chaves internas derivadas e estáveis por perna (ex.: `transferId:1`).
5. **Cartão:** hold como memo na posição na autorização; JournalEntry na captura; estorno pós-captura como novo lançamento (alternativa 3 rejeitada por volume de lançamentos e conciliação).
6. **Crédito externo** (Pix/TED recebido): estados recebido → contabilizado → disponível/bloqueado. Fica disponível apenas após liquidação confirmada (SW-010).
7. **Outbox:**
   - Relay com lease por shard; polling no início, CDC quando o volume exigir.
   - Eventos com `eventId`, sequência por agregado e hash/assinatura, sem PII direta.
   - Consumidores deduplicam.
   - **O relay publica apenas até o LSN já replicado ao DR** (watermark), se a ADR-0008 optar por replicação assíncrona inter-região.
8. **Retry:** em uma única camada, budget ≤ 10%, backoff exponencial com jitter, circuit breaker. **Nenhum retry de comando financeiro sem a mesma idempotency key.** NGINX sem `non_idempotent`.
9. **Timeout interno → UNKNOWN**, seguido de `GetCommandResult(key)` ou retry com a mesma chave. Nunca uma chave nova e nunca FAILED sem confirmação.

## Consequências positivas
Efeito único por comando; eventos coerentes com o commit; invariantes de saldo e limite garantidas pelo banco; auditabilidade (NFR-103).

## Consequências negativas e trade-offs
~8–12 linhas e ~25 inserções em índice por comando (capacity-model §2); expurgo de outbox e idempotência gera I/O; o watermark de replicação atrasa eventos.

## Segurança, privacidade e conformidade
Cobre SEC-REQ-009/010/011/014/016/018/025. Ledger sem nome, CPF ou PAN (ADR-0011).

## Operação, performance e DR
Alertas por idade do outbox (p99 ≤ 2 s; warn > 5 s; crit > 60 s). Lookup de idempotência p99 ≤ 5 ms. Expurgo por drop de partição.

## Plano de migração e rollback
Greenfield. Mudança de schema por expand/contract (NFR-131).

## Critérios de validação
xUnit/Playwright: sem chave → 400; 10 requisições paralelas → 1 lançamento; payload divergente → 422; concorrente → 409; timeout pós-commit → mesma resposta. Concorrência: 100 débitos com saldo para 10 → exatamente 10. Permissão: UPDATE no ledger → erro. Job de invariantes com 0 quebras. Replay total do broker → estado idêntico.

## Referências e ADRs relacionados
ADR-0002, 0004, 0005, 0008, 0010. `docs/architecture/bounded-contexts.md` §4. Draft IETF httpapi `Idempotency-Key` (ainda não é RFC). RFC 9457 (Problem Details).
