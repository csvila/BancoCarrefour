# ADR-0009 — Estilo arquitetural: monolito modular cortado por limite de consistência

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09)
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumo de software-architect (D1), revisão de custo operacional por devops-platform
- Revisores: usuário (aprovado em 2026-11-09)

## Contexto
O CLAUDE.md pede Clean Architecture/vertical slices "quando fizer sentido" e proíbe abstrações sem benefício. As invariantes financeiras exigem atomicidade entre conta, saldo, hold, limite, idempotência e outbox.

## Problema
Definir a granularidade dos deployables.

## Alternativas consideradas
1. **Microsserviços por bounded context (13+):** consistência distribuída (2PC/sagas onde hoje basta uma transação) e custo operacional alto.
2. **Monolito único:** mistura CDE, PII e Core; raio de impacto total; escalonamento único.
3. **Monolito modular por limite de consistência, com ~13 deployables:** cada separação justificada por autoridade, conformidade, disponibilidade ou segmento de rede.

## Decisão
Alternativa 3:
- **Core Banking** (Accounts + Ledger + Holds + limite + Idempotência + Outbox) é um deployable particionado.
- Separados com justificativa explícita: Card Authorization (CDE), Party (cofre de PII), CAE (regional), Audit (WORM), Payments (workflows externos), Risk, Legacy ACL, Statement (query/projector), Recon/Settlement, Notifications, IAM, BFF/Device Gateway.
- Internamente: vertical slices por caso de uso; fronteiras de módulo verificadas por testes de arquitetura (ex.: NetArchTest), incluindo a regra "caminho de débito não depende de cache/read model".
- Contratos internos em gRPC versionado; externos em REST `/v1` com OpenAPI e Problem Details (RFC 9457).

## Consequências positivas
Invariantes resolvidas por transação local; menos componentes que microsserviços; isolamento de PCI e PII.

## Consequências negativas e trade-offs
O Core Banking concentra lógica crítica (disciplina de módulos necessária); ~13 deployables ainda exigem plataforma madura (GitOps, mesh).

## Segurança, privacidade e conformidade
Reduz escopo PCI (Card isolado) e LGPD (PII no Party).

## Operação, performance e DR
Escala por papel; bulkheads por deployable; prioridade de PriorityClass para Core/Card/CAE.

## Plano de migração e rollback
Extrair módulo para deployable só com nova ADR e justificativa.

## Critérios de validação
Testes de arquitetura no CI; revisão de DevOps sobre o custo operacional; nenhum 2PC no código.

## Referências e ADRs relacionados
ADR-0003, 0010, 0012, 0013. `docs/architecture/bounded-contexts.md` §1.
