# ADR-0002 — Topologia active-active com autoridade única de escrita por partição

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09)
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de devops-platform, software-architect, solution-architect, performance-analyst
- Revisores: usuário (aprovado em 2026-11-09); security-privacy (revisão cruzada)
- Agentes consultados: solution-architect, software-architect, security-privacy, devops-platform, performance-analyst

## Contexto
A plataforma precisa de alta disponibilidade para 1–10M clientes. A escrita financeira, porém, exige autoridade única por conta e proteção contra split-brain (CLAUDE.md). RPO, RTO e TPS ainda não foram aprovados (OPS-012, PERF-001). A oferta de regiões de nuvem no Brasil pode ser limitada [VALIDAR].

## Problema
Como distribuir canais e leitura em várias regiões sem permitir dois escritores na mesma conta e preservando o caminho para evoluir sem reescrita.

## Requisitos e restrições
NFR-001, NFR-010, NFR-011, NFR-040, NFR-063, NFR-120. TM-011/012/013. Regras financeiras do CLAUDE.md: single-writer, fencing, sem exactly-once.

## Alternativas consideradas
1. **(A) Active-active multi-região completo:** cada partição com home em uma das regiões. Escrita local, mas exige diretório, rehome e CAE por região sem duplicar quotas desde o dia 1, além de fencing maduro.
2. **(B) Ledger active-passive (warm standby) com canais/leitura active-active.** Mais simples e mais barato; escritas originadas em R2 cruzam região.
3. **Multi-writer com CRDT para saldo.** Rejeitado: incompatível com débito sem saldo negativo e com partidas dobradas.
4. **Manter estado atual (sem decisão).** Bloqueia a implementação.

## Decisão
**Fase 1 = opção B, implementada como caso degenerado de A.** Todas as partições têm `home_region = R1`; canais e leitura ficam active-active em R1 e R2.
- **Partições lógicas fixas** (proposta: 4096, ver CF-05), atribuídas à conta na abertura e gravadas no **Partition Directory**. Sem re-hash; a migração acontece por partição.
- **Directory + lease/epoch em store de consenso** (etcd, 3–5 nós, 3 AZs). Cada partição registra shard, home, SoR (ADR-0007) e `epoch`.
- **Fencing validado no armazenamento:** toda transação de escrita confere `partition_control.epoch = :token` **na mesma transação**. Writer com epoch defasado aborta. Lease sem fencing é proibido.
- **Roteamento por biblioteca cliente partition-aware;** writer incorreto responde `WRONG_PARTITION_OWNER`.
- **Intra-região:** quorum síncrono em 3 AZs com failover automático; epoch incrementado antes de aceitar escrita.
- **Inter-região:** promoção manual ou semiautomática com 2 aprovadores, isolamento prévio (revogar credenciais, read-only, bloqueio de ingress, epoch++) e LSN de corte registrado. Partições afetadas ficam em `RECOVERY` até reconciliar. Modelo de replicação e RPO: ADR-0008.
- **Gatilhos de failover:** ≥ 2 sinais independentes; nunca um único health check.
- **Critérios para evoluir para A:** RPO/RTO/TPS aprovados; game days com RPO/RTO medidos; reconciliação com divergência zero em N execuções; 3º site se RPO=0 inter-região for exigido.

## Consequências positivas
RPO=0 contra perda de AZ sem custo de latência inter-região; um único ponto de promoção, mais fácil de auditar; dados nascem particionados (`partition_id`, `home_region`, `epoch`), então A vira rehome gradual.

## Consequências negativas e trade-offs
Escritas originadas em R2 pagam RTT; RTO regional de 15–60 min (proposto); etcd é mais um componente crítico; ~16–64 shards (capacity-model) aumentam a carga operacional.

## Segurança, privacidade e conformidade
Mitiga TM-012/013 (SEC-REQ-012/013). Credenciais de escrita por região, revogáveis. Residência de dados é pré-requisito (ADR-0011).

## Operação, performance e DR
Runbook de falha de região em `docs/infra/topology.md` §3.3. Admission control por prioridade após failover (PERF-005). Ledger DB fora do Kubernetes ou em operator com HA própria.

## Plano de migração e rollback
Greenfield; nada a migrar. Para evoluir até A, rehome por partição com congelamento, epoch++ e canário.

## Critérios de validação
Teste de fencing (writer zumbi rejeitado); chaos de partição de rede inter-AZ e inter-região sem duplo writer; failover de AZ com RPO=0 medido; drill regional com RPO/RTO medidos; k6 de failover (capacity-model §6).

## Referências e ADRs relacionados
ADR-0003, 0005, 0007, 0008, 0010, 0011. `docs/infra/topology.md`, `docs/architecture/c4/02-containers.md`, `docs/performance/capacity-model.md`. Findings: OPS-012, PERF-004, SW-012, TM-012, TM-013.
