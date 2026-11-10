# 2026-11-09 — Etapa 2: skeleton e plano de testes

## Delegações
| Agente | Tarefa | messageId | Resultado |
|---|---|---|---|
| developer | ETAPA2-SKELETON | bbb46da0 | Skeleton em `src/`; findings DEV-001..008 |
| test-analyst | ETAPA2-TEST-PLAN | 06c2f849 | `tests/TEST-PLAN.md` (154 casos); findings TA-001..012; handoffs H-01..H-10 |

## Skeleton
- Arquivos de build em `src/` (o guard do developer não permite escrita na raiz; decisão pendente DEV-001/TA-005).
- `.slnx`, `global.json` 10.0.403 (`latestPatch`), `TreatWarningsAsErrors`, NuGetAudit, `InvariantGlobalization`, CPM com pinning transitivo, `packageSourceMapping`.
- ServiceDefaults conforme a ADR-0006: JwtBearer padrão, só RS256/PS256/ES256, skew ≤ 60 s, fallback policy, validação no startup. Inclui também Problem Details, correlation-id, OTel sem query string nem headers, rejeição de token em query (400) e resiliência sem retry em métodos não seguros.
- **Evidência do orquestrador:** `dotnet build BancoCarrefour.slnx -c Release --no-incremental` (em `src/`) → 0 avisos, 0 erros. Nenhuma escrita fora de `src/`. Varredura de segredos sem achados.

## Plano de testes
- 154 casos: 85 P0, 60 P1, 9 P2. Fases: F0 (sobre o skeleton) 38, F1 35, F2 35, F3 33, FG 13.
- **Sondas de governança feitas pelo test-analyst** (em cópias no scratchpad, sem tocar no log real):
  - G-02 confirmado: o guard falha aberto quando recebe JSON com tipo inesperado.
  - G-04 confirmado: append concorrente perde linhas.

## Arquivos de build movidos para a raiz
- Decisão do usuário (messageId d792bc67) com autorização pontual de escrita na raiz. Delegação ao developer: 07023378.
- O developer moveu via `git mv` os seis arquivos de build de `src/` para a raiz, ajustando os caminhos do `.slnx`.
- **Evidência do orquestrador:** `dotnet build BancoCarrefour.slnx -c Release --no-incremental` na raiz → 0 avisos, 0 erros.
- Novos findings: DEV-009 (`.editorconfig` × Markdown), DEV-010 (contexto Docker na raiz), DEV-011 (dono das versões de pacotes de teste).

## Pacotes de teste e `.editorconfig`
- O usuário autorizou o DEV-009 e aprovou os pacotes de teste (messageId 7745f7a8). Delegação ao developer: 0a2c6f18.
- `.editorconfig`: nova seção `[*.md]` com `trim_trailing_whitespace = false`.
- `Directory.Packages.props`: 10 pacotes de teste, todos com licença OSI permissiva. Asserções com Shouldly; FluentAssertions e coverlet não incluídos.
- **Evidência:** restore dos 10 pacotes num projeto descartável sem vulnerabilidades nem conflitos. Build na raiz com 0 avisos, refeito pelo orquestrador.
- Novos findings: DEV-012 (xUnit v3 4.x exige Microsoft Testing Platform no `global.json`; `dotnet test` falha até isso ser ajustado), DEV-013 (`.gitattributes`), DEV-014 (pacotes VSTest dispensáveis), DEV-015 (navegadores do Playwright no CI), DEV-016 (Docker no CI e imagem fixada por digest).

## Runner de testes, `.gitattributes` e remoção do VSTest
- O usuário autorizou o DEV-012 e o DEV-013 e decidiu remover os pacotes VSTest (DEV-014), messageId 3fc64db1. Delegação ao developer: f3677949.
- `global.json` passa a usar `"test": { "runner": "Microsoft.Testing.Platform" }`.
- `xunit.runner.visualstudio` e `Microsoft.NET.Test.Sdk` saíram do `Directory.Packages.props`.
- Novo `.gitattributes`: `* text=auto eol=lf`, CRLF para `.ps1/.cmd/.bat` e binários marcados.
- **Evidência:** build na raiz com 0 avisos. `dotnet test` roda em modo MTP. Projeto descartável aprovado (1/1). Nenhum arquivo versionado muda só por fim de linha.
- Novos findings: DEV-017 (`dotnet test` sem projetos retorna exit 1; usar `--minimum-expected-tests` no CI), DEV-018 a DEV-021 (baixa/informativo).

## Testes F0
- Pedido do usuário: messageId 6fefd3df. Delegação ao test-analyst: 26606393. Resposta: 9b760559.
- Quatro projetos xunit.v3 sobre MTP, todos em `tests/`:
  - unidade: ServiceDefaults;
  - integração da Payments.Api: WebApplicationFactory e Playwright APIRequestContext contra Kestrel efêmero;
  - integração do CoreBanking.Host: HTTP e gRPC;
  - arquitetura: NetArchTest e varredura de segredos, PAN e CPF.
- O IdP fake só troca o `BackchannelHttpHandler`, sem afrouxar a validação. As chaves são geradas em tempo de execução.
- **Evidência do orquestrador** (`dotnet test --project <csproj> -c Release --minimum-expected-tests 1 --report-xunit-trx`):

  | Projeto | Total | Passou | Falhou | Skip |
  |---|---|---|---|---|
  | ServiceDefaults.UnitTests | 68 | 67 | 0 | 1 |
  | Payments.Api.IntegrationTests | 123 | 118 | 0 | 5 |
  | CoreBanking.Host.IntegrationTests | 12 | 9 | 0 | 3 |
  | ArchitectureTests | 22 | 22 | 0 | 0 |

  O build Release de cada projeto de teste e o build da raiz deram 0 avisos e 0 erros. Os TRX ficaram locais, fora do commit.
- **Os 9 Skips são defeitos de produção confirmados, ainda não corrigidos:**
  - **TA-015 (Alta):** chave removida do JWKS continua aceita por causa do last-known-good.
  - **TA-019 (Média):** o binding de `Schemes:Bearer` amplia as aud/iss aceitas.
  - **TA-018:** rotas gRPC unimplemented são anônimas.
  - **TA-013:** token sem `nbf` é aceito.
  - **TA-014:** `kid` inexistente é aceito.
  - **TA-017:** OpenAPI sem security scheme.
- Observações: TA-016 (1ª requisição com `kid` novo dá 401) e TA-020 (limite de corpo não definido).
- **Casos F0 não cobertos:**
  - TC-020 e TC-026 PermissionDenied: faltam endpoints com scope.
  - TC-217: limite de corpo.
  - Parte do TC-216 e do TC-218: dependem de alterar o NuGet.config e do seam H-02.

## Correções TA-015 e TA-019, `.slnx` e `.gitignore`
- Autorização do usuário: messageId 76aa83f4. Delegação ao developer: c0805fbe. Resposta: c9662963.
- **TA-015:** um PostConfigure desliga `UseLastKnownGoodConfiguration` no `ConfigurationManager`.
  - Se o IdP cair, a configuração atual em cache continua valendo (verificado no código do IdentityModel 8.19.2).
  - Em cold start, a validação falha fechada (401).
- **TA-019:** o `TokenValidationParameters` é reconstruído só a partir das opções validadas.
- **DEV-022 (corrigido junto):** `MetadataAddress`, `Forward*` e `SigningKeys` vindos do binding foram neutralizados.
- Os 4 projetos de teste entraram na `BancoCarrefour.slnx`, e `tests/TestResults/` entrou no `.gitignore`.
- **Evidência do orquestrador:**
  - build da raiz (9 projetos) com 0 avisos e 0 erros;
  - `dotnet test --solution BancoCarrefour.slnx -c Release --minimum-expected-tests 1`: 225 testes, 216 passaram, 0 falharam, 9 em Skip.
- Os três testes de TA-015 e TA-019 sem Skip passaram numa cópia do repositório feita pelo developer. A reativação no repositório foi delegada ao test-analyst (5c2dbd79).
- Findings em aberto:
  - DEV-023: revogação ainda limitada pelo `AutomaticRefreshInterval` de 1 h; decisão da security-privacy;
  - DEV-024: falha do PostConfigure aparece na primeira requisição, não no startup;
  - DEV-025: registrar o LKG desabilitado na ADR-0006.

## DEV-023 e reativação dos testes
- O usuário autorizou o DEV-023 (messageId 798d2527); delegação ao developer: b1156cbe.
  - `AutomaticRefreshInterval` passou de 1 h para 5 min, o mínimo do IdentityModel (abaixo disso ele lança IDX10108). `RefreshInterval` ficou em 5 min.
  - Com o IdP disponível, uma chave revogada deixa de ser aceita em cerca de 5,5 min por instância.
- **Reativação dos testes (test-analyst, 5c2dbd79):**
  - Os 3 testes de TA-015/TA-019 voltaram a valer como gate.
  - Testes novos para o DEV-022 e para o LKG desligado, com controles positivos.
  - Todos os testes novos falham contra o código anterior à correção. Isso confirmou que `ForwardDefault`, vindo do binding, permitia passar sem token antes do DEV-022.
- **Evidência do orquestrador:**
  - build da raiz com 0 avisos e 0 erros;
  - `dotnet test --solution BancoCarrefour.slnx -c Release --minimum-expected-tests 1`, executado 2 vezes: 235 testes, 0 falhas, 6 em Skip (TA-013, TA-014, TA-017, TA-018).
- **Novos findings:**
  - DEV-026 (Alta): com o IdP indisponível, a configuração atual em cache continua valendo sem limite de idade. Decisão pendente.
  - DEV-027 (Média): sem backoff depois de uma falha de refresh.
  - DEV-028 (Baixa): sem métrica de idade da configuração.
  - TA-021: teste de startup instável; corrigido em `tests/`.
  - TA-022: o binding não lê `AutomaticRefreshInterval`.
  - TA-023: developer e test-analyst não devem trabalhar em paralelo no mesmo working tree.

## Não realizado
- Correções de TA-013, TA-014, TA-017 e TA-018 (não autorizadas). Os testes correspondentes seguem em Skip.
- DEV-024..DEV-028 não decididos.
- ADR-0006 ainda não registra o LKG desligado nem o refresh de 5 min (DEV-025).
- Nenhuma alteração em `.claude/` ou `scripts/` (ADR-0014 segue Proposed).
