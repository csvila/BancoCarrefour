---
name: observability-otel
description: "Define observabilidade distribuída com OpenTelemetry, métricas, logs, traces, SLOs e alertas. Use ao criar serviços e analisar incidentes."
---

# Observabilidade OpenTelemetry

- Instrumentar traces, metrics e logs correlacionados; propagar W3C Trace Context.
- Correlation ID e Transaction ID opacos; não usar CPF, conta, cartão ou token como label.
- Métricas RED para APIs e USE para infraestrutura.
- Métricas de negócio: approved/declined/unknown, idempotency replay, outbox lag, consumer lag, reconciliação pendente, reservas, divergência de saldo.
- Histogramas para latência; p50/p95/p99; evitar cardinalidade alta.
- Logs estruturados, sem secrets/PII/payloads completos; retenção e acesso por classificação.
- Alertas acionáveis com severidade, runbook, owner e impacto.
- Dashboards separados: canal, autorização, ledger, extrato, contingência, segurança e DR.
- SLOs e error budgets; distinguir disponibilidade, correção financeira e frescor do extrato.
- Amostragem de traces sem perder eventos de erro/alto risco conforme política.
