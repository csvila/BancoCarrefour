---
name: financial-resilience
description: "Aplica padrões de consistência, idempotência, ledger, contingência e reconciliação em transações bancárias. Use em débito, crédito, pagamentos, saldo, autorização e failover."
---

# Resiliência financeira

## Invariantes
- Ledger de partidas dobradas, append-only; correções por reversão/compensação.
- Saldo contábil, disponível, bloqueado e reservado separados.
- Comando financeiro com idempotency key estável, escopo, payload hash e retenção definida.
- Mesmo idempotency key + mesmo payload retorna resultado anterior; payload diferente é conflito.
- Persistir resultado da autorização, lançamentos, reserva e Outbox atomicamente quando pertencem à mesma unidade transacional.
- Não afirmar exactly-once ponta a ponta; usar at-least-once, deduplicação e idempotência.
- Commit durável antes de confirmar sucesso ao canal. Timeout após envio pode resultar em UNKNOWN; consultar/repetir com a mesma chave.
- Broker e cache não são autoridade financeira.

## Débito
Validar identidade, escopo, risco, bloqueios, saldo disponível, limite e concorrência; criar reserva/lançamento de forma atômica; publicar evento via Outbox. Diferenciar autorização, captura, liquidação, reversão e estorno.

## Crédito
Validar origem, autenticidade, liquidação e disponibilidade; diferenciar crédito recebido, contabilizado, bloqueado e disponível. Nunca tornar disponível apenas por receber mensagem.

## HA/DR
- Autoridade única de escrita por conta/partição.
- Fencing token monotônico; leases sem fencing não bastam.
- Impedir split-brain. Não promover réplica assíncrona como se RPO=0.
- Failover só após isolamento do antigo writer e verificação do estado durável.
- Quotas/limites de contingência devem ser pré-alocados, não duplicados entre regiões.

## Contingência
CAE pode aprovar apenas operações cobertas por reservas/quotas assinadas, limites por conta/canal/região, controles antifraude, validade e journal durável. Caso contrário retornar DECLINED/UNAVAILABLE/UNKNOWN conforme semântica, nunca aprovação implícita.

## Reconciliação
Reprocessamento idempotente, sequência monotônica, validação de assinatura, matching por transaction ID, detecção de duplicidade e trilha de exceção.
