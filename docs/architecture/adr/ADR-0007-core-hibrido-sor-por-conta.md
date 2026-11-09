# ADR-0007 — Core híbrido: System of Record único por conta e migração strangler

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09)
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de solution-architect (SA-001) e software-architect (SW-002)
- Revisores: usuário (aprovado em 2026-11-09); validação de P-02 (core legado) com negócio/TI legado

## Contexto
A missão prevê "core híbrido". A autoridade entre o core legado e o novo ledger é um risco aberto no PROJECT-STATUS. Os dois arquitetos classificaram o tema como **crítico** (SA-001, SW-002): dois writers para a mesma conta causam gasto duplo e divergência de saldo.

## Problema
Definir quem é a autoridade de escrita de cada conta durante a convivência e como migrar sem dual-write.

## Requisitos e restrições
NFR-040, NFR-044, NFR-146. TM-030. Regras do CLAUDE.md: ledger é a fonte oficial; escrita com autoridade única por conta/partição.

## Alternativas consideradas
1. **Big-bang.** Risco operacional inaceitável para 1–10M clientes.
2. **Legado como autoridade permanente; PBD só como canal.** Não entrega o ledger novo; mantém as limitações de batch.
3. **Dual-write legado + ledger.** Rejeitada: duas verdades.
4. **Ledger novo como espelho do legado.** Rejeitada: duas verdades.
5. **SoR único por conta + strangler por coorte.** Recomendada.

## Decisão
- **Cada conta tem exatamente um SoR (`LEGACY` ou `NEW`)**, registrado no Partition Directory (ADR-0002) com epoch/fencing. Sem dual-write.
- Roteamento de comandos pelo SoR. Contas `LEGACY` passam pela **Legacy ACL** (idempotente, mTLS, validação de esquema).
- **Strangler:**
  1. Leitura primeiro: read models alimentados por CDC do legado + eventos do ledger.
  2. Produtos novos nascem no ledger.
  3. Migração por coorte/partição: congelar (epoch++ e fencing no legado), drenar ou migrar holds, carga final, reconciliar saldos contábil/disponível/bloqueado/reservado, trocar o SoR, descongelar.
- **Rollback:** antes do primeiro lançamento novo, reverter o SoR; depois, migração compensatória (sem edição destrutiva).
- **Janela batch do legado:** contas `LEGACY` operam apenas com quotas do CAE (ADR-0005); fora delas, UNAVAILABLE. Nunca saldo-sombra.
- Contabilidade geral: o ERP/GL recebe lançamentos sumarizados de forma assíncrona e reconciliada.

## Consequências positivas
Elimina a classe de gasto duplo entre cores; migração reversível por coorte; descomissionamento progressivo do legado.

## Consequências negativas e trade-offs
A ACL e a reconciliação permanecem por toda a convivência; congelamento por coorte exige janela; dependência das interfaces do legado (desconhecidas, P-02).

## Segurança, privacidade e conformidade
Canal autenticado com o legado; reconciliação auditável; migração de PII para o Party com minimização (ADR-0011).

## Operação, performance e DR
Runbook de cutover por coorte; métricas de divergência; o SoR faz parte do failover (Directory replicado).

## Plano de migração e rollback
Descrito na Decisão; cada coorte tem critério go/no-go baseado em reconciliação com diferença zero.

## Critérios de validação
Teste negativo: escrita no SoR errado é rejeitada. Teste de cutover com reconciliação zero. Teste de rollback antes e depois do primeiro lançamento novo. Teste de janela batch: só quotas do CAE.

## Referências e ADRs relacionados
ADR-0002, 0003, 0004, 0005. `docs/architecture/c4/01-context.md` §3. Premissa P-02 e dúvidas de negócio em `docs/architecture/etapa1-consolidacao.md` §5.
