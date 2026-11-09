# Modelo de Capacidade — v0.1 (Proposta)

> Status: **Proposta — nenhum número é fato medido.** Autor: `performance-analyst` (ETAPA1-CAPACITY, resp. à delegação `7d70c21e`). Persistido pelo orquestrador em 2026-11-09.
> Todas as premissas (P-xx) devem ser substituídas por dados de negócio/telemetria (§9). Para sizing, usar o cenário **Agressivo** até haver dados reais.
> Decisões relacionadas: ADR-0002, ADR-0003, ADR-0008, ADR-0010.

## 0. Convenções
- Cenários: **C** conservador, **B** base, **A** agressivo (envelope de sizing).
- TPS = operações/s na borda (API/ISO 8583). Escrita financeira = comando que altera o ledger ou cria reserva.
- Escalonamento linear por premissa para 5M/10M; efeitos não lineares (hot accounts, batch, cache) tratados em §4–§5.

## 1. Modelo paramétrico de carga

### 1.1 Fórmulas
- Sessão (login/token, saldo, extrato): `R_t = N × a × s × k_t`
- Transacional (Pix, boleto, cartão, ATM, APIs): `R_t = N × f_t`
- `TPS_avg = R_t / 86 400`; `TPS_pk = R_t × h / 3 600 × b` (h = fração do dia na hora de pico; b = burst intra-hora)
- Pior dia: `TPS_worst = TPS_pk × m_t` (m = multiplicador sazonal: 5º dia útil, Black Friday)
- Capacidade por região: `C_reg = Σ max_dia(TPS_worst) × H_burst`, com `H_burst = 1,3`; **cada região absorve 100% do pior dia (N-1)**; CPU-alvo 60–65% em `C_reg`.

### 1.2 Premissas (ajustáveis)

| ID | Parâmetro | C | B | A |
|---|---|---|---|---|
| P-01 | `a` clientes ativos/dia | 20% | 35% | 50% |
| P-02 | `s` sessões/dia por ativo | 1,5 | 2,0 | 3,0 |
| P-03 | `k` login/token por sessão | 1,2 | 1,2 | 1,2 |
| P-04 | `k` saldo por sessão | 1,5 | 1,5 | 1,5 |
| P-05 | `k` extrato por sessão | 0,6 | 0,6 | 0,6 |
| P-06 | `f` Pix/transf. enviados/cliente/dia | 0,3 | 0,6 | 1,2 |
| P-07 | `f` Pix/TED recebidos/cliente/dia | 0,3 | 0,6 | 1,2 |
| P-08 | `f` boleto/cliente/dia | 0,03 | 0,05 | 0,10 |
| P-09 | `f` autorização de cartão/cliente/dia (~70% presencial) | 0,5 | 1,0 | 2,0 |
| P-10 | `f` saque ATM/cliente/dia | 0,01 | 0,02 | 0,04 |
| P-11 | `f` APIs parceiros/Open Finance (95% leitura) | 0,2 | 0,5 | 1,5 |
| P-12 | `h` fração do dia na hora de pico | 8% | 10% | 12% |
| P-13 | `b` burst intra-hora | 1,5 | 2,0 | 3,0 |
| P-14 | `m` 5º dia útil/pagamento | 1,5 | 2,0 (cartão 1,3) | 3,0 |
| P-15 | `m` Black Friday (varejo Carrefour) | 1,5 | cartão 3,0; demais 1,5 | 3,0 |
| P-16 | Comandos de ledger por ciclo de cartão (auth+captura+reversões) | 2,2 | 2,2 | 2,2 |

### 1.3 Cenário B, 1M clientes (fator de pico = 5,556e-5)

| Tipo | Classe | Ops/dia | TPS médio | TPS pico | m | TPS pior dia |
|---|---|---|---|---|---|---|
| Login/token | auth | 840 000 | 9,7 | 46,7 | 2,0 | 93 |
| Saldo | leitura | 1 050 000 | 12,2 | 58,3 | 2,0 | 117 |
| Extrato | leitura | 420 000 | 4,9 | 23,3 | 2,0 | 47 |
| Pix/transf. enviado | escrita | 600 000 | 6,9 | 33,3 | 2,0 | 67 |
| Pix/TED recebido | escrita | 600 000 | 6,9 | 33,3 | 2,0 | 67 |
| Boleto | escrita | 50 000 | 0,6 | 2,8 | 2,0 | 6 |
| Autorização de cartão | escrita (reserva) | 1 000 000 | 11,6 | 55,6 | 3,0 (BF) | 167 |
| Saque ATM | escrita | 20 000 | 0,2 | 1,1 | 2,0 | 2 |
| APIs parceiros | 95% leitura | 500 000 | 5,8 | 27,8 | 2,0 | 56 |
| **Total** | | **5 080 000** | **58,8** | **282** | envelope | **620** |

Envelope soma máximos de dias diferentes (conservador). Em um único dia: 5º dia útil ≈ 526 TPS; Black Friday ≈ 507 TPS.

### 1.4 Agregado (TPS médio / pico / pior dia)

| Cenário | Ops/dia por 1M | 1M | 5M | 10M |
|---|---|---|---|---|
| C | 2,33M | 27 / 78 / 117 | 135 / 388 / 583 | 270 / 777 / 1 165 |
| B | 5,08M | 59 / 282 / 620 | 294 / 1 411 / 3 101 | 588 / 2 822 / 6 202 |
| A | 10,99M | 127 / 1 099 / 3 297 | 636 / 5 495 / 16 485 | 1 272 / 10 990 / 32 970 |

### 1.5 Leitura vs escrita

| Cenário | Auth/dia por 1M | Leitura | Escrita financeira | % escrita | Escrita 10M médio / pico / pior |
|---|---|---|---|---|---|
| C | 360k | 820k | 1 150k | 49% | 133 / 383 / 575 |
| B | 840k | 1 945k | 2 295k | 45% | 266 / 1 275 / 2 746 |
| A | 1 800k | 4 575k | 4 615k | 42% | 534 / 4 615 / 13 845 |

**Conclusão crítica:** cada autorização de cartão é uma escrita (reserva). A fração de escrita fica entre 40% e 50%, então **o ledger dimensiona a plataforma**, não a leitura.

### 1.6 Distribuição horária (P-17, validar com telemetria)
Pix/app com picos às 11–13h e às 18–21h. Cartão/POS com pico às 18–21h e nos fins de semana; na Black Friday, picos à 0h (e-commerce) e às 10–12h (loja). No dia de pagamento, 0–7h concentra agendados e uma tempestade de consultas de saldo. A janela de batch noturno concorre com o online e **não está somada** (PERF-007).

## 2. Amplificação de escrita, IOPS e armazenamento

### 2.1 Linhas por comando financeiro (P-18)

| Artefato (mesma transação) | Linhas | Bytes estimados | Retenção |
|---|---|---|---|
| Journal (cabeçalho) | 1 | ~0,4 KB | longa (Jurídico) |
| Postings (média 3) | 2–4 | ~1,2 KB | longa |
| Posição de saldo | 1–2 | ~0,2 KB | corrente |
| Hold (cartão) | 1 | ~0,3 KB | até captura/expiração |
| Registro de idempotência | 1 | ~0,3 KB | curta (definir) |
| Outbox | 1–2 | ~1 KB cada | até publicar e expurgar |
| Auditoria de decisão | 1 | ~0,5 KB | longa |
| **Total** | **~8–12 linhas, ~25 inserções em índice** | **~2 KB longa + ~1,5 KB transitória** | |

Eventos: 1,5–2 por comando. WAL: ~6 KB por comando (até ~3× com full-page writes).

### 2.2 Crescimento, 10M clientes (ciclo de cartão ×2,2)

| Cenário | Comandos/dia | Ledger primário/dia | **TB/ano primário** | 3 cópias | Read model extrato |
|---|---|---|---|---|---|
| C | 17,5M | 35 GB | **12,8** | ~38 TB | ~6 TB/ano |
| B | 35,0M | 70 GB | **25,5** | ~77 TB | ~13 TB/ano |
| A | 70,2M | 140 GB | **51,2** | ~154 TB | ~26 TB/ano |

Retenção legal **a validar com Jurídico** (exemplo ilustrativo: 5 anos em B ≈ 128 TB primário, o que exige particionamento temporal e tiering hot/warm/cold imutável).

### 2.3 IOPS (B, 10M, pior dia ≈ 2 746 escritas/s)
WAL ≈ 16,5 MB/s (até ~50 MB/s). Páginas de dados ≈ 14k–27k IOPS no agregado (28k–55k com headroom 2×), ou seja, ~2–3,5k IOPS por shard com 16 shards (requer benchmark). No cenário A, multiplicar por 5: 64 shards ou instâncias maiores.

## 3. Latência

### 3.1 Orçamento por hop (candidato; a soma de percentis é um limite superior)

**Autorização de cartão (emissor).** Alvo: p50 ≤ 60 / p95 ≤ 150 / p99 ≤ 250 ms. Os prazos das bandeiras precisam ser validados.

| Hop | p50 | p95 | p99 |
|---|---|---|---|
| Gateway ISO 8583 / mTLS | 2 | 5 | 10 |
| HSM | 3 | 8 | 15 |
| Roteamento + regras | 2 | 5 | 10 |
| Antifraude (timeout rígido ~60 ms, fallback por política) | 15 | 40 | 60 |
| Reserva + outbox (commit síncrono intra-região) | 5 | 15 | 40 |
| Resposta | 2 | 5 | 10 |
| **Soma** | **29** | **78** | **145** |

**Pix/transferência (comando aceito, sem SPI).** Alvo: p95 ≤ 300 / p99 ≤ 600 ms. Soma: 32 / 94 / 208 ms (WAF 2/5/15; JWT 0,5/1/3; idempotência 3/8/20; antifraude 20/60/120; ledger 5/15/40; resposta 2/5/10).

**Extrato.** Alvo: p95 ≤ 150 / p99 ≤ 300 ms (1ª página). Frescor p99 ≤ 5 s. Cache hit 1/3/8 ms; miss com keyset pagination 5/20/60 ms.

### 3.2 Replicação síncrona e commit
`W_commit ≈ fsync_local + RTT_réplica + fsync_réplica`. Intra-região acrescenta ~1–3 ms no p50 (usar quorum ANY 1 de N). Inter-região acrescenta ≥ 1 RTT (ilustrativo: 5–15 ms no país; ≥ 100 ms fora dele).
Teto por conta quente: `λ_max ≈ 1/W_lock` ≈ **330/s** intra (3 ms), **65/s** inter próxima (15 ms), **9/s** inter distante (110 ms).
**Conclusão:** com sync inter-região, nenhuma conta de liquidação aguenta o pico (~667/s numa única contrapartida, cenário B 10M). Recomendação: **sync intra-região + async inter-região com RPO > 0 explícito**, ou sync inter-região apenas com sub-contas (ADR-0008, ADR-0010).

### 3.3 Little's Law
Conexões DB em uso (B 10M, pior dia × 2 = 5 492 tx/s): W = 10 ms → L ≈ 55 no agregado (~3,5 por shard com 16 shards); W = 40 ms → L ≈ 220. Pool por shard = 2–4× o L, **limitado** (~2× vCPU do DB), com timeout de espera explícito (50–100 ms no caminho do cartão). Requisições em voo na borda no cenário A ≈ 3 300. Alarmar por `pool_wait_time` p99, não só por CPU.

## 4. Sizing inicial (B 10M; A entre parênteses)
Throughput por pod de 2 vCPU (P-19, a medir): leitura 600–800 RPS; token 300; comando financeiro 200–250. Mínimo de 3 réplicas (1 por AZ).

| Serviço | C_reg (TPS) | RPS/pod | Pods/região B (A) |
|---|---|---|---|
| Identidade/token (se self-hosted) | 1 213 | 300 | 7 (≈36) |
| Saldo | 1 517 | 800 | 3 (≈15) |
| Extrato | 607 | 600 | 3 (≈12) |
| Pagamentos/Pix/boleto | 1 807 | 200 | 14 (≈70) |
| Autorização de cartão (bulkhead) | 2 167 | 250 | 14 (≈70) |
| ATM gateway | 29 | — | 3 (3) |
| APIs parceiros | 723 | 400 | 3 (≈30) |
| Ledger writer | 4 039 escritas/s | 250 | ~25 (≈110) |
| Outbox relay | 1 por shard | — | 16 (64) |

**Ledger:** limite ≤ 500 comandos/s por shard → **16 shards (B)**, **64 (A)**. Partições virtuais por `hash(accountId)` mapeadas em shards (ver ADR-0002 sobre o número).
**Hot accounts** (ADR-0010): sub-contas de compensação por shard + journal de netting periódico; contas da casa sem `SELECT FOR UPDATE` no caminho crítico; micro-batching apenas em contrapartidas internas; escrow para recebedores quentes; transferências entre shards via saga com conta de trânsito (sem 2PC).
**Broker:** ~8k eventos/s por região (B) e ~36k (A). Tópico do ledger com 48–64 partições (B) e 192–256 (A), chave `accountId`. 16 consumidores iniciais do projetor, com consumo idempotente por `(journalId, postingSeq)`.
**Cache:** 80–90% de hit na 1ª página do extrato. **Nunca autoriza débito.**
**Headroom:** cada região dimensionada para 100% do pior dia × 1,3. DB ≤ 60% e broker ≤ 50% no pior dia.

## 5. Gargalos e riscos

| Risco | Mitigação |
|---|---|
| Hot accounts / hot partition | Sub-contas por shard + netting; métrica de `lock_wait` por conta |
| Contenção em conta de cliente | Lock otimista com retry limitado; rate limit por conta/cartão |
| Thundering herd pós-failover | Admission control por prioridade (cartão > Pix > saldo > extrato > parceiros), aquecimento escalonado, jitter |
| Amplificação por retries (3 camadas × 3 = 27×) | Retry em uma única camada, budget ≤ 10%, backoff com jitter, circuit breaker, idempotency key obrigatória |
| Outbox lag | Alerta por idade do mais antigo (> 5 s warn, > 60 s crit); expurgo por drop de partição |
| Rebuild do read model (~70 h por ano de histórico a 50k/s) | Snapshots, rebuild particionado e paralelo, shadow/blue-green |
| Batch noturno vs online | Throttling por shard, quota de IOPS, prioridade do online (PERF-007) |
| Store de idempotência | Índice (escopo, chave), particionamento temporal, TTL definido |
| Antifraude síncrono domina o p99 | Timeout rígido + fallback explícito; nunca aprovação implícita |
| GC .NET | Server GC, pooling, medir allocations/req |
| Expiração de holds | Expiração contínua e particionada |

## 6. Plano de validação k6 (scripts a cargo do `test-analyst` após aprovação dos SLOs)
Open model (`constant-arrival-rate` / `ramping-arrival-rate`), execução distribuída; nunca em produção; tokens de teste via secret/env.

| Cenário | Perfil | Aceite (candidato) |
|---|---|---|
| Smoke | 1–5 VU, 2 min | 0 erros de negócio |
| Baseline | Pico normal B, 30 min | SLOs de §7; erro < 0,1% |
| Stress | Até 2× pior dia ou saturação | 429/503 com Problem Details, sem cascata, sem perda/duplicidade |
| Spike | 0 → 3× pico em 30 s | p99 recuperado em ≤ 2 min |
| Soak | 70% do pior dia, 8–24 h | Deriva de p99 < 10%; lag e memória estáveis |
| Capacity | Degraus de +10% | Curva throughput × latência por pod/shard |
| Hot account | Concorrência na mesma conta e na contrapartida | 0 violação de saldo; journal balanceado |
| Idempotência/replay | 5–10% duplicadas, incluindo timeout pós-commit | 0 duplicidade no ledger; payload divergente → conflito |
| Failover | Queda de writer, AZ e região sob carga | 0 split-brain; UNKNOWN resolvidos; reconciliação com diferença 0; RTO medido |

Dados sintéticos: ≥ 1M contas fictícias com popularidade Zipf; cartões de teste sem PAN real. Checks de **status de negócio** (APPROVED/DECLINED/UNKNOWN), não só HTTP 200. Reconciliação obrigatória após cada teste.

## 7. SLIs/SLOs de desempenho candidatos
Harmonização com as NFRs: ver `docs/architecture/etapa1-consolidacao.md` §2 (conflito CF-03).

| SLI | SLO candidato |
|---|---|
| Autorização de cartão | p95 ≤ 150 ms, p99 ≤ 250 ms |
| Comando Pix/transferência | p95 ≤ 300 ms, p99 ≤ 600 ms |
| Saldo | p95 ≤ 100 ms, p99 ≤ 200 ms |
| Extrato (1ª página) | p95 ≤ 150 ms, p99 ≤ 300 ms |
| Emissão de token | p95 ≤ 200 ms |
| Frescor do extrato | p99 ≤ 5 s; degradado ≤ 60 s |
| Idade do outbox mais antigo | p99 ≤ 2 s |
| Commit do ledger | p99 ≤ 40 ms |
| UNKNOWN em autorização | ≤ 0,01%; 100% resolvidos em ≤ 24 h (validar) |
| Erro técnico | ≤ 0,1% em 28 dias |

Disponibilidade, correção financeira e frescor com error budgets separados.

## 8. Findings
PERF-001..PERF-012: ver `docs/governance/agent-communications.jsonl` e `docs/architecture/etapa1-consolidacao.md`.

## 9. Dados que o negócio precisa fornecer
Base atual e projetada (12/36 meses) e % de ativos por canal; volumes por tipo e por hora em pelo menos 13 meses (Black Friday, Natal, dias de pagamento); picos históricos por minuto e calendário promocional; top-N contas por volume; payload médio e ciclo de vida do cartão; inventário de batches; timeouts e stand-in das bandeiras; SLAs do SPI (Compliance); retenção legal (Jurídico); residência de dados e regiões candidatas com RTT medido.
