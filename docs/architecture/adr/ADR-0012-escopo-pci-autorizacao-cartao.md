# ADR-0012 — Escopo PCI DSS e modelo de autorização de cartão

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09) — Card Auth token-only, preferência pela alternativa 2; escopo final sujeito ao papel de emissor e ao QSA
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de solution-architect (P-01, P-09), software-architect (BC-06), security-privacy (§8), devops-platform
- Revisores: usuário (aprovado em 2026-11-09), negócio, Compliance/QSA

## Contexto
Os insumos divergem quanto a onde ficam o PAN e a decisão de emissor:
- **solution-architect:** PAN e CDE na processadora; a PBD só vê tokens. Na Fase 1 a processadora decide com parâmetros; na Fase 2 a PBD decide.
- **software-architect:** Card Authorization, Token Vault e HSM próprios no CDE (Z4), com decisão do emissor na PBD.
- **security-privacy e devops-platform:** CDE em cluster/node pools dedicados; tokenização o mais cedo possível.

## Problema
Minimizar o escopo PCI sem impedir que o banco controle a decisão de emissor quando isso for estratégico.

## Alternativas consideradas
1. **Processadora decide e tokeniza (PBD fora do CDE).** Menor escopo; menos controle sobre a decisão e a latência.
2. **PBD decide como emissor usando tokens da processadora (vault na processadora).** Controle de decisão com escopo reduzido; Card Authorization recebe apenas tokens. Avaliar com o QSA se componentes "connected-to" entram no escopo.
3. **Vault, HSM e Card Authorization próprios (CDE interno).** Controle máximo e maior escopo PCI e custo.

## Decisão
- **Desenhar o Card Authorization para operar com tokens** (sem PAN), o que é compatível com 1 e 2.
- **Preferir a alternativa 2** se a processadora oferecer tokenização e encaminhamento da decisão; caso contrário, a 1 na Fase 1.
- A alternativa 3 só com justificativa de negócio e ADR própria.
- **Em qualquer caso:**
  - CDE (o que existir) em **cluster dedicado** (preferível) ou node pools dedicados, com NetworkPolicy/firewall, egress restrito e teste de segmentação periódico.
  - SAD nunca armazenado após a autorização (sem usar a exceção de emissor).
  - PIN apenas em PED/HSM (DUKPT/TR-31).
  - Ledger, read models, Payments e observabilidade fora do escopo (só tokens).
  - Arquivos de clearing processados no CDE ou recebidos tokenizados.
  - O Card Authorization fica co-localizado com a home da partição (latência; capacity-model §3.1).

## Consequências positivas
Escopo PCI minimizado; o caminho para a decisão própria fica preservado.

## Consequências negativas e trade-offs
Dependência contratual e de latência da processadora; a fronteira exata do escopo só é fechada com o QSA.

## Segurança, privacidade e conformidade
TM-025/026/027/032; SEC-REQ-024. PCI DSS v4.0.1, PCI PTS POI e PCI PIN [VALIDAR QSA].

## Operação, performance e DR
Card Authorization com bulkhead dedicado (capacity-model §4.1); STIP conforme ADR-0005.

## Plano de migração e rollback
Da Fase 1 (processadora decide) para a Fase 2 (PBD decide) por BIN/produto, com canário.

## Critérios de validação
Documento de escopo aprovado pelo QSA; pentest de segmentação; scanner de PAN fora do CDE com 0 ocorrências.

## Referências e ADRs relacionados
ADR-0005, 0009, 0011. `docs/security/threat-model.md` §8. Dúvidas de negócio: papel emissor/adquirente, processadora, rede ATM.
