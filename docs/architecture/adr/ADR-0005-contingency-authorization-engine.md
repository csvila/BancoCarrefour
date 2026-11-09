# ADR-0005 — Contingency Authorization Engine (CAE) e quotas

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09) — condição: política de exposição e CF-07 confirmadas por Risco antes de ativar contingência em produção
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de software-architect, security-privacy, solution-architect, devops-platform
- Revisores: usuário (aprovado em 2026-11-09); **Risco** precisa aprovar a política de exposição

## Contexto
O CLAUDE.md permite que a contingência aprove apenas operações cobertas por quotas ou reservas pré-alocadas e por política de risco aprovada. **UNKNOWN após timeout não equivale a DECLINED nem a APPROVED.** O core legado tem janelas batch (RC-02), e o Core pode ficar indisponível por partição.

## Problema
Manter parte do serviço disponível durante falhas sem gasto duplo, sem exposição além do aprovado e sem converter incerteza em aprovação.

## Requisitos e restrições
NFR-140..146, NFR-079. TM-015, TM-016, TM-036. SW-003 (crítico), SW-004, CF-07, CF-13.

## Alternativas consideradas
1. **Sem contingência (sempre UNAVAILABLE).** Seguro, mas reduz a disponibilidade de T0.
2. **Quota financiada pelo saldo do cliente (hold de quota no ledger).** Recomendada: sem risco de crédito para o banco.
3. **Quota assumida pelo banco (exposição própria).** Exige apetite de risco aprovado; candidata por segmento no futuro.
4. **Saldo-sombra em cache.** Rejeitada: cache nunca autoriza débito.

## Decisão
1. **Estados normativos:** APPROVED, DECLINED (com motivo), **UNKNOWN** (não conclusivo, exige consulta ou retry com a mesma chave) e **UNAVAILABLE** (certeza de não processamento). Proibido mapear UNKNOWN para APPROVED/DECLINED.
2. **Quotas:** alocadas pelo Core via `AllocateContingencyQuota` (hold na conta). São **assinadas com KMS/HSM** e carregam epoch, validade, escopo (conta/canal/região/dispositivo) e contador monotônico. São **exclusivas por região/instância**, nunca duplicadas. Σ quotas ≤ reserva no ledger.
3. **Entrada em CONTINGENCY por partição, declarada pelo control plane** (partição cercada ou inalcançável), **nunca por timeout de uma requisição**.
4. **Elegibilidade (SW-003):** só requisições **nunca despachadas ao Core**. Requisições em DISPATCHED/UNKNOWN **não vão ao CAE**; ficam UNKNOWN até consulta. A mesma chave vale para Core e CAE.
5. **Fora da quota, quota expirada ou assinatura inválida → DECLINED/UNAVAILABLE.**
6. **Journal durável** por região, assinado e sequenciado. No retorno do Core, `SettleContingency(key=contingencyTxnId)` idempotente contra o hold de quota; reconciliação obrigatória.
7. **Antifraude indisponível:** aplicar a política explícita aprovada por Risco (fail-closed para alto risco); nunca aprovação implícita.
8. **Stand-in (STIP) da bandeira (SW-004):** advices ingeridos como lançamentos forçados idempotentes, com política de saldo negativo e conciliação dedicada; parâmetros de STIP alinhados com Risco e bandeira.
9. **Resposta à rede de cartão em timeout do Core (CF-07):** a rede exige resposta binária no prazo.
   - **Proposta:** responder recusa à rede (fail-closed). O estado interno fica **UNKNOWN**, com `ReleaseHold` compensatório idempotente e `GetCommandResult` para resolver.
   - A recusa externa é a resposta segura ao arranjo; a incerteza interna não é apagada.
   - **Requer aprovação de Risco/negócio,** por tocar a regra 8 do CLAUDE.md.
10. **CAE na borda** (terminal, agência, totem): **fora do escopo da Fase 1** até política de Risco e attestation de dispositivo (CF-13).

## Consequências positivas
Disponibilidade parcial com exposição limitada e auditável; elimina a classe de débito duplo por desvio pós-timeout.

## Consequências negativas e trade-offs
Quotas imobilizam parte do saldo disponível do cliente (impacto de UX a validar com o negócio); complexidade de alocação, expiração e reconciliação; dependência de KMS para assinatura.

## Segurança, privacidade e conformidade
SEC-REQ-015/016/026. Se o CAE tocar dados de cartão, avaliar o escopo PCI (ADR-0012).

## Operação, performance e DR
Métricas de consumo de quota, taxa de UNKNOWN (meta ≤ 0,01%) e idade de journal não reconciliado. Kill switch auditado. Prazo de resolução de UNKNOWN [VALIDAR].

## Plano de migração e rollback
Ativação por feature flag por canal; rollback = desativar (sempre UNAVAILABLE).

## Critérios de validação
Timeout pós-commit seguido de retry: UNKNOWN ou resultado original, **nunca segunda aprovação**. Quota esgotada, expirada, com assinatura inválida ou relógio adulterado: rejeitada. Replay de aprovação offline: deduplicado. Partição de rede: Σ aprovado ≤ Σ quotas. Advice STIP duplicado: um lançamento.

## Referências e ADRs relacionados
ADR-0002, 0003, 0007, 0012. `docs/architecture/bounded-contexts.md` §4.4. Findings SW-003, SW-004, TM-015, TM-016.
