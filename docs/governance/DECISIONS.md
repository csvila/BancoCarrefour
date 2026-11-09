# Índice de decisões

| ID | Decisão | Status | Condições / pendências |
|---|---|---|---|
| ADR-0001 | Modelo de agentes e governança de escrita | Accepted | Complementada pela ADR-0014 (Proposed) |
| ADR-0002 | Topologia active-active com autoridade única por partição (Fase 1 = B como A degenerado) | Accepted (2026-11-09) | — |
| ADR-0003 | Ledger de partidas dobradas, Outbox e idempotência | Accepted (2026-11-09) | Retenção de idempotência [VALIDAR janelas externas] |
| ADR-0004 | CQRS para extrato | Accepted (2026-11-09) | — |
| ADR-0005 | Contingency Authorization Engine e quotas | Accepted (2026-11-09) | Risco confirma política de exposição e CF-07 antes de ativar em produção |
| ADR-0006 | Bearer via OAuth2/OIDC com IdP externo | Accepted (2026-11-09) | Escolha do produto de IdP (ADR futura) |
| ADR-0007 | Core híbrido: SoR único por conta + strangler | Accepted (2026-11-09) | Validar interfaces do core legado (P-02) |
| ADR-0008 | Replicação do ledger e RPO por classe de falha | Accepted (2026-11-09) | Fase 1 = B; RPO regional ≤ 5 s [VALIDAR]; reavaliar B+ após RTT medido |
| ADR-0009 | Estilo: monolito modular por limite de consistência | Accepted (2026-11-09) | — |
| ADR-0010 | Hot accounts e transferências entre partições | Accepted (2026-11-09) | Calibrar intervalo de netting |
| ADR-0011 | Classificação de dados, PII fora do ledger e residência | Accepted (2026-11-09) | Residência e retenção [VALIDAR Compliance/Jurídico] |
| ADR-0012 | Escopo PCI e modelo de autorização de cartão | Accepted (2026-11-09) | Papel de emissor e escopo final com QSA |
| ADR-0013 | Plataforma .NET 10 LTS / C# 14 e baseline de build | Accepted (2026-11-09) | Confirmar política de suporte em fonte oficial |
| ADR-0014 | Governança técnica do time de agentes | **Proposed** | Decisão explícita do usuário |

Consolidação, conflitos e findings: `docs/architecture/etapa1-consolidacao.md`.
