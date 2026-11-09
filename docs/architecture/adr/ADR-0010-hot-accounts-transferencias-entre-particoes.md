# ADR-0010 — Hot accounts e transferências entre partições

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09)
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de performance-analyst (PERF-002 crítico, PERF-010) e software-architect (SW-005, D9)
- Revisores: usuário (aprovado em 2026-11-09)

## Contexto
Com single-writer por partição, contas internas de alto volume (liquidação Pix/SPI, liquidação de bandeira, receita de tarifas, grandes recebedores do varejo) serializam os commits. O teto é ~330 commits/s com sync intra-região e ~9–65/s com sync inter-região, contra demanda estimada de ~667–1 667/s (capacity-model §3.2).

## Problema
Remover o gargalo de contrapartida única e permitir transferências entre partições sem 2PC.

## Alternativas consideradas
1. **Conta interna única serializada.** Inviável no pico.
2. **2PC/XA entre shards.** Rejeitada: bloqueio em falha, latência, acoplamento.
3. **Sub-contas de compensação por shard + netting periódico.** Recomendada.
4. **Micro-batching de lançamentos de cliente.** Rejeitada: atrasa o débito e a resposta.

## Decisão
- **Sub-contas de clearing por shard/partição** (`clearing_<conta>_<shard>`): a contrapartida de cada journal é local ao shard do cliente; cada journal continua balanceado.
- **Journal de netting periódico** (1 s a 1 min, a calibrar) consolida as sub-contas na conta real. O saldo consolidado é a soma das sub-contas.
- **Contas da casa sem checagem de saldo no caminho crítico:** append-only, sem `SELECT FOR UPDATE` na posição.
- Micro-batching **apenas** em contrapartidas internas.
- Escrow/sub-contas por canal ou loja para recebedores corporativos quentes.
- **Transferência entre partições:** saga orquestrada por Payments, com contas de clearing por partição e idempotência por perna (`transferId:n`). Falha de negócio na perna 2 → `Reverse(perna 1)`. Clearing entre partições deve fechar em zero (reconciliação).
- Métrica de `lock_wait` por conta (com cardinalidade controlada).

## Consequências positivas
Throughput escala com o número de shards; compatível com sync inter-região se a ADR-0008 evoluir; sem 2PC.

## Consequências negativas e trade-offs
O saldo da conta real fica eventualmente consolidado (defasagem do netting); mais contas internas no plano de contas; a saga introduz estados intermediários (PENDING) visíveis ao canal (202).

## Segurança, privacidade e conformidade
Sub-contas e netting auditáveis; ajustes apenas por compensação.

## Operação, performance e DR
Alerta de clearing não zerado; tempo de netting; taxa de compensações da saga.

## Plano de migração e rollback
Parâmetro de netting configurável; mudança de mapeamento de sub-contas por expand/contract.

## Critérios de validação
k6 hot account com 2× a demanda sem violar invariantes; teste de falha entre pernas sem diferença na reconciliação; clearing fecha em zero após N ciclos.

## Referências e ADRs relacionados
ADR-0002, 0003, 0008, 0009. capacity-model §4.2.
