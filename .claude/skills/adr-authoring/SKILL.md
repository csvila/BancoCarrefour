---
name: adr-authoring
description: "Escreve Architecture Decision Records com contexto, alternativas, decisão e consequências. Use antes de decisões técnicas relevantes ou mudanças de arquitetura."
---

# Architecture Decision Records

Guardar em `docs/architecture/adr/ADR-NNNN-titulo.md`. Use numeração sequencial.

## Template obrigatório
- Título e status: Proposed / Accepted / Superseded / Deprecated.
- Data, autores, revisores e agentes consultados.
- Contexto e problema.
- Requisitos funcionais e NFRs afetados.
- Restrições e premissas.
- Alternativas consideradas (mínimo duas, incluindo manter estado atual quando aplicável).
- Decisão e justificativa.
- Consequências positivas e negativas.
- Riscos, mitigação e plano de reversão.
- Impactos em segurança, LGPD, PCI DSS, operação, custo, performance e DR.
- Diagramas C4 e ADRs relacionados.
- Critérios de validação e evidências.

## Regras bancárias
Decisões sobre consistência, single-writer, failover, ledger, idempotência, reservas, autorização offline, tokenização, retenção e localização de dados exigem ADR. Não declarar consenso sem revisão dos responsáveis.

Arquitetos propõem; orquestrador consolida e solicita persistência. Não implementar decisão Proposed.
