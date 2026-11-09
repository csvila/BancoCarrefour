# ADR-0008 — Replicação do ledger e RPO por classe de falha

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09) — Fase 1 = opção B; meta de RPO regional ≤ 5 s [VALIDAR]; B+ reavaliado após medição de RTT
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de solution-architect, devops-platform, performance-analyst, software-architect
- Revisores: usuário (aprovado em 2026-11-09); acompanhamento de negócio/Risco (apetite de RPO) e DevOps (RTT medido)

## Contexto
Os especialistas divergem quanto ao RPO na perda de uma região:
- **solution-architect:** recomenda RPO=0, com quorum síncrono em 3 sites (opção A), pelo menos para transferências internas. Elas não têm contraparte externa, então uma perda seria indetectável.
- **devops-platform:** recomenda assíncrono entre regiões na Fase 1 (B), com promoção manual, LSN de corte e reconciliação.
- **performance-analyst:** com sync entre regiões, o teto por conta quente cai para ~9–65 commits/s, contra demanda de ~667/s. Inviável sem sub-contas (ADR-0010).
- **software-architect:** assíncrono com partição em RECOVERY; nunca promover como se RPO=0.

## Problema
Escolher o modo de replicação inter-região do ledger que equilibre RPO, latência, throughput, custo e o número de sites disponíveis no Brasil.

## Requisitos e restrições
NFR-010/011, NFR-020/023, NFR-060. TM-012. PERF-004, SW-012, OPS-012. Residência de dados (ADR-0011).

## Alternativas consideradas
1. **(A) Quorum síncrono em 3 sites** (2 regiões + witness, ou SQL distribuído com consenso): RPO=0 regional; +≥ 1 RTT por commit; exige 3º site; viável para hot accounts só com ADR-0010.
2. **(B) Sync intra-região + async inter-região:** RPO de segundos declarado; transações na janela viram UNKNOWN/RECOVERY; menor latência e custo.
3. **(B+) B com "ack após replicação" apenas para classes selecionadas** (ex.: transferências internas acima de um valor): RPO=0 nessas classes ao custo de RTT apenas nelas.
4. **(A) para todo T0.** Máxima garantia; latência e custo máximos.

## Decisão
**Fase 1 = (B):**
- Réplica síncrona com quorum em 3 AZs (RPO=0 contra perda de AZ, obrigatório).
- Réplica assíncrona para R2, com **RPO de perda de região declarado explicitamente** (meta proposta ≤ 5 s [VALIDAR]).
- Outbox publicando só até o LSN replicado (ADR-0003); partições afetadas em RECOVERY; reconciliação obrigatória com contrapartes.
- **Medir o RTT entre os sites candidatos** e reavaliar **(B+)** para transferências internas: é a preocupação legítima do solution-architect e a decisão cabe ao negócio.
- **(A) completa somente se o negócio exigir RPO=0 regional,** com 3º site e ADR-0010 implementada.

## Consequências positivas
Latência e throughput preservados para hot accounts; custo menor; compatível com um único site de nuvem + site híbrido.

## Consequências negativas e trade-offs
Na perda total de região, transações confirmadas dentro da janela podem precisar de reconciliação. Transferências internas não têm contraparte externa para reconstrução: **risco residual que o negócio precisa aceitar ou mitigar com (B+).**

## Segurança, privacidade e conformidade
Réplica e backups no Brasil até parecer de Compliance (ADR-0011). Exigências BCB/CMN de continuidade [VALIDAR].

## Operação, performance e DR
Métrica de lag de replicação com alerta; runbook regional (`docs/infra/topology.md` §3.3); drills semestrais com RPO medido.

## Plano de migração e rollback
B → B+ por classe via configuração do commit. B → A exige 3º site e nova ADR.

## Critérios de validação
RTT medido entre os sites candidatos; drill regional com RPO/RTO medidos; k6 de commit com sync intra; teste de RECOVERY e de reconciliação pós-failover com diferença zero.

## Referências e ADRs relacionados
ADR-0002, 0003, 0010, 0011. capacity-model §3.2; topology §1.2–1.3; nfr NFR-011.
