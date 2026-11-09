---
name: performance-engineering
description: "Analisa e otimiza latência, throughput, capacidade, filas e bancos de dados. Use em sizing, testes de carga e incidentes de lentidão."
---

# Performance engineering

- Construir modelo de carga com média, pico, burst, sazonalidade, concorrência e distribuição por cliente/conta.
- Não derivar infraestrutura somente de número de clientes. Medir TPS, payload, fan-out, custo de DB, eventos/transação, retenção e crescimento.
- Definir orçamento de latência por hop e SLO p95/p99.
- Medir throughput sustentável, saturação, queue depth, consumer lag, pool de conexões, lock contention, GC, allocations e I/O.
- Testar cold start, steady state, spike, soak, failover e recovery.
- Verificar N+1, índices, query plans, serialização, backpressure e retries amplificadores.
- Calcular headroom e capacidade N-1 (perda de uma zona/região) explicitamente.
- Otimizações propostas devem incluir benchmark antes/depois e risco de consistência.
- Não sacrificar durabilidade, autorização, criptografia ou auditoria para reduzir latência.
- Agente read-only: solicita alterações ao Developer e novos testes ao Test Analyst.
