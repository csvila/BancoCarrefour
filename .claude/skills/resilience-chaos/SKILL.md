---
name: resilience-chaos
description: "Planeja testes de caos, failover e recuperação para sistemas financeiros. Use para validar tolerância a falhas e DR."
---

# Chaos Engineering e DR

- Definir hipótese, blast radius, critérios de abortar, ambiente, observadores e rollback antes do experimento.
- Começar em ambiente não produtivo; produção somente com autorização formal e janela aprovada.
- Injetar latência, perda de pacote, DNS, falha de pod/nó/AZ, indisponibilidade de DB/broker, rotação de chaves e falha do core.
- Verificar fencing, split-brain, RPO, RTO, idempotência, UNKNOWN, reconciliação e consistência do extrato.
- Nunca injetar caos em sistemas financeiros reais sem plano aprovado e limites de exposição.
- Registrar hipótese, experimento, resultado, evidência, desvios e ações corretivas.
