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

## Etapa 2 (em andamento, 2026-11-09)
- **Arquivos de build na raiz** (decisão do usuário, DEV-001/TA-005 resolvidos): `global.json`, `BancoCarrefour.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `NuGet.config`, `.editorconfig`. Build a partir da raiz com 0 avisos. Edições futuras nesses arquivos exigem autorização do usuário até a ADR-0014.
- **Skeleton (developer):** `src/` com CPM, NuGet source mapping, lock files, ServiceDefaults (JwtBearer fail-closed, Problem Details, correlation-id, OTel, health), CoreBanking.Host (gRPC; módulos Ledger/Accounts vazios) e Payments.Api (`/v1`). Build Release com 0 avisos e 0 erros, verificado pelo orquestrador. Não commitado.
- **Plano de testes (test-analyst):** `tests/TEST-PLAN.md` com 154 casos, rastreabilidade com SEC-REQ e ADRs. Nenhum teste implementado.
- **Testes:** pacotes aprovados (xunit.v3, Mvc.Testing, Playwright + Xunit.v3, Testcontainers.PostgreSql, TimeProvider.Testing, NetArchTest, Shouldly) cadastrados pelo developer no `Directory.Packages.props` da raiz. A execução é feita só pela Microsoft Testing Platform (`dotnet test --solution BancoCarrefour.slnx`); os pacotes VSTest foram removidos. `.gitattributes` com LF.
- **Testes F0 (test-analyst):** 4 projetos em `tests/` com 225 testes: 216 passaram, 0 falharam, 9 em Skip. Cada Skip corresponde a um defeito de produção (TA-013..TA-019). Execução verificada pelo orquestrador em 2026-11-09.
  - **Mais grave: TA-015 (Alta).** Chave revogada continua aceita.
  - **TA-019 (Média):** aud/iss ampliáveis por configuração.
  - **TA-015 e TA-019 corrigidos** pelo developer com autorização do usuário, junto com o DEV-022. Testes incluídos na `.slnx`, `tests/TestResults/` no `.gitignore`. DEV-023 corrigido (refresh do JWKS a cada 5 min). Testes reativados como gate: suíte com 235 testes, 0 falhas e 6 em Skip (TA-013/014/017/018), verificada pelo orquestrador.
- **Decisões abertas:** DEV-026 (Alta: idade máxima da configuração do JWKS com o IdP fora) e DEV-027/028; TA-013/014/017/018 (correção ou ajuste de ADR); DEV-025 (registrar na ADR-0006); aprovar dependências transitivas usadas diretamente pelos testes; gate `--minimum-expected-tests` no CI (DEV-017), framework dos testes de governança (D-1, ligado à ADR-0014), regra 404/403 (TA-004), semânticas de Risco (TA-006), lista de dados sintéticos (TA-007).

## Próximos passos
Etapa 2: C3 dos caminhos críticos, threat model v0.2 contra o C2, solution skeleton pelo `developer`, plano de testes pelo `test-analyst`.
