# ADR-0004 — CQRS para extrato e consultas

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09)
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de software-architect, performance-analyst, solution-architect
- Revisores: usuário (aprovado em 2026-11-09)

## Contexto
Extrato e histórico são consultas de alto volume, com janela longa e paginação. Consultá-las no writer da partição competiria com as escritas, que são o recurso que dimensiona a plataforma (capacity-model §1.5).

## Problema
Servir extrato com baixa latência e escala horizontal sem carregar o ledger e sem que dados derivados autorizem débito.

## Requisitos e restrições
NFR-002, NFR-014, NFR-024/025, NFR-041/042/043. TM-014, TM-019. PERF-008.

## Alternativas consideradas
1. **Consultar o ledger diretamente (réplicas de leitura do shard).** Mais simples, mas mistura carga e prende o schema do ledger às consultas.
2. **CQRS com projeções alimentadas por eventos do outbox.** Recomendada.
3. **Store wide-column (Cassandra/Scylla) ou OpenSearch.** Escala, mas aumenta a operação; OpenSearch só como complemento para busca.

## Decisão
- **Statement Projector** consome eventos do ledger (e CDC do legado durante a convivência, ADR-0007) e grava em **PostgreSQL particionado por conta/mês**, com Redis para a 1ª página.
- Projeção idempotente por `(journalId, postingSeq)`, ordenada pela sequência da conta, com **watermark** (posição "as-of") exposto ao canal.
- **O read model nunca autoriza débito.** O saldo autoritativo é lido no Core. O comprovante de um comando vem do resultado do próprio comando (read-your-writes).
- Rebuild por replay com snapshots por conta, de forma particionada e paralela, em shadow/blue-green.
- Cache key inclui o principal; autorização por objeto também nas leituras.

## Consequências positivas
Escala independente; o ledger fica isolado; histórico pode ir para tiering.

## Consequências negativas e trade-offs
Consistência eventual (frescor p99 ≤ 5 s, a validar); rebuild completo é caro (~70 h por ano de histórico sem snapshots, PERF-008); dois modelos para manter.

## Segurança, privacidade e conformidade
SEC-REQ-007 nas leituras; contrapartes mascaradas; retenção do read model ≤ necessidade de exibição (ADR-0011).

## Operação, performance e DR
SLO de frescor e alerta de lag (> 30 s). Rebuild a partir do broker ou do ledger em DR; cache descartável.

## Plano de migração e rollback
Durante a convivência, a fonte é CDC do legado + eventos do ledger. Rollback = rebuild.

## Critérios de validação
Teste com read model desatualizado mostrando saldo maior: o débito é decidido pelo ledger. Teste negativo de extrato cruzado entre clientes. k6: extrato p95 dentro do SLO aprovado; rebuild de 1 ano sintético dentro da janela aprovada.

## Referências e ADRs relacionados
ADR-0003, 0007, 0011. `docs/architecture/c4/02-containers.md` (#19).
