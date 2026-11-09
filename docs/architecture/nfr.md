# Requisitos Não Funcionais — v0.1 (Proposta)

> Autor: `solution-architect` (ETAPA1-C1-NFR, delegação `d541b605`); persistido pelo orquestrador em 2026-11-09.
> Todos os alvos são **propostos**. Os números ilustrativos **não são fatos**. Prazos legais e regulatórios **não** estão definidos aqui.
> ⚠ Valores de latência e capacidade divergem em parte do `docs/performance/capacity-model.md`. A harmonização proposta está em `docs/architecture/etapa1-consolidacao.md` §2 (CF-03, CF-04) e **prevalece sobre esta tabela até a aprovação**.

## 0. Convenções
- **Tiers:**
  - **T0** decisão financeira em tempo real: cartão (emissor), Pix enviado/recebido, débito/transferência, saque ATM, validação de Bearer.
  - **T1** consulta: saldo, extrato, comprovantes, login.
  - **T2** onboarding/KYC, contratação, chaves Pix.
  - **T3** backoffice, relatórios, reporting batch, analytics.
- **SLI de disponibilidade** = requisições válidas com resposta *conclusiva* dentro do limiar ÷ requisições válidas, em 30 dias. UNKNOWN e UNAVAILABLE contam como eventos ruins, embora sejam o comportamento seguro.
- **Latência** medida no servidor, na entrada da PBD, sob pico nominal.
- **Indisponibilidade em 30 dias:** 99,99% = 4,3 min; 99,95% = 21,6 min; 99,9% = 43,2 min; 99,5% = 3,6 h.

## 1. Disponibilidade

| ID | Requisito | Alvo | Validação | Status |
|---|---|---|---|---|
| NFR-001 | Disponibilidade T0 | ≥ 99,99%/mês | SLO + burn-rate; chaos de AZ/região | A validar com negócio; exigências de bandeira/SPI com Compliance |
| NFR-002 | Disponibilidade T1 | ≥ 99,95% | SLO | A validar com negócio |
| NFR-003 | Disponibilidade T2 | ≥ 99,9%, com retomada de jornada | Teste de falha de bureau | Proposto |
| NFR-004 | Disponibilidade T3 | ≥ 99,5%; 100% dos prazos de reporting | Agenda e alertas | A validar com Compliance |
| NFR-005 | Saturação de T1–T3 não degrada T0 | 0 violações | Saturar extrato e medir T0 | Proposto |
| NFR-006 | Nenhuma dependência síncrona de T0 com SLO menor sem fallback seguro; IdP fora do caminho por transação | 100% mapeado | Revisão C2 + chaos | Proposto |
| NFR-007 | Deploy sem downtime para T0/T1; batch do legado não indisponibiliza T0 além do CAE | 0 min planejados | Canary; janelas batch | A validar com negócio/legado |

## 2. RTO/RPO por classe

| ID | Classe | RPO | RTO | Observação | Status |
|---|---|---|---|---|---|
| NFR-010 | Ledger, decisões, idempotência, outbox, reservas — **perda de AZ** | **0** | ≤ 60 s automático | Quorum síncrono entre AZs | Proposto (obrigatório) |
| NFR-011 | Mesma classe — **perda de região** | SA recomenda 0 (opção A, 3 sites); alternativa B ≤ 5 s com janela UNKNOWN | ≤ 15 min com fencing confirmado | **Conflito CF-01**. Decisão na ADR-0008 | Pendente de decisão |
| NFR-012 | Cadastro, KYC, consentimentos | ≤ 1 min (consentimento: avaliar 0) | ≤ 30 min | — | A validar com Jurídico |
| NFR-013 | Trilha de auditoria | ≤ 1 min | ≤ 1 h | Buffer local durável | Proposto |
| NFR-014 | Read models | N/A (derivado) | Serviço ≤ 15 min; rebuild total ≤ 4 h no cenário 10M (a medir) | Ver PERF-008: estimativa de ~70 h por ano de histórico sem snapshots | Proposto |
| NFR-015 | Cache | N/A | Cold start sem violar T0 | — | Proposto |
| NFR-016 | Documentos/biometria | ≤ 15 min | ≤ 4 h | — | Proposto |
| NFR-017 | Telemetria (exceto auditoria) | ≤ 15 min | ≤ 1 h | — | Proposto |
| NFR-018 | Backups WORM em outro site, PITR, restore trimestral | — | — | Réplica não é backup | Proposto |

## 3. Latência (servidor, pico nominal)

| ID | Operação | p50 / p95 / p99 | Status |
|---|---|---|---|
| NFR-020 | Decisão de emissor (cartão); timeout interno menor que o da processadora/bandeira, com margem | 40 / 100 / 250 ms (harmonizar: CF-03) | Valores externos a validar |
| NFR-021 | Pix enviado (etapa interna) | — / 250 / 500 ms | Arranjo a validar com Compliance |
| NFR-022 | Pix recebido (liquidação confirmada → crédito disponível) | — / 200 / 500 ms | A validar com Compliance |
| NFR-023 | Transferência interna | — / 200 / 400 ms (+RTT se ADR-0008 = A) | Proposto |
| NFR-024 | Saldo | — / 100 / 250 ms | Proposto |
| NFR-025 | Extrato (≤ 50 itens, ≤ 90 dias); histórico profundo ≤ 3 s ou assíncrono | — / 300 / 800 ms (harmonizar: CF-03) | Janela online a validar com negócio |
| NFR-026 | Login/emissão de token (sem MFA) | — / 800 ms | Proposto |
| NFR-027 | Validação local de Bearer | p99 ≤ 2 ms | Proposto |
| NFR-028 | Passo de onboarding | p95 ≤ 2 s (sem terceiros) | Proposto |

## 4. Throughput e capacidade
O modelo vigente é o `docs/performance/capacity-model.md` (conflito CF-04). O exemplo ilustrativo do solution-architect (10M: ~2 500 escritas/s em burst) é compatível em ordem de grandeza com o cenário B do performance-analyst (~2 746 escritas/s no pior dia). A proporção leitura/escrita (r = 8) foi **substituída** pela do modelo de capacidade (40–50% de escrita).

| ID | Requisito | Status |
|---|---|---|
| NFR-030 | Capacidade sustentada ≥ pior dia × 1,3 por região em N-1 (critério do capacity-model) | Depende de dados de negócio |
| NFR-031 | Sazonalidade (Black Friday, salário, fatura) calibrada com histórico | A validar com negócio |
| NFR-032 | Hot accounts sem degradar NFR-02x (ADR-0010) | Proposto |
| NFR-033 | Sobrecarga: rejeição explícita, fila limitada, nunca aprovação silenciosa | Proposto |

## 5. Consistência

| ID | Requisito | Alvo | Status |
|---|---|---|---|
| NFR-040 | Ledger linearizável por partição; Σ débitos = Σ créditos | 0 quebras (job contínuo) | Proposto |
| NFR-041 | Débito nunca autorizado por cache/read model | 0 violações | Proposto |
| NFR-042 | Defasagem dos read models | p95 ≤ 1 s, p99 ≤ 5 s, alerta > 30 s; UI mostra "posição em <timestamp>" | A validar com negócio |
| NFR-043 | Read-your-writes: comprovante vem do resultado do comando | 100% | Proposto |
| NFR-044 | Reconciliação legado × Ledger × contrapartes | 100% casado ou exceção registrada | A validar com Compliance/Operações |
| NFR-045 | Sequência monotônica por conta; detecção de lacunas | 0 lacunas não detectadas | Proposto |

## 6. Idempotência

| ID | Requisito | Status |
|---|---|---|
| NFR-050 | 100% dos comandos financeiros com `Idempotency-Key`, escopo e hash do payload. Mesmo payload devolve o mesmo resultado; payload divergente → 409/422 (CF-10); sem chave → 400 | Proposto |
| NFR-051 | Retenção ≥ maior janela de retry/reapresentação/estorno das contrapartes | Janelas externas a validar |
| NFR-052 | Consumidores at-least-once com dedup por eventId; replay total produz estado idêntico | Proposto |
| NFR-053 | Integrações carregam IDs da contraparte; timeout → UNKNOWN + consulta antes de repetir | Proposto |

## 7. Durabilidade e integridade

| ID | Requisito | Status |
|---|---|---|
| NFR-060 | Commit durável (quorum) antes de confirmar ao canal | Proposto |
| NFR-061 | Lançamentos imutáveis (sem UPDATE/DELETE por permissão); correção por reversão | Proposto |
| NFR-062 | Hash encadeado por partição; verificação diária | Proposto |
| NFR-063 | Fencing token em toda escrita; storage rejeita epoch antigo | Proposto |

## 8. Segurança e Bearer

| ID | Requisito | Status |
|---|---|---|
| NFR-070 | OAuth2/OIDC; allowlist de algoritmos; validação de iss, aud, exp, nbf, kid/JWKS, scopes/roles em 100% dos endpoints (exceto health) | Proposto (ADR-0006) |
| NFR-071 | Access token de cliente com TTL ≤ 10 min (proposto); refresh rotativo; sender-constrained para parceiros, Open Finance e alto risco | Perfil Open Finance a validar |
| NFR-072 | Autorização por objeto em cada requisição; 401/403 sem revelar existência | Proposto |
| NFR-073 | mTLS + workload identity; zero credencial estática | Proposto |
| NFR-074 | Segredos só em KMS/HSM/secret manager; scanner bloqueante na CI | Proposto |
| NFR-075 | TLS 1.2+ (1.3 preferido); cifra em repouso com rotação | Proposto |
| NFR-076 | Operadores: MFA, menor privilégio, JIT/PAM, 4-olhos | Proposto |
| NFR-077 | Certificado por dispositivo com revogação propagada em ≤ 5 min (proposto) | Proposto |
| NFR-078 | Rotação de JWKS sem downtime | Proposto |
| NFR-079 | Antifraude em T0 com timeout; fallback = recusa ou política do CAE, nunca aprovação | A validar com Risco |

## 9. LGPD (sem prazos legais definidos aqui)

| ID | Requisito | Status |
|---|---|---|
| NFR-080 | Registro das operações de tratamento antes do go-live; stores classificados C0–C4 | A validar com Encarregado |
| NFR-081 | Nenhum CPF completo, PAN, CVV, PIN, token ou senha em logs, traces ou eventos | Proposto |
| NFR-082 | Ledger/eventos com IDs pseudônimos; PII em store separado; crypto-shredding quando cabível (ADR-0011) | A validar com Jurídico |
| NFR-083 | Retenção configurável por categoria, descarte com evidência | Prazos por Jurídico/Compliance |
| NFR-084 | Fluxo de direitos do titular (art. 18) e revisão de decisão automatizada (art. 20) | A validar com Jurídico |
| NFR-085 | RIPD para biometria, antifraude e perfilamento antes do go-live | A validar com Encarregado |
| NFR-086 | Nenhuma transferência internacional sem base validada | A validar com Jurídico |
| NFR-087 | Resposta a incidentes com comunicação à ANPD/titulares | Prazos a validar |
| NFR-088 | Somente dados sintéticos em dev/test | Proposto |

## 10. PCI DSS

| ID | Requisito | Status |
|---|---|---|
| NFR-090 | CDE minimizado, segmentação documentada e testada (ADR-0012) | A validar com QSA |
| NFR-091 | SAD nunca armazenado após autorização; PIN só em HSM | Proposto |
| NFR-092 | Versão vigente do PCI DSS e nível de validação | A validar |

## 11. Auditabilidade

| ID | Requisito | Status |
|---|---|---|
| NFR-100 | Auditoria de 100% dos comandos financeiros, ações de backoffice, acessos a PII e mudanças de configuração; sem payload sensível | Proposto |
| NFR-101 | Armazenamento WORM; operadores não apagam | Retenção a validar |
| NFR-102 | Drift de relógio ≤ 50 ms; ordenação por sequência | Proposto |
| NFR-103 | Reconstruir o saldo em qualquer instante; rastrear lançamento → comando → chave → decisão → ator | Proposto |

## 12. Observabilidade

| ID | Requisito | Status |
|---|---|---|
| NFR-110 | Tail sampling em T0 retendo 100% de erros, UNKNOWN e lentidões | Proposto |
| NFR-111 | SLO por jornada com burn-rate multi-janela | Proposto |
| NFR-112 | Métricas de negócio: aprovação/recusa/UNKNOWN, idade do outbox, lag, quebras de reconciliação, consumo de quotas | Proposto |
| NFR-113 | W3C traceparent ponta a ponta (inclusive broker e ACL) | Proposto |
| NFR-114 | Telemetria respeita NFR-081 | Proposto |
| NFR-115 | MTTD de violação de SLO T0 ≤ 5 min | Proposto |

## 13. Escalabilidade e operabilidade

| ID | Requisito | Status |
|---|---|---|
| NFR-120 | De 1M a 10M sem re-arquitetura; mover partição online com fencing (≤ 5 s de indisponibilidade por partição) | Proposto |
| NFR-121 | Scale-out stateless em ≤ 5 min; pré-escala para eventos agendados | Proposto |
| NFR-122 | Tiering online/arquivo do ledger | Proposto |
| NFR-130 | Canary com rollback automático por SLO (≤ 10 min) | Proposto |
| NFR-131 | Migrações expand/contract compatíveis com N-1 | Proposto |
| NFR-132 | Runbooks para falhas T0; drills de AZ (mensal) e de região (semestral) | A validar com negócio |
| NFR-133 | Kill switches e feature flags auditados | Proposto |
| NFR-134 | APIs versionadas, Problem Details, OpenAPI, política de depreciação | Proposto |
| NFR-135 | 100% IaC; drift detection; sem mudança manual em produção | Proposto |

## 15. Contingência offline

| ID | Requisito | Status |
|---|---|---|
| NFR-140 | Estados distintos: **APPROVED**, **DECLINED** (com motivo), **UNKNOWN** (não conclusivo: consultar ou repetir com a mesma chave), **UNAVAILABLE** (certeza de não processamento). Timeout nunca é convertido silenciosamente | Proposto (CLAUDE.md regra 8) |
| NFR-141 | CAE só dentro de quotas pré-alocadas, assinadas (HSM), com validade, sem duplicação entre regiões; Σ quotas ≤ reserva no ledger | Proposto (ADR-0005) |
| NFR-142 | Exposição máxima por conta/canal/região | A validar com Risco |
| NFR-143 | Journal offline durável, assinado, sequenciado; reconciliação 100% | Proposto |
| NFR-144 | Duração máxima offline por dispositivo/canal | A validar com Risco |
| NFR-145 | Advices de stand-in lançados como lançamentos forçados idempotentes | A validar com bandeira/processadora |
| NFR-146 | Legado em batch: só quotas do CAE; caso contrário, UNAVAILABLE | Proposto |

## 16. Custo e outros

| ID | Requisito | Status |
|---|---|---|
| NFR-150 | Custo por transação e por cliente ativo medido mensalmente; tags FinOps | A validar com negócio |
| NFR-151 | Custo de N-1 explícito; não-T0 escala para baixo | Proposto |
| NFR-152 | Licenças e custo de saída avaliados nas ADRs | Proposto |
| NFR-160 | Residência de dados por classe e fornecedor | A validar com Compliance/Jurídico |
| NFR-170 | Acessibilidade (proposto WCAG 2.1 AA) | Obrigação a validar com Jurídico |
| NFR-180 | Conformidade/homologação Open Finance/Pix | A validar com Compliance |
