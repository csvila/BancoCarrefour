---
name: testing-k6
description: "Cria e executa testes de carga k6 para APIs e fluxos bancários. Use para baseline, stress, spike, soak e failover."
---

# Testes de carga k6

- Scripts em `tests/Performance/k6/*.js`; não usar k6 como teste unitário.
- Cenários: smoke, load, stress, spike, soak, capacity e failover.
- Usar dados sintéticos e tokens de teste obtidos de secret/env; nunca commitar token.
- Testar autorização, consulta de extrato, idempotência, concorrência por conta e degradação.
- Definir thresholds de p95/p99, error rate, checks, throughput e timeouts; thresholds devem vir dos SLOs aprovados.
- Não gerar carga financeira real nem usar endpoints de produção sem autorização explícita e plano de segurança.
- Distribuir contas/partições para evitar que o teste meça apenas contenção de uma conta. Incluir cenário deliberado de concorrência na mesma conta.
- Registrar versão do script, ambiente, dados, parâmetros, commit, resultado e limitações.
- Não interpretar sucesso HTTP como sucesso financeiro: validar status de negócio.
