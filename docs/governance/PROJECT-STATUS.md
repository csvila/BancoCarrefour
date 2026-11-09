# Status do projeto

- Atualizado: 2026-11-09 (Etapa 1)
- Estado: **Etapa 1 concluída. ADRs 0002–0013 aprovadas pelo usuário em 2026-11-09 (Accepted, algumas com condições).** ADR-0014 (governança técnica) segue Proposed. Implementação liberada no escopo das ADRs aceitas; alterações em `.claude/`/`scripts/` continuam bloqueadas até decisão sobre a ADR-0014.
- Escopo: plataforma bancária resiliente, core híbrido, 1–10 milhões de clientes.
- Stack: C# 14 / .NET 10 (SDK 10.0.403 verificado; LTS a confirmar em fonte oficial: ADR-0013), Bearer/OIDC, xUnit, Playwright .NET, k6, NGINX, Docker, Kubernetes.
- Agentes: 8 definidos em `.claude/agents/`; 5 acionados na Etapa 1 (solution-architect, software-architect, security-privacy, devops-platform, performance-analyst).
- Regra de escrita: somente `developer` altera produção/infra executável; somente `test-analyst` altera `tests/**`. Demais agentes read-only. Reforço técnico proposto na ADR-0014.

## Artefatos da Etapa 1 (todos Proposed/Draft)
C1 e C2 (`docs/architecture/c4/`), bounded contexts, NFRs, threat model, topologia, modelo de capacidade, ADR-0002..0014 e consolidação (`docs/architecture/etapa1-consolidacao.md`).

## Decisões pendentes
1. ADR-0014: mudanças de governança (usuário).
2. Condições das ADRs aceitas: RPO regional e B+ (ADR-0008, após RTT); papel no cartão e escopo com QSA (ADR-0012); política de exposição e CF-07 por Risco antes de ativar o CAE em produção (ADR-0005); residência/retenção (ADR-0011); política de suporte .NET em fonte oficial (ADR-0013).
3. C1/C2 seguem v0.1; revisão v0.2 na Etapa 2 (CF-09, threat model v0.2).

## Riscos em aberto
- **Críticos (com ADR proposta):** autoridade legado × ledger (ADR-0007); desvio ao CAE após timeout pós-commit (ADR-0005); hot accounts (ADR-0010).
- **Altos:** RPO/topologia e residência no Brasil (ADR-0008/0011); STIP da bandeira; ausência de dados reais de volume (modelo paramétrico, erro de até 10×); governança de agentes aplicada só no cliente (G-01, G-05).
- Requisitos regulatórios (Pix, Open Finance, BCB/CMN, LGPD, PCI) **não validados**; perguntas consolidadas na §5 da consolidação.

## Próximos passos
Etapa 2: C3 dos caminhos críticos, threat model v0.2 contra o C2, solution skeleton pelo `developer`, plano de testes pelo `test-analyst`.
