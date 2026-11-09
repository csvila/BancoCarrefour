# Etapa 1 — Consolidação da Arquitetura Inicial

- Data: 2026-11-09 · Orquestrador
- Insumos: solution-architect (C1 + NFR), software-architect (C2 + bounded contexts), security-privacy (threat model), devops-platform (topologia), performance-analyst (capacidade)
- Estado: **ADRs 0002–0013 aprovadas pelo usuário em 2026-11-09** (Accepted, com as condições registradas em `docs/governance/DECISIONS.md`). ADR-0014 segue Proposed.

## 1. Artefatos produzidos

| Artefato | Caminho | Status |
|---|---|---|
| C1 Contexto | `docs/architecture/c4/01-context.md` | Proposta v0.1 |
| C2 Containers | `docs/architecture/c4/02-containers.md` | Proposta v0.1 |
| C3 Componentes | `docs/architecture/c4/03-components.md` | Pendente (Etapa 2: autorização, ledger, extrato, CAE, IAM) |
| C4 Código | `docs/architecture/c4/04-code.md` | Pendente (derivado do código real) |
| Bounded contexts + fluxos | `docs/architecture/bounded-contexts.md` | Proposta v0.1 |
| NFRs | `docs/architecture/nfr.md` | Proposta v0.1 |
| Threat model | `docs/security/threat-model.md` | Draft v0.1 |
| Topologia | `docs/infra/topology.md` | Proposta v0.1 |
| Capacidade | `docs/performance/capacity-model.md` | Proposta v0.1 |
| ADRs | `docs/architecture/adr/ADR-0002..0014` | Proposed |

## 2. Conflitos identificados e resolução proposta

| ID | Tema | Posições | Resolução proposta | Fecha em |
|---|---|---|---|---|
| **CF-01** | RPO na perda de região | SA: RPO=0 com 3 sites (A), ao menos para transferências internas. DevOps: assíncrono (B). Perf: sync inter-região inviável para hot accounts sem sub-contas. SW: assíncrono + RECOVERY | **Fase 1 = B** com RPO declarado (≤ 5 s [VALIDAR]); medir RTT e avaliar **B+** (ack após replicação) para transferências internas; A só se o negócio exigir | **ADR-0008 — decisão do negócio** |
| CF-02 | Terminologia de topologia | SA "active-active com home por partição"; DevOps "B como A degenerado" | Compatíveis: mesmo modelo, Fase 1 com todas as homes em R1 | ADR-0002 (resolvido) |
| **CF-03** | Alvos de latência | NFR (SA) × SLO candidato (Perf), ver tabela abaixo | Tabela harmonizada abaixo, que prevalece até a aprovação | Aprovação de SLOs (PERF-006) |
| CF-04 | Capacidade e proporção leitura/escrita | SA: exemplo ilustrativo, r = 8 leituras por escrita. Perf: 40–50% de escrita (cartão = reserva) | **Modelo do performance-analyst prevalece**; NFR §4 atualizado com nota | Dados reais (PERF-001) |
| CF-05 | Número de partições lógicas | SW: 4096. Perf: 256–1024 virtuais | **4096 lógicas** (mais granularidade de rebalance, overhead baixo); Perf valida o custo de `partition_control` por shard | ADR-0002 |
| **CF-06** | Escopo do CDE e quem decide no cartão | SA: PAN na processadora; Fase 1 processadora decide. SW: Card Auth + Vault + HSM próprios. Sec/DevOps: CDE dedicado | Card Auth **token-only**; preferir decisão na PBD com vault da processadora (alt. 2); CDE próprio só com justificativa | **ADR-0012 — negócio + QSA** |
| **CF-07** | Resposta à rede em timeout do Core | SW: recusa externa + UNKNOWN interno + ReleaseHold. CLAUDE.md: UNKNOWN ≠ DECLINED | Recusa externa como resposta segura ao arranjo, **sem apagar** o UNKNOWN interno; reconciliação obrigatória | **ADR-0005 — aprovação de Risco** |
| CF-08 | Controlador NGINX | DevOps: `kubernetes/ingress-nginx` comunitário anunciou retirada [VALIDAR]. CLAUDE.md exige NGINX | Avaliar F5 NGINX Ingress Controller vs. NGINX Gateway Fabric (Gateway API) | ADR futura (Etapa 2) |
| CF-09 | Numeração das zonas de confiança | C1 (TB0–TB8), threat model (TB0–TB10), C2 (Z0–Z7) | Unificar numeração na v0.2 | Revisão C1/C2/TM v0.2 |
| CF-10 | Código HTTP para chave reutilizada | SW: 422 (divergente), 409 (em andamento). Sec/SA: "409/422" | **422 divergente, 409 em andamento** | ADR-0003 |
| CF-11 | Store de fencing | DevOps: consenso dedicado ou tabela no DB. SW: etcd | **etcd** para directory/lease **+ validação obrigatória** em `partition_control` no shard | ADR-0002 |
| CF-12 | Escopos de escrita da governança | Guard (`src/`, `infra/`, `deploy/`) × CLAUDE.md (`developer` também em docs vinculados; sem `deploy/`) | Alinhar em decisão única | **ADR-0014 — usuário** |
| CF-13 | CAE na borda (terminal/totem) | DevOps [VALIDAR]; SA quotas por dispositivo; SW CAE por região | Fora da Fase 1 até política de Risco + attestation | ADR-0005 |

### Tabela harmonizada de latência (proposta, CF-03)

| Operação | NFR (SA) | SLO candidato (Perf) | **Proposta consolidada** | Base |
|---|---|---|---|---|
| Autorização de cartão | 40 / 100 / 250 | 60 / 150 / 250 | **p95 ≤ 150, p99 ≤ 250 ms** | Orçamento por hop soma 78/145 ms; p99 coincide; prazo das bandeiras [VALIDAR] |
| Pix/transferência (comando aceito) | p95 250 / p99 500 | p95 300 / p99 600 | **p95 ≤ 250, p99 ≤ 500 ms** | Orçamento p99 = 208 ms; adota o mais estrito, com folga |
| Saldo | p95 100 / p99 250 | p95 100 / p99 200 | **p95 ≤ 100, p99 ≤ 200 ms** | Mais estrito, factível |
| Extrato 1ª página (cache) | — | p95 150 / p99 300 | **p95 ≤ 150, p99 ≤ 300 ms** | Cache hit 80–90% |
| Extrato ≤ 50 itens / ≤ 90 dias | p95 300 / p99 800 | — | **p95 ≤ 300, p99 ≤ 800 ms** | Cache miss com keyset |
| Frescor do extrato | p95 1 s / p99 5 s | p99 5 s | **p95 ≤ 1 s, p99 ≤ 5 s** | Convergem |
| Idade do outbox | p99 ≤ 1 s | p99 ≤ 2 s | **p99 ≤ 2 s** (alerta > 5 s) | Watermark de replicação (ADR-0003/0008) adiciona latência |

## 3. Findings consolidados (59 findings brutos → temas)

### 3.1 Arquitetura e domínio

| ID consolidado | Sev. | Findings de origem | Encaminhamento | Bloqueia implementação? |
|---|---|---|---|---|
| A-01 Autoridade legado × ledger | **Crítica** | SA-001, SW-002, TM-030 | ADR-0007 | Sim |
| A-02 Desvio ao CAE após timeout pós-commit | **Crítica** | SW-003, TM-015/016 | ADR-0005 | Sim (CAE) |
| A-03 Hot accounts | **Crítica** | PERF-002, SW-005 | ADR-0010 | Sim (ledger) |
| A-04 ADRs ausentes / C4 e NFR vazios | Alta | SA-002/003/004, SW-001, PERF-003, SEC-013 | **Resolvido nesta etapa** (textos Proposed) | — |
| A-05 RPO inter-região / topologia | Alta | SA-005, PERF-004, SW-012, OPS-012 | ADR-0008 (negócio) | Sim (DR) |
| A-06 Contador de limite fora da transação | Alta | SW-006 | ADR-0003 | Sim |
| A-07 Stand-in (STIP) da bandeira | Alta | SW-004, RC-05 | ADR-0005 | Cartão |
| A-08 Retry budget / admission control | Alta | PERF-005 | ADR-0003 | Não (baseline de código) |
| A-09 Sem dados reais de volume | Alta | PERF-001 | Negócio | Não (modelo paramétrico) |
| A-10 Crédito disponível antes da liquidação | Média | SW-010 | ADR-0003 | Sim (Pix) |
| A-11 Classificação de dados / PII × ledger | Média | SA-010, SW-011 | ADR-0011 | Não |
| A-12 Target framework / `global.json` | Média | SW-008 | ADR-0013 | Skeleton |
| A-13 Demais itens | Média/Baixa | PERF-006..012, SW-007, SW-009, SW-013 | Etapa 2 (C3) e test-analyst após SLOs | Não |

### 3.2 Requisitos de segurança e testes
42 ameaças (TM-001..042) e 30 requisitos testáveis (SEC-REQ-001..030) viram critérios de aceite das ADRs 0002–0006, 0011 e 0012. **Handoff futuro ao test-analyst:** suíte negativa de autorização, replay, concorrência, fencing e UNKNOWN, após o skeleton e as ADRs aceitas.

### 3.3 Governança do time (ADR-0014 — decisão do usuário)

| ID | Sev. | Origem | Situação |
|---|---|---|---|
| G-01 Bash contorna o guard e o deny do JSONL | **Alta** | SEC-001, OPS-003, TM-034 | Aberto |
| G-02 Guard fail-open com entrada malformada | Alta (não verificado) | SEC-002a, OPS-002 | Aberto. Interpretador ausente **não reproduzido** (python3 = 3.15.2) |
| G-03 Lifecycle fora do repositório | Média (rebaixado) | OPS-001, SEC-007, SW-014 | **Verificado:** grava em `%LOCALAPPDATA%\Temp` via Git Bash; funciona, mas fora da trilha |
| G-04 JSONL sem lock (Windows), hash chain, inReplyTo, redação; `from` livre | Média | OPS-007, SEC-004/005/006 | Aberto. **Mitigação nesta sessão:** orquestrador grava sequencialmente; correlação via campo `summary` ("resp. a <id>") |
| G-05 Sem controles no servidor (CODEOWNERS, CI, branch protection); `master`×`main` | Alta | OPS-006, SEC-010 | Aberto (configuração do GitHub pelo usuário) |
| G-06 Escopos inconsistentes (`deploy/`, `docs/`, `.github/`, `scripts/`) | Média | OPS-004/005, SEC-003, SA-007, CF-12 | Aberto |
| G-07 Skill `secure-implementation` inexistente | Baixa | OPS-009, SEC-011, SA-008 | Aberto |
| G-08 Validador do time só estrutural; CRLF | Média | OPS-010, SEC-011 | Aberto |
| G-09 `bypassPermissions` não desabilitado | Média | SEC-012 | Aberto |
| G-10 `.gitignore` incompleto / sem secret scanning | Média | OPS-008, SEC-009 | Aberto (`developer` após aprovação) |
| G-11 Sem hook exigindo `delegation` prévia | Média | SEC-008 | Aberto |
| G-12 WebFetch/WebSearch como vetor de exfiltração | Baixa | OPS-011 | Aberto (política) |
| G-13 Template de ADR incompleto frente à skill | Média | SA-009 | **Corrigido pelo orquestrador nesta etapa** (documental) |
| G-14 Validação prevista na ADR-0001 não executada | Baixa | SEC-014 | Aberto (`test-analyst` após decisão sobre o framework) |

## 4. Decisões pendentes do usuário / negócio
1. **Aprovar (ou ajustar) as ADRs 0002–0013** e a arquitetura C1/C2.
2. **ADR-0008:** apetite de RPO na perda de região (B, B+ ou A).
3. **ADR-0012:** papel de emissor/adquirente; processadora e tokenização.
4. **ADR-0005 (CF-07):** recusa externa à rede em timeout do Core, com UNKNOWN interno (Risco).
5. **ADR-0014:** autorizar mudanças de governança (`.claude/`, `scripts/`, escopos, servidor).
6. Escolha de provedor/sites no Brasil e de IdP (ADRs futuras).

## 5. Perguntas para negócio / Compliance / Jurídico / Risco (consolidadas)
- **Negócio:**
  - Papel no cartão (emissor? adquirente?) e produtos.
  - Core legado: produto, interfaces, janelas batch.
  - Histórico de volumes (≥ 13 meses, picos por minuto, Black Friday, salário) e top-N contas.
  - Apetite de indisponibilidade por jornada.
  - Rede ATM própria ou compartilhada; modelo do Totem.
  - Janela de extrato online.
  - Inventário de batches.
- **Compliance/Jurídico/Encarregado:**
  - Participação no Pix/SPI/STR e regras do arranjo.
  - Open Finance.
  - Normas BCB/CMN de nuvem, residência e continuidade.
  - Matriz de retenção e hipóteses de eliminação frente à imutabilidade.
  - Prazos de direitos do titular e de incidentes.
  - Bases legais por finalidade e RIPD.
  - Escopo e nível PCI (QSA).
  - Reporting regulatório.
  - Acessibilidade.
  - Exigências de bandeira (timeout, stand-in).
- **Risco:**
  - Limites de exposição do CAE e duração offline.
  - Fallback do antifraude.
  - Política de saldo negativo para advices de STIP.
  - CF-07.

## 6. Próximos passos (após aprovação)
1. Etapa 2: C3 dos caminhos críticos (autorização, ledger, extrato, CAE, IAM); revisão cruzada do threat model contra o C2 (v0.2); unificação das zonas de confiança (CF-09).
2. `developer`: solution skeleton conforme ADR-0009/0013 (`global.json`, CPM, analyzers), **somente após Accepted**.
3. `test-analyst`: plano de testes derivado dos SEC-REQ e dos critérios das ADRs; scripts k6 após os SLOs aprovados.
4. Governança: executar o que for autorizado na ADR-0014.
