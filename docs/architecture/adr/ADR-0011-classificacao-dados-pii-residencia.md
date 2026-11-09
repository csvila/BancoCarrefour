# ADR-0011 — Classificação de dados, PII fora do ledger e residência

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09) — residência e retenção permanecem [VALIDAR] com Compliance/Jurídico
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de solution-architect (SA-010), security-privacy, software-architect (SW-011)
- Revisores: usuário (aprovado em 2026-11-09); Encarregado/Jurídico/Compliance

## Contexto
O ledger é imutável, mas a LGPD prevê direitos do titular e eliminação quando cabível. Logs, eventos e fornecedores SaaS precisam de uma regra objetiva de minimização. O provedor e a região ainda não estão definidos (TM-037).

## Problema
Classificar dados, separar a identificação pessoal da trilha contábil imutável e definir a localização sem inventar requisitos regulatórios.

## Alternativas consideradas
1. **PII no ledger.** Rejeitada: conflita com a eliminação/anonimização e amplia a exposição.
2. **IDs pseudônimos no ledger/eventos + PII no Party + crypto-shredding quando cabível.** Recomendada.
3. **Decidir caso a caso por fornecedor, sem política.** Rejeitada: inconsistente.

## Decisão
- **Classes:** C0 público, C1 interno, C2 pessoal, C3 pessoal financeiro (pessoal, **não** automaticamente sensível no sentido do art. 5º II), C3-S sensível (biometria etc.), C4 PCI, S segredos (regras em `c4/01-context.md` §8).
- Ledger, eventos, audit e read models usam **IDs pseudônimos** (`accountId`, `partyId`). Nome, CPF e contatos ficam no **Party** (colunas cifradas com KMS, hash chaveado para busca).
- **Crypto-shredding** da camada de identificação quando a eliminação for devida **e** compatível com as obrigações de guarda [VALIDAR Jurídico].
- Proibidos em logs, traces e eventos: CPF completo, PAN, CVV, PIN, tokens, senhas (NFR-081). Redação na origem e scanner no CI.
- **Residência:** até parecer de Compliance, dados C2+ (incluindo backups e réplicas) ficam em sites no Brasil. Fornecedores SaaS com processamento no exterior só com base do art. 33 validada [VALIDAR].
- **Retenção:** matriz por categoria definida por Jurídico/Compliance; nenhum prazo é presumido.
- RIPD recomendado antes do go-live (biometria, antifraude, perfilamento).

## Consequências positivas
Direitos do titular sem tocar o ledger; menor superfície de vazamento; base objetiva para logs e fornecedores.

## Consequências negativas e trade-offs
Joins com PII exigem chamada ao Party; gestão de chaves por titular para crypto-shredding; a restrição de residência pode limitar provedores e o DR (ADR-0008).

## Segurança, privacidade e conformidade
Cobre TM-017, TM-037, TM-038, TM-039; SEC-REQ-017/028/029. Itens [VALIDAR] encaminhados a Jurídico/Compliance.

## Operação, performance e DR
Backups cifrados com chave distinta; teste de eliminação; DLP no pipeline de logs.

## Plano de migração e rollback
Migração do legado para o Party com minimização (ADR-0007).

## Critérios de validação
Scanner de PII/PAN em logs, eventos e fixtures; teste de crypto-shredding; inventário de stores classificados (NFR-080).

## Referências e ADRs relacionados
ADR-0004, 0006, 0007, 0008, 0012. `docs/security/threat-model.md` §7. LGPD arts. 5º, 7º, 11, 18, 20, 33, 37, 38, 48 (citações indicativas, [VALIDAR]).
