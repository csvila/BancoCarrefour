# Plano de Testes — v0.1 (Proposta)

- Autor: `test-analyst` (tarefa ETAPA2-TEST-PLAN; resposta à delegação `06c2f849-1ab3-4ba9-9364-6e5b1b40b561`)
- Data: 2026-11-09 · Branch de referência: `etapa1-arquitetura` @ `b71c714`
- Status: **Proposta.** Nenhum teste foi implementado ou executado como parte deste plano, exceto as duas sondas de governança descritas na §8.3 (evidência registrada).
- Escopo de escrita: este arquivo é o único artefato criado. Projetos `.csproj`, scripts k6 e fixtures serão criados depois que o skeleton do `developer` estiver disponível.

---

## 1. Objetivo, escopo, referências e premissas

### 1.1 Objetivo
Transformar os requisitos aceitos em casos de teste verificáveis, com rastreabilidade bidirecional. Os requisitos vêm das ADRs 0002–0013 (Accepted), dos SEC-REQ-001..030, das NFRs e da tabela harmonizada de latência. Toda regra financeira do CLAUDE.md precisa de um teste que falhe se ela for violada.

### 1.2 Escopo
| Dentro | Fora (nesta versão) |
|---|---|
| Unidade (xUnit), integração (xUnit + WebApplicationFactory, Playwright .NET APIRequestContext), arquitetura, carga (k6), caos/failover (planejado em hml), governança | Pentest, auditoria QSA, aprovação de escopo PCI, revisão jurídica: são atividades de terceiros, e aqui só se registra a dependência |
| Payments API `/v1`, CoreBanking gRPC, ServiceDefaults (JwtBearer, ProblemDetails, correlation-id, OTel, health) | UI Web/Mobile (browser contexts entram quando existir BFF/UI) |
| Domínio futuro: ledger, idempotência, outbox, fencing, CAE, CQRS, saga, Party | Execução em produção, que é **proibida** em qualquer cenário |

### 1.3 Referências
- `CLAUDE.md`; `docs/governance/PROJECT-STATUS.md`; `docs/governance/DECISIONS.md`.
- ADR-0002..0013, seções "Critérios de validação". A ADR-0014 está **Proposed** e só orienta a §8.
- `docs/security/threat-model.md` §5 (TM-001..042) e §6 (SEC-REQ-001..030).
- `docs/architecture/nfr.md` (NFR-0xx..1xx). Para latência, **prevalece** `docs/architecture/etapa1-consolidacao.md` §2 (tabela harmonizada, CF-03).
- `docs/performance/capacity-model.md` §6 (plano k6) e §7 (SLOs candidatos).
- `docs/architecture/bounded-contexts.md` §4 (fluxos 4.1–4.4).

### 1.4 Premissas
| ID | Premissa | Se falhar |
|---|---|---|
| PT-01 | O skeleton do `developer` entrega: host gRPC **CoreBanking**, **Payments API** REST `/v1` e **ServiceDefaults** com JwtBearer, ProblemDetails (RFC 9457), correlation-id, OpenTelemetry e health checks | Os casos F0 (§4) são replanejados |
| PT-02 | O skeleton expõe ao menos um endpoint protegido por policy de scope em Payments e um método gRPC protegido em CoreBanking | Sem isso, a suíte negativa de JWT (§5.1) não roda em F0 |
| PT-03 | `net10.0` / C# 14, SDK 10.0.403 (verificado neste host: `dotnet --list-sdks`) | — |
| PT-04 | Docker disponível para bancos efêmeros (verificado neste host: Docker 29.9.2, daemon ativo). Runners de CI também precisarão de Docker | Ver §6.2 (alternativas) |
| PT-05 | k6 **não** está instalado neste host (verificado: `k6: command not found`) | Instalação no runner de carga/hml (dependência) |
| PT-06 | Nenhum teste roda contra produção. Ambientes: `dev` local/CI e `hml` | — |
| PT-07 | Códigos HTTP seguem as ADRs Accepted quando divergem do threat model v0.1 (ver TA-003) | Ajustar quando sair o threat model v0.2 |

---

## 2. Estratégia e pirâmide

### 2.1 Pirâmide proposta
| Nível | Ferramenta | Volume relativo | O que valida | Onde roda |
|---|---|---|---|---|
| Unidade | xUnit | ~60% | Invariantes de domínio (partidas dobradas, precisão monetária, saldos distintos), máquinas de estados (idempotência, autorização com UNKNOWN, quota CAE), canonicalização de payload, regras de elegibilidade CAE, Theories para matrizes de regras | Todo build |
| Arquitetura | xUnit + biblioteca de arch-test (NetArchTest.eNhancedEdition ou ArchUnitNET, ver D-3) | ~5% | Fronteiras de módulo (ADR-0009), "caminho de débito não depende de cache/read model" (SEC-REQ-014), ausência de 2PC, ausência de `DateTime.UtcNow` direto no domínio | Todo build |
| Integração de componente | xUnit + WebApplicationFactory (TestServer) + PostgreSQL efêmero | ~25% | Idempotência atômica, concorrência, fencing, permissões do banco, outbox, injeção de falhas via DI, gRPC in-process | Todo PR (requer Docker) |
| Integração de contrato HTTP | **Microsoft.Playwright para .NET** (`APIRequestContext`) contra host Kestrel real (local) ou URL de hml | ~8% | Contrato visto pelo cliente: Bearer, status, headers (`WWW-Authenticate`, `Idempotent-Replayed`, `Retry-After`), `application/problem+json`, matriz BOLA, fluxos ponta a ponta | Todo PR (local) e hml |
| Carga | k6 (JavaScript) | — | SLOs, saturação, hot account, replay, failover | hml, sob demanda e antes de release |
| Caos/failover | Ferramenta a definir com DevOps (ver TA-008) | — | Fencing sob partição de rede, RPO/RTO, Σ aprovado ≤ Σ quotas | hml, game days |
| Governança | xUnit invocando processo (recomendado) ou pytest (ver §8, D-1) | — | Guard, log JSONL, validador | Todo PR, depois da decisão |

### 2.2 Quando usar cada ferramenta de integração (CLAUDE.md, ADR-0013)
- **xUnit + WebApplicationFactory (TestServer em memória)** quando o teste precisa:
  - substituir serviços por DI (`TimeProvider` falso, injeção de falha pós-commit, backchannel do IdP fake, barreiras de concorrência);
  - inspecionar o estado persistido (ledger, outbox, idempotência) na mesma execução;
  - chamar gRPC in-process (`GrpcChannel` com o `HttpMessageHandler` do TestServer).

  Esse modelo **não substitui** a verificação de contrato HTTP sobre rede, porque o TestServer não passa por Kestrel, pelos limites de socket nem pelos headers de transporte reais.
- **Playwright .NET `APIRequestContext`** quando o objeto da verificação é a **interação de API** como um cliente a vê. Isso cobre autenticação Bearer, token em query, Problem Details, headers de idempotência, BOLA e fluxos ponta a ponta. O `APIRequestContext` exige um endpoint HTTP real. Há três opções, em ordem de preferência:
  1. WebApplicationFactory hospedada em Kestrel em porta efêmera. O ASP.NET Core 10 introduziu suporte a Kestrel na WebApplicationFactory: **confirmar a API exata no SDK 10.0.403 na implementação**. Essa opção preserva os seams de DI.
  2. Processo/container da aplicação iniciado pelo fixture.
  3. URL de hml via variável de ambiente `BC_TEST_BASE_URL`.

  A mesma suíte Playwright precisa rodar sem alteração nos três alvos, mudando só a configuração. O `APIRequestContext` não deve exigir binários de navegador (confirmar na implementação).
- **Playwright com browser contexts** apenas quando existir BFF/UI (TM-040: CSP, CSRF, cookie HttpOnly/SameSite). Fase F2/F3.
- **Proibido** usar EF InMemory, SQLite ou mocks de repositório em testes de concorrência, fencing, permissões e atomicidade, porque eles não reproduzem o isolamento nem os locks do PostgreSQL.

### 2.3 Princípios
- Determinismo: relógio injetável (`TimeProvider`/`FakeTimeProvider`), IDs controláveis, nenhuma dependência de rede externa, **nenhum `sleep` fixo**. Usar polling com deadline e condição explícita.
- Status de negócio acima de status HTTP: um 200 não prova sucesso financeiro. Toda asserção financeira confere o efeito no ledger (contagem de lançamentos, saldos, outbox).
- Teste de "nunca": para cada proibição (UNKNOWN → APPROVED, débito via cache, segundo lançamento), existe pelo menos um teste que falharia se ela fosse violada.
- Nomenclatura `Metodo_Estado_Esperado`; AAA; Theories para matrizes.
- Asserções: as nativas do xUnit. FluentAssertions ≥ 8 tem licença comercial e não deve ser adotada sem decisão (TA-010). Alternativas como Shouldly e AwesomeAssertions exigem aprovação (D-3).

---

## 3. Estrutura proposta de `tests/` (futura)

```
tests/
  TEST-PLAN.md                         (este documento)
  Directory.Build.props                (test-analyst; ver 3.1)
  Directory.Packages.props             (test-analyst; importa o CPM do developer + versões só de teste; ver 3.1)
  BancoCarrefour.Tests.slnx            (ou inclusão na solução do developer: decisão D-2)
  xunit.runner.json                    (paralelismo, timeouts longos detectados)
  TestKit/
    BancoCarrefour.TestKit/            FakeIdp, JwtFactory, SyntheticData, PostgresFixture,
                                       FaultInjection, LogCapture/SpanCapture, PiiScanner, KestrelAppFactory
  Unit/
    BancoCarrefour.CoreBanking.UnitTests/
    BancoCarrefour.Payments.UnitTests/
    BancoCarrefour.ServiceDefaults.UnitTests/
    BancoCarrefour.Contingency.UnitTests/      (quando o CAE existir)
  Integration/
    BancoCarrefour.CoreBanking.IntegrationTests/   (gRPC in-process + PostgreSQL efêmero)
    BancoCarrefour.Payments.IntegrationTests/      (WebApplicationFactory + Playwright APIRequestContext)
    BancoCarrefour.Security.IntegrationTests/      (suíte negativa JWT/BOLA parametrizada por host)
  Architecture/
    BancoCarrefour.ArchitectureTests/
  Governance/
    BancoCarrefour.GovernanceTests/            (pendente de D-1; executa scripts/ em cópia temporária)
  Performance/
    k6/
      lib/          (auth.js com token via env, data.js Zipf, checks.js de status de negócio)
      scenarios/    (smoke.js, baseline.js, stress.js, spike.js, soak.js, capacity.js,
                     hot-account.js, replay.js, failover.js, statement.js)
      thresholds/   (candidate.json, que vira approved.json após PERF-006)
      README.md     (como rodar, parâmetros e evidência obrigatória)
  Chaos/
    experiments/    (especificações de hipótese/passos/aborto; manifests ficam em infra/, ver TA-008)
  TestData/
    allowlists/     (PANs de teste aprovados, lista de CPFs sintéticos aprovada: D-9)
    generators/     (geradores determinísticos com seed; nenhum dado estático parecido com real)
```

Os nomes dos projetos seguirão os nomes de assembly reais do skeleton. Projetos novos só serão criados quando o módulo correspondente existir.

### 3.1 Configuração de build dos testes
**Problema.** Segundo a delegação, o skeleton coloca `Directory.Build.props` e `global.json` em `src/`, e possivelmente também `Directory.Packages.props` e `nuget.config`. Nenhum deles vale para `tests/`:
- O MSBuild procura `Directory.Build.props` e `Directory.Packages.props` **subindo** a partir do diretório do projeto. Projetos em `tests/` passam por `tests/` e pela raiz, e nunca por `src/`.
- O `dotnet` resolve o `global.json` subindo a partir do **diretório corrente**. Um `dotnet test` rodado na raiz ou em `tests/` não fica preso ao SDK.
- O NuGet resolve `nuget.config` (e o package source mapping) pelo mesmo tipo de busca ascendente. Sem isso, os testes restauram sem a mitigação de TM-023.

**Opção A (recomendada).** O `developer` move `global.json`, `Directory.Build.props`, `Directory.Packages.props` e `nuget.config` para a **raiz**. Em `tests/`:
- `tests/Directory.Build.props` importa o da raiz (`<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)..'))" />`) e acrescenta só o que é de teste: `IsPackable=false`, `IsTestProject=true` e supressão **pontual** de analyzers incompatíveis com nomes `Metodo_Estado_Esperado` (por exemplo, CA1707), sem desligar `TreatWarningsAsErrors`.
- `tests/Directory.Packages.props` importa o CPM da raiz e declara apenas os `PackageVersion` de teste. Assim o `test-analyst` mantém as próprias dependências sem editar arquivo do `developer`.
- `RestorePackagesWithLockFile=true` e `packages.lock.json` versionados por projeto de teste. Restore em CI com `--locked-mode`.

**Opção B (fallback).** Os arquivos ficam em `src/`:
- `tests/Directory.Build.props` e `tests/Directory.Packages.props` importam `..\src\...` explicitamente.
- O `test-analyst` mantém uma **cópia** de `global.json` em `tests/`, com teste de drift que compara os dois arquivos.
- Fica pendente onde mora o `nuget.config` com source mapping. Uma cópia em `tests/` gera duplicação de controle de supply chain.

Essa escolha depende do orquestrador e do `developer` (D-2, TA-005).

---

## 4. Matriz de rastreabilidade

### 4.1 Legenda
- **Nível:** U = unidade (xUnit); I-W = integração xUnit + WebApplicationFactory; I-P = integração Playwright APIRequestContext; A = arquitetura; K = k6; C = caos/failover; G = governança; M = verificação manual ou checklist (não automatizável pelo `test-analyst`).
- **Prioridade:** P0 = bloqueia merge e release de qualquer fluxo financeiro; P1 = bloqueia release do componente; P2 = desejável ou contínuo.
- **Fase:**
  - **F0**: implementável já sobre o skeleton.
  - **F1**: depende do domínio Core (accounts, ledger, idempotência, outbox, `partition_control`) e de PostgreSQL efêmero.
  - **F2**: depende de Payments/saga, Statement/projector, CAE, Card, Party, Risk/antifraude (fakes).
  - **F3**: depende de infra/hml (K8s, NGINX, etcd, mesh, IdP real, CI) ou de SLOs aprovados.
  - **FG**: governança, depende de D-1 e da ADR-0014.
- `[VALIDAR]`: o resultado esperado depende de Compliance, Risco, bandeira ou QSA. O teste é parametrizado e não fixa a regra por conta própria.

### 4.2 Catálogo de casos de teste

#### 4.2.1 Autenticação e autorização (ADR-0006; SEC-REQ-001..008, 019, 027)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-001 | NFR-070, ADR-0006 | I-P | Host com FakeIdp | Chamar endpoint protegido sem `Authorization` | 401, `WWW-Authenticate: Bearer`, corpo sem detalhe interno | P0 | F0 |
| TC-002 | ADR-0006 (controle positivo) | I-P | Token RS256 válido (iss, aud, scope corretos) | Chamar endpoint protegido | 2xx; garante que os negativos não passam por erro de setup | P0 | F0 |
| TC-003 | SEC-REQ-001 | I-P | Token com header `alg:none`, sem assinatura | Chamar endpoint | 401 | P0 | F0 |
| TC-004 | SEC-REQ-001 | I-P | Token HS256 assinado com a chave **pública** RSA do JWKS como segredo HMAC | Chamar endpoint | 401 | P0 | F0 |
| TC-005 | SEC-REQ-001 | I-W (Theory) | Tokens RS384, RS512, ES384, HS256, HS512 com chaves válidas de teste | Chamar endpoint com cada um | 401 para tudo fora de {RS256, PS256, ES256}; 2xx para os três permitidos | P1 | F0 |
| TC-006 | SEC-REQ-001 | I-P | Token válido com payload alterado após a assinatura | Chamar endpoint | 401 | P0 | F0 |
| TC-007 | SEC-REQ-002, SEC-REQ-005 | I-W | FakeIdp conta as requisições ao JWKS | Enviar N tokens com `kid` desconhecido | 401; refresh do JWKS limitado (≤ 1 busca por janela configurada, ver TA-013) | P0 | F0 |
| TC-008 | SEC-REQ-002 | I-W (Theory) | Listener "atacante" local conta conexões | `kid` = `../../x`, `' OR 1=1`, URL, string de 10 KB | 401; 0 requisições de saída além do JWKS configurado | P0 | F0 |
| TC-009 | SEC-REQ-002 | I-W | Listener "atacante" serve JWKS próprio | Token assinado pela chave do atacante com `jku`, `x5u` ou `jwk` embutido | 401; 0 hits no listener | P0 | F0 |
| TC-010 | SEC-REQ-003 | I-P | — | Token com `iss` diferente (inclui variação de caixa e barra final) | 401 | P0 | F0 |
| TC-011 | SEC-REQ-003 | I-P (Theory) | — | `aud` de outra API, `aud` ausente, `aud` em lista sem a API | 401 | P0 | F0 |
| TC-012 | SEC-REQ-003, TM-008 | I-P | Endpoint de operador com audience e role separadas | Token de cliente válido no endpoint de operador | 403 | P0 | F2 |
| TC-013 | SEC-REQ-004 | I-P | Skew configurado (≤ 60 s) | Token com `exp` = agora − (skew + 1 s); controle com `exp` = agora − (skew − 5 s) | 401 no primeiro e 2xx no controle. Margem de ±5 s porque a validação de tempo do IdentityModel pode não usar `TimeProvider` | P0 | F0 |
| TC-014 | SEC-REQ-004 | I-P (Theory) | — | Token sem `exp`; token com `nbf` no futuro além do skew | 401 | P0 | F0 |
| TC-015 | SEC-REQ-005 | I-W | FakeIdp responde 503, timeout ou conexão recusada desde o cold start | Chamar endpoint com token bem formado | 401 (fail-closed), **nunca 2xx nem 5xx** | P0 | F0 |
| TC-016 | SEC-REQ-005, NFR-078 | I-W | JWKS com chaves A e B (sobreposição) | Tokens de A e de B; remover A, forçar refresh e repetir | A e B aceitos durante a sobreposição; A dá 401 após a remoção; sem downtime para B | P1 | F0 |
| TC-017 | SEC-REQ-001/005 | I-W | JWKS malformado ou contendo chave `kty: oct` | Chamar endpoint | 401 | P1 | F0 |
| TC-018 | SEC-REQ-006 | I-P | — | `?access_token=<jwt>` com e sem header Bearer; variantes `?token=`, `?id_token=` | 400 Problem Details conforme ADR-0006 (ver TA-003); o token não aparece em logs | P0 | F0 |
| TC-019 | SEC-REQ-006/017 | I-W | LogCapture + SpanCapture (InMemoryExporter) | Requisições com Bearer válido e inválido | 0 ocorrências de `eyJ...` ou do valor de `Authorization` em logs, spans, atributos e eventos | P0 | F0 |
| TC-020 | TM-008, NFR-070 | I-P | Endpoint com policy de scope | Token válido sem o scope exigido | 403 | P0 | F0 (PT-02) |
| TC-021 | SEC-REQ-007, SEC-REQ-027 | I-P (matriz) | Clientes sintéticos A e B com contas próprias | Para **cada** endpoint e verbo: A acessa um recurso de B e um recurso inexistente | Ambos indistinguíveis (mesmo status canônico conforme D-8, mesmo formato de corpo e mesmos headers relevantes); nenhum efeito | P0 | F1 |
| TC-022 | NFR-070 | I-W | — | Enumerar `EndpointDataSource` e os serviços gRPC | Toda rota exige autorização, exceto a allowlist declarada (health) | P0 | F0 |
| TC-023 | SEC-REQ-008 | I-P | Comando financeiro | Corpo com campos extras (`status`, `ownerId`, `balance`) | 400, ou campos ignorados **sem efeito** (conferir no estado persistido) | P1 | F1 |
| TC-024 | TM-003 | I-P | Tokens com audience de serviço ou de terminal | Usar em API de cliente | 401 | P1 | F0 |
| TC-025 | SEC-REQ-019 | I-P | Gateway de parceiros em hml | Sem certificado de cliente; `private_key_jwt` inválido; acima da quota | Handshake ou token recusado; 429 com Problem Details e `Retry-After` | P1 | F3 |
| TC-026 | ADR-0006 (S2S), NFR-073 | I-W | CoreBanking gRPC | Chamada sem credencial; com token de usuário final | `Unauthenticated` ou `PermissionDenied`; nenhum efeito | P0 | F0 |
| TC-027 | TM-004 | I-P | IdP escolhido em hml | Reuso de refresh token já rotacionado | Família de tokens revogada (comportamento do IdP) | P2 | F3 |

#### 4.2.2 Baseline de plataforma do skeleton (ADR-0013, NFR-113/134)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-210 | ADR-0013 | M + CI | Commit identificado | `dotnet --info`; `dotnet build -c Release` com `TreatWarningsAsErrors` | 0 warnings, 0 erros; evidência (comando, saída, commit) | P0 | F0 |
| TC-211 | NFR-134, RFC 9457 | I-P | Endpoint que provoca 400, 404 e 500 (falha injetada) | Chamar cada um | `application/problem+json` com `type`, `title`, `status` e `traceId`; sem stack trace nem mensagem de exceção fora de `Development` | P0 | F0 |
| TC-212 | NFR-113 | I-P | — | Sem correlation-id; com id válido; com CRLF, mais de 128 caracteres ou caracteres de controle | Gerado quando ausente; ecoado quando válido; malformado substituído ou rejeitado, nunca refletido; presente nos logs | P1 | F0 |
| TC-213 | NFR-113 | I-W | Payments chama CoreBanking | Requisição com `traceparent` | Mesmo trace-id no span gRPC downstream | P1 | F1 |
| TC-214 | NFR-070 | I-P | — | `GET` em live e ready; ready com dependência derrubada | Anônimos; sem versões, hosts nem connection strings; ready não saudável quando a dependência cai | P1 | F0 |
| TC-215 | NFR-134 | I-P | — | Obter o documento OpenAPI | Security scheme Bearer; respostas de erro como Problem Details; só rotas `/v1` | P2 | F0 |
| TC-216 | ADR-0013, TM-023 | CI | Lock files presentes | `dotnet restore --locked-mode`; restore com feed não mapeado | Restore determinístico; pacote de origem não mapeada falha | P1 | F0 |
| TC-217 | SEC-REQ-020 | I-P | Limite de corpo no Kestrel configurado | Corpo acima do limite | 413 Problem Details | P1 | F0 |
| TC-218 | Higiene de config | I-W | Ambiente `Production` simulado | Subir host com `RequireHttpsMetadata=false` ou Authority de teste | Startup falha (guarda contra configuração de teste em produção) | P1 | F0 |

#### 4.2.3 Idempotência (ADR-0003 item 4; SEC-REQ-009/010; NFR-050..053)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-040 | SEC-REQ-009 | I-P | Comando financeiro (POST `/v1/transfers` ou equivalente) | Enviar sem `Idempotency-Key` | 400 Problem Details; 0 registros de idempotência, lançamentos ou outbox | P0 | F1 |
| TC-041 | SEC-REQ-009 | I-P (Theory) | — | Chave vazia, com mais de 255 caracteres, com caracteres de controle | 400; nenhum efeito | P1 | F1 |
| TC-042 | SEC-REQ-009 | I-W + I-P | Conta sintética com saldo | 10 requisições **paralelas** com a mesma chave e o mesmo payload | Exatamente 1 JournalEntry e 1 outbox; as demais respostas são replay (mesmo status, `Idempotent-Replayed: true`) ou 409 em andamento; 0 respostas 5xx | P0 | F1 |
| TC-043 | SEC-REQ-009, CF-10 | I-P | Chave K já concluída | Mesma K com payload divergente (valor ou destino) | 422, `type` = `idempotency-key-reused`; nenhum efeito adicional | P0 | F1 |
| TC-044 | ADR-0003, CF-10 | I-W | Seam de barreira mantém a 1ª requisição em IN_PROGRESS | 2ª requisição com a mesma K | 409 com `Retry-After`; após liberar a barreira, 1 único efeito | P0 | F1 |
| TC-045 | ADR-0003 item 9, fluxo 4.3 | I-W + I-P | Seam descarta a resposta **após** o commit | Primeira chamada sem resposta (timeout no cliente); retry com mesma K e payload | Mesmo status e corpo (comparação canônica) com `Idempotent-Replayed: true`; 1 lançamento | P0 | F1 |
| TC-046 | SEC-REQ-010 | I-P | Clientes A e B | A e B usam a mesma K | Efeitos independentes; B nunca recebe a resposta gravada de A | P0 | F1 |
| TC-047 | ADR-0003 (hash canônico) | U (Theory) | — | Mesmo payload com ordem de campos e espaços diferentes; payloads semanticamente diferentes | Mesmo hash no primeiro caso e hash diferente no segundo. A política para `10.0` vs `10.00` precisa ser definida pelo developer | P1 | F1 |
| TC-048 | NFR-051 | I-W | FakeTimeProvider | Avançar o relógio além da retenção configurada | Comportamento documentado (janela [VALIDAR contrapartes]); nunca um segundo efeito dentro da janela | P2 | F1 |
| TC-049 | ADR-0003 (chaves por perna) | U | — | Gerar chaves `transferId:n` em retries | Chaves estáveis e determinísticas | P1 | F2 |
| TC-050 | ADR-0003 item 1 (atomicidade) | I-W | Falha injetada após gravar IN_PROGRESS e antes do commit | Executar e depois repetir com a mesma K | Rollback total (sem IN_PROGRESS órfão permanente); o retry processa uma única vez | P0 | F1 |
| TC-051 | ADR-0003 | U | — | Transições do IdempotencyRecord | IN_PROGRESS → COMPLETED; nunca regride; hash imutável | P0 | F1 |

#### 4.2.4 Ledger, concorrência e saldos (ADR-0003; SEC-REQ-011/025; NFR-040/045/060..062)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-070 | SEC-REQ-011 | I-W (PostgreSQL real) | Conta com saldo exato para 10 débitos | 100 débitos paralelos com chaves distintas | Exatamente 10 APPROVED e 90 DECLINED por saldo insuficiente (status de negócio, não 5xx); saldo final 0; nunca negativo | P0 | F1 |
| TC-071 | ADR-0003 itens 1 e 3, SW-006 | I-W | Conta com hold, bloqueio e limite | Débitos e holds concorrentes | `disponível = contábil − holds − bloqueado (+ limite)` respeitado; consumo de limite nunca excede o contratado | P0 | F1 |
| TC-072 | ADR-0003 item 2 | U (Theory) | — | Construir JournalEntry desbalanceado; multimoeda balanceado por moeda e desbalanceado em uma | Rejeitado quando Σ D ≠ Σ C em qualquer moeda | P0 | F1 |
| TC-073 | Regras financeiras | U (Theory) | — | Valores ≤ 0, escala acima da permitida pela moeda, overflow | Rejeitados; somente `decimal` (arch-test proíbe `double`/`float` em valores) | P0 | F1 |
| TC-074 | SEC-REQ-025, NFR-061 | I-W | Migrações aplicadas; conexão com a role do serviço | `UPDATE` e `DELETE` em journal e postings | Erro de permissão; dados intactos | P0 | F1 |
| TC-075 | SEC-REQ-025, NFR-062 | I-W | Cadeia de hash por partição | Adulterar uma linha como superuser do container e rodar o verificador | Verificador falha e aponta a posição | P0 | F1 |
| TC-076 | NFR-040, ADR-0003 | I-W | Dataset sintético de N contas | Rodar o job de invariantes; repetir com uma quebra semeada | 0 quebras no dataset limpo; a quebra semeada é detectada | P0 | F1 |
| TC-077 | Regras financeiras | I-W | Lançamento existente | `Reverse` duas vezes com a mesma chave | Nova entry espelhada referenciando a original; original intacta; a segunda chamada é replay | P0 | F1 |
| TC-078 | NFR-045 | I-W | — | Sequência por conta; lacuna injetada | Sequência monotônica; lacuna detectada | P1 | F1 |
| TC-079 | BC-03 | U (Theory) | — | Débito em conta bloqueada ou encerrada; encerramento com saldo ou hold | Recusado com motivo | P1 | F1 |
| TC-080 | ADR-0003 item 5 | I-W | FakeTimeProvider | PlaceHold, CaptureHold (valor final ≤ autorizado + tolerância), ReleaseHold repetido, expiração | Hold reduz o disponível sem lançamento; captura gera JournalEntry e libera o residual; release idempotente; expiração correta | P1 | F2 |
| TC-081 | ADR-0003 item 6, SW-010 | I-W | — | Crédito externo recebido sem liquidação; depois com liquidação confirmada | Disponível só após a liquidação; estados recebido → contabilizado → disponível/bloqueado | P0 | F2 |
| TC-082 | ADR-0003 item 7 | I-W | — | Comando com rollback forçado; comando com commit | 0 outbox no rollback; 1 mensagem no commit com `eventId`, sequência e hash, **sem PII** | P0 | F1 |
| TC-083 | ADR-0003, NFR-052 | I-W | Broker de teste com histórico | Replay total dos eventos | Estado derivado idêntico (hash do estado) | P1 | F2 |

#### 4.2.5 Fencing, topologia e SoR (ADR-0002, 0007, 0008; SEC-REQ-012/013)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-090 | SEC-REQ-012, NFR-063 | I-W | `partition_control.epoch = 6` | Writer com token 5 tenta escrever | Transação abortada (erro de fencing); 0 linhas | P0 | F1 |
| TC-091 | SEC-REQ-012 ("pausa simulada") | I-W | Barreira entre a leitura e o commit do writer A (epoch 5) | Writer B incrementa para 6 e escreve; liberar A | Commit de A rejeitado; só B tem efeito | P0 | F1 |
| TC-092 | ADR-0002 | I-W | Directory de teste | Comando enviado ao writer errado | `WRONG_PARTITION_OWNER`; nenhum efeito | P1 | F1 |
| TC-093 | ADR-0007 | I-W | Conta com SoR = `LEGACY` | Comando de escrita no Core `NEW` | Rejeitado; nenhum efeito | P0 | F2 |
| TC-094 | ADR-0007 | C/I-W | Coorte sintética em hml | Congelar, migrar, reconciliar e trocar o SoR | Diferença zero em contábil, disponível, bloqueado e reservado | P1 | F3 |
| TC-095 | ADR-0007 | C/I-W | — | Rollback antes e depois do primeiro lançamento novo | Antes: SoR revertido. Depois: migração compensatória sem edição destrutiva | P1 | F3 |
| TC-096 | ADR-0007, NFR-146 | I-W | Conta `LEGACY` em janela batch | Débito com e sem quota CAE | Com quota: decisão do CAE. Sem quota: UNAVAILABLE | P1 | F2 |
| TC-097 | ADR-0002, SEC-REQ-013 | C | hml multi-AZ/região | Particionar a rede inter-AZ e inter-região sob carga | Nenhuma partição com dois writers (auditoria de epoch e de escritas) | P0 | F3 |
| TC-098 | ADR-0002, NFR-010 | C + K | hml com 3 AZs | Derrubar a AZ do writer sob carga | RPO = 0 (todo commit confirmado ao cliente está persistido); RTO medido contra a meta proposta de ≤ 60 s | P0 | F3 |
| TC-099 | ADR-0002/0008 | C + K | hml com 2 regiões | Drill regional: isolamento, epoch++, LSN de corte, promoção | Partições em RECOVERY até reconciliar; RPO e RTO medidos; reconciliação com diferença zero | P0 | F3 |
| TC-100 | ADR-0002 (≥ 2 sinais) | C | — | Derrubar um único health check | Sem promoção | P1 | F3 |
| TC-101 | ADR-0008 | I-W/C | Partição em RECOVERY | Comando de escrita | UNAVAILABLE até a reconciliação; nunca aprovação | P1 | F3 |

#### 4.2.6 UNKNOWN, contingência e antifraude (ADR-0005; SEC-REQ-015/016/026)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-110 | SEC-REQ-016, NFR-140 | U (Theory sobre todas as transições) | — | Aplicar cada evento (timeout, resposta tardia, retry, `GetCommandResult` COMMITTED ou NOT_FOUND confirmado) a cada estado | UNKNOWN → APPROVED/DECLINED **só** com evidência do Core; timeout nunca leva a DECLINED/FAILED; transições inválidas lançam erro | P0 | F1 |
| TC-111 | SEC-REQ-016 | U (property-based, opcional: D-3) | — | Sequências aleatórias de eventos | Nunca duas aprovações; nunca APPROVED derivado de timeout | P1 | F1 |
| TC-112 | ADR-0005 critério 1, fluxo 4.3 item 5 | I-W | Seam: Core comita e o cliente gRPC recebe `DeadlineExceeded` | Comando; depois retry com a mesma chave | Primeiro UNKNOWN; o retry devolve o resultado original; nunca chave nova; nunca segunda aprovação | P0 | F2 |
| TC-113 | ADR-0005 item 4, SW-003 | I-W | Partição em CONTINGENCY; ordem DISPATCHED/UNKNOWN | Reprocessar a ordem | CAE fake com 0 chamadas; continua UNKNOWN | P0 | F2 |
| TC-114 | ADR-0005 item 3 | I-W | Control plane fake sem declaração | Timeout isolado de requisição | CAE não é acionado | P0 | F2 |
| TC-115 | SEC-REQ-015 | U + I-W | Quota com saldo X | Consumir até exceder | Excedente recebe DECLINED ou UNAVAILABLE ([VALIDAR Risco], TA-006); Σ aprovado ≤ X | P0 | F2 |
| TC-116 | SEC-REQ-015 | U | FakeTimeProvider | Usar quota após a validade | Rejeitada | P0 | F2 |
| TC-117 | SEC-REQ-015 | U | Signer de teste com chave gerada em runtime | Byte alterado; chave de outra finalidade; epoch divergente | Rejeitada | P0 | F2 |
| TC-118 | ADR-0005 critério 2 | U | Relógio local adiantado ou atrasado em relação ao tempo confiável | Autorizar | Rejeitada; a validade não é estendida | P0 | F2 |
| TC-119 | SEC-REQ-015 | I-W | — | Replay do mesmo `contingencyTxnId` ou da mesma chave | Deduplicado; 1 consumo | P0 | F2 |
| TC-120 | ADR-0005 item 2 | U (Theory) | — | Escopo divergente (conta, canal, região, dispositivo); quota de R1 usada em R2 | Rejeitada | P0 | F2 |
| TC-121 | ADR-0005 item 6 | I-W | Journal com N aprovações | `SettleContingency` repetido | 1 liquidação por transação; reconciliação journal × ledger com diferença zero | P0 | F2 |
| TC-122 | ADR-0005 critério 4 | C | hml, 2 regiões com quotas | Partição de rede sob carga | Σ aprovado em todas as regiões ≤ Σ quotas | P0 | F3 |
| TC-123 | ADR-0005 item 8, NFR-145 | I-W | — | Advice STIP duplicado | 1 lançamento forçado; política de saldo negativo [VALIDAR Risco/bandeira] | P1 | F2 |
| TC-124 | SEC-REQ-026, NFR-079 | I-W | Antifraude fake com timeout | Comando T0 | Resultado conforme a política configurada (DECLINED ou UNKNOWN [VALIDAR Risco]); **nunca APPROVED implícito** | P0 | F2 |
| TC-125 | ADR-0005 item 9 (CF-07) | I-W | Core com timeout dentro do prazo da rede | Autorização de cartão | Recusa externa, estado interno UNKNOWN, `ReleaseHold` compensatório idempotente. **Condicional à aprovação de Risco** | P0 | F2 |
| TC-126 | ADR-0005 (kill switch) | I-W | Feature flag desligada | Requisição elegível | UNAVAILABLE; mudança da flag auditada | P1 | F2 |
| TC-127 | ADR-0005 item 6 | I-W | Falha injetada após gravar no journal e antes da resposta | Reiniciar o CAE e repetir | O journal contém a transação; o replay é idempotente | P1 | F2 |

#### 4.2.7 CQRS e extrato (ADR-0004; SEC-REQ-014; TM-019)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-130 | ADR-0004 critério 1, SEC-REQ-014 | I-W | Read model/cache semeado com saldo 1000; ledger com 10 | Débito de 100 | DECLINED (decidido pelo ledger) | P0 | F1 |
| TC-131 | SEC-REQ-014, ADR-0009 | A | — | Assemblies e namespaces do caminho de débito | Sem referência a Statement/read models, `IDistributedCache`, `HybridCache`, `IMemoryCache` ou StackExchange.Redis | P0 | F0 |
| TC-132 | ADR-0004 critério 2, TM-019 | I-P | Clientes A e B; cache aquecido por B | A pede o extrato da conta de B; A pede a própria primeira página logo após B | Resposta uniforme (D-8); A nunca recebe dados de B (a cache key inclui o principal) | P0 | F2 |
| TC-133 | ADR-0004 | I-W | — | Reprocessar eventos `(journalId, postingSeq)` | Sem linhas duplicadas; watermark exposto e monotônico | P0 | F2 |
| TC-134 | NFR-043 | I-P | — | Comando seguido de comprovante | O comprovante vem do resultado do comando, não do read model | P1 | F2 |
| TC-135 | SEC-REQ-020 | I-P | — | Extrato sem page size e com page size acima do máximo | Default limitado; acima do máximo dá 400 | P1 | F2 |
| TC-136 | ADR-0004 critério 4 | K | 1 ano sintético | Rebuild em shadow | Dentro da janela aprovada (a definir) | P2 | F3 |
| TC-137 | ADR-0004 (contrapartes) | I-P | — | Extrato com contraparte | Contraparte mascarada | P1 | F2 |

#### 4.2.8 Hot accounts e saga (ADR-0010)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-140 | ADR-0010 | U | — | Montar journal de cliente | Contrapartida em sub-conta de clearing do shard do cliente; entry balanceada | P1 | F1 |
| TC-141 | ADR-0010 critério 3 | I-W | N ciclos de netting (FakeTimeProvider) | Rodar o netting | Clearing entre partições fecha em zero; saldo consolidado = Σ sub-contas | P1 | F2 |
| TC-142 | ADR-0010 critério 2 | I-W | Saga entre partições | Falha de negócio na perna 2 | `Reverse(perna 1)`; reconciliação com diferença zero; retries por perna idempotentes | P0 | F2 |
| TC-143 | ADR-0010, fluxo 4.1 | I-W | — | Timeout técnico na perna 2 | 202 PENDING ou UNKNOWN; sem nova perna 1; sem reversão prematura | P0 | F2 |

#### 4.2.9 Eventos e outbox (SEC-REQ-018)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-170 | SEC-REQ-018 | I-W | — | Entregar o mesmo `eventId` duas vezes | Efeito único | P0 | F2 |
| TC-171 | SEC-REQ-018 | I-W | — | Evento com hash ou assinatura inválida | Vai para DLQ sem efeito; DLQ sem PII | P1 | F2 |
| TC-172 | NFR-045 | I-W | — | Eventos fora de ordem | Retidos ou reordenados até a lacuna ser resolvida | P1 | F2 |
| TC-173 | ADR-0003 item 7, ADR-0008 | I-W/C | Replicação assíncrona | Lag de replicação simulado | O relay não publica além do watermark do LSN replicado | P1 | F3 |

#### 4.2.10 Privacidade e dados (ADR-0011/0012; SEC-REQ-017/028/029)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-150 | SEC-REQ-017, NFR-081/114 | I-W | LogCapture, SpanCapture, captura de outbox e DLQ, PiiScanner | Requisições com PAN de teste, CPF sintético e Bearer, inclusive provocando erros de validação | 0 ocorrências em logs, spans, atributos, eventos, Problem Details e DLQ (§5.9) | P0 | F0 (logs/spans) → F1/F2 (eventos) |
| TC-151 | SEC-REQ-017 | I-P | — | Payload inválido contendo PAN ou CPF | O `detail` do Problem Details não ecoa o valor | P0 | F0 |
| TC-152 | SEC-REQ-028, NFR-088 | G (xUnit) | — | Varrer `tests/**` | Nenhum PAN fora da allowlist, nenhum CPF fora da lista sintética aprovada, nenhum JWT ou chave privada versionados | P0 | F0 |
| TC-153 | ADR-0011, NFR-082 | A + I-W | — | Schema do ledger, read models e contratos de evento | Sem colunas ou campos de PII direta (nome, CPF, contato, PAN); payload de evento por allowlist | P1 | F1 |
| TC-154 | SEC-REQ-029 | I-P | Party | Acessar PII sem finalidade declarada | Negado e auditado | P1 | F2 |
| TC-155 | ADR-0011 critério 2 | I-W | Party com chave por titular | Crypto-shredding | Identificação ilegível; ledger intacto e reconciliável ([VALIDAR Jurídico] sobre a hipótese de eliminação) | P1 | F2 |
| TC-156 | ADR-0012 critério 3 | M/CI | hml | Scanner de PAN em logs e stores fora do CDE | 0 ocorrências | P0 | F3 |
| TC-157 | ADR-0012 (token-only) | A | — | Contratos públicos de Card/Payments/Core | Sem campos `Pan`, `CardNumber`, `Cvv`, `Pin`, nem equivalentes em ISO 8583 | P1 | F1 |

#### 4.2.11 Borda, plataforma e supply chain (SEC-REQ-020..024; ADR-0003 item 8; ADR-0012)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-180 | ADR-0003 item 8, SEC-REQ-020, ADR-0006 | I-P/C | NGINX de hml com upstream fake | POST com falha de upstream; corpo grande; inspecionar o log | POST não repetido pelo NGINX (o upstream conta 1 hit); 413; log sem `$http_authorization` | P0 | F3 |
| TC-181 | SEC-REQ-021 | CI | Pipeline com scanner de segredos | Segredo canário sintético em branch descartável ou diretório temporário | CI falha | P1 | F3 |
| TC-182 | SEC-REQ-022 | C | hml com admission | Deploy de imagem não assinada | Negado | P1 | F3 |
| TC-183 | SEC-REQ-023 | C | Mesh em STRICT | Pod sem identidade tenta alcançar o Core | Bloqueado | P1 | F3 |
| TC-184 | SEC-REQ-024 | I-W/C | Terminal Gateway | MAC inválido, STAN repetido, certificado revogado | Recusa | P1 | F3 |
| TC-185 | ADR-0012 critério 2 (complementar ao pentest) | C | hml com CDE segmentado | Conectividade de fora do CDE para Card/Vault | Negada | P1 | F3 |

#### 4.2.12 Arquitetura (ADR-0009)
| TC | Requisito | Nível | Pré-condição | Passos resumidos | Resultado esperado | Prio | Fase |
|---|---|---|---|---|---|---|---|
| TC-200 | ADR-0009 | A | Projetos do skeleton | Regras de dependência entre módulos | Payments depende só de contratos gRPC do Core; módulos do Core não acessam internals uns dos outros | P0 | F0 |
| TC-201 | ADR-0009 ("nenhum 2PC") | A | — | Procurar `TransactionScope` com promoção distribuída, `EnlistDistributedTransaction` e APIs de MSDTC | Nenhuma ocorrência | P1 | F0 |
| TC-202 | Clean Architecture (quando aplicável) | A | — | O domínio não depende de ASP.NET Core nem de EF | Conforme o desenho do skeleton (confirmar com o developer) | P2 | F0 |
| TC-203 | Determinismo (H-01) | A | — | `DateTime.Now`, `DateTime.UtcNow` e `DateTimeOffset.UtcNow` no domínio e na infraestrutura financeira | Nenhuma ocorrência (usar `TimeProvider`) | P1 | F0 |
| TC-204 | Regras financeiras | A | — | `double` ou `float` em tipos monetários | Nenhuma ocorrência | P1 | F1 |

#### 4.2.13 Carga (k6), ver §7
| TC | Requisito | Nível | Resumo | Prio | Fase |
|---|---|---|---|---|---|
| TC-300 | capacity §6 | K | Smoke | P0 | F3 |
| TC-301 | capacity §6/§7, consolidação §2 | K | Baseline | P0 | F3 |
| TC-302 | NFR-033 | K | Stress | P1 | F3 |
| TC-303 | capacity §6 | K | Spike | P1 | F3 |
| TC-304 | capacity §6 | K | Soak | P1 | F3 |
| TC-305 | capacity §6 | K | Capacity (degraus) | P2 | F3 |
| TC-306 | ADR-0010 critério 1 | K | Hot account (2× demanda) | P0 | F3 |
| TC-307 | ADR-0003, capacity §6 | K | Idempotência/replay | P0 | F3 |
| TC-308 | ADR-0002 critério 5 | K + C | Failover sob carga | P0 | F3 |
| TC-309 | ADR-0004 critério 3 | K | Extrato p95 | P1 | F3 |
| TC-310 | ADR-0008 critério 3 | K | Commit com sync intra-região | P1 | F3 |
| TC-311 | NFR-005 | K | Saturar T1 sem degradar T0 | P1 | F3 |
| TC-312 | SEC-REQ-005, NFR-027 | K | Tokens inválidos sob carga: 401 sem degradar válidos; refresh do JWKS limitado | P2 | F3 |

#### 4.2.14 Governança (SEC-REQ-030, G-14, ADR-0014 Proposed), ver §8
| TC | Requisito | Nível | Resumo | Prio | Fase |
|---|---|---|---|---|---|
| TC-400 | ADR-0014 critério 3 | G | Guard: stdin não JSON → exit 2 | P0 | FG |
| TC-401 | ADR-0014 item 2, G-02 | G | Guard: JSON bem formado com tipos inesperados → exit 2 (**hoje exit 1**: TA-001) | P0 | FG |
| TC-402 | SEC-REQ-030 | G (Theory) | Guard: test-analyst em `src/`, `infra/`, `docs/`, `.claude/`, `scripts/` → 2; `tests/` → 0 | P0 | FG |
| TC-403 | SEC-REQ-030 | G (Theory) | Guard: developer em `tests/` → 2; `src/` → 0 | P0 | FG |
| TC-404 | ADR-0014 item 2 | G (Theory) | Guard: `tests/../src/x`, absoluto fora do repositório, caixa diferente, ADS `::$DATA`, UNC, junction em `tests/` apontando para `src/` → bloqueado | P0 | FG |
| TC-405 | ADR-0014 item 2 | G | Guard: papel desconhecido ou ausente → 2 | P1 | FG |
| TC-406 | ADR-0014 item 2 | G | Guard: cwd diferente da raiz (raiz via `CLAUDE_PROJECT_DIR`) | P1 | FG |
| TC-407 | ADR-0014 critério 1/2 | G | Bash: developer com `echo x > tests/x.txt` e test-analyst escrevendo em `src/` → bloqueado (requer hook Bash ainda inexistente) | P0 | FG |
| TC-408 | ADR-0014 critério 4, G-04 | G | Log: 50 appends concorrentes → 50 linhas válidas e `messageId` únicos (**hoje falha no Windows**: TA-002) | P0 | FG |
| TC-409 | ADR-0014 item 4 | G | Log: `--type` inválido rejeitado; JWT, PAN e CPF sintéticos no `summary` → rejeitado ou redigido | P1 | FG |
| TC-410 | ADR-0014 critério 5 | G | Verificador da cadeia `prevHash`: linha alterada → falha | P1 | FG |
| TC-411 | ADR-0014 item 7, G-08 | G | Validador do time: tools por papel, hooks, skills, CRLF | P2 | FG |
| TC-412 | ADR-0014 critério 6 | CI | PR fora do escopo → check path-segregation falha | P1 | F3 |
| TC-413 | ADR-0014 item 6 | M | Deny efetivo do JSONL e `disableBypassPermissionsMode` (checklist manual) | P2 | FG |

### 4.3 Índice SEC-REQ → TC
| SEC-REQ | TCs |
|---|---|
| 001 | TC-003, 004, 005, 006, 017 |
| 002 | TC-007, 008, 009 |
| 003 | TC-010, 011, 012, 024 |
| 004 | TC-013, 014, 027 |
| 005 | TC-007, 015, 016, 017, 312 |
| 006 | TC-018, 019 |
| 007 | TC-021, 132 |
| 008 | TC-020, 023 |
| 009 | TC-040, 041, 042, 043, 044, 045, 050, 051, 307 |
| 010 | TC-046, 048 |
| 011 | TC-070, 071, 306 |
| 012 | TC-090, 091, 092 |
| 013 | TC-097, 099, 100, 308 |
| 014 | TC-130, 131 |
| 015 | TC-115, 116, 117, 118, 119, 120, 122 |
| 016 | TC-110, 111, 112, 113, 114, 125 |
| 017 | TC-019, 150, 151, 153 |
| 018 | TC-082, 170, 171, 172 |
| 019 | TC-025 |
| 020 | TC-135, 180, 217 |
| 021 | TC-181 |
| 022 | TC-182 |
| 023 | TC-183 |
| 024 | TC-184 |
| 025 | TC-074, 075 |
| 026 | TC-124 |
| 027 | TC-021, 132 |
| 028 | TC-152 |
| 029 | TC-154 |
| 030 | TC-400..413 (FG) |

### 4.4 Índice dos critérios de validação das ADRs → TC
| ADR | Critério | TCs | Observação |
|---|---|---|---|
| 0002 | Writer zumbi rejeitado | TC-090, 091 | |
| 0002 | Chaos de partição inter-AZ e inter-região sem duplo writer | TC-097 | |
| 0002 | Failover de AZ com RPO = 0 medido | TC-098 | |
| 0002 | Drill regional com RPO/RTO | TC-099 | |
| 0002 | k6 de failover | TC-308 | |
| 0003 | Sem chave → 400 | TC-040 | |
| 0003 | 10 paralelas → 1 lançamento | TC-042 | |
| 0003 | Divergente → 422; concorrente → 409 | TC-043, 044 | |
| 0003 | Timeout pós-commit → mesma resposta | TC-045 | |
| 0003 | 100 débitos, saldo para 10 → 10 | TC-070 | |
| 0003 | UPDATE no ledger → erro | TC-074 | |
| 0003 | Job de invariantes com 0 quebras | TC-076 | |
| 0003 | Replay total do broker → estado idêntico | TC-083 | |
| 0004 | Read model desatualizado não decide débito | TC-130 | |
| 0004 | Extrato cruzado negativo | TC-132 | |
| 0004 | k6 extrato p95 | TC-309 | |
| 0004 | Rebuild de 1 ano sintético na janela | TC-136 | Janela a aprovar |
| 0005 | Timeout pós-commit + retry: nunca segunda aprovação | TC-112, 045 | |
| 0005 | Quota esgotada, expirada, com assinatura inválida ou relógio adulterado | TC-115, 116, 117, 118 | Semântica [VALIDAR Risco] |
| 0005 | Replay offline deduplicado | TC-119 | |
| 0005 | Partição de rede: Σ aprovado ≤ Σ quotas | TC-122 | |
| 0005 | Advice STIP duplicado → 1 lançamento | TC-123 | |
| 0006 | Suíte negativa JWT → 401 | TC-003..011, 013..017 | |
| 0006 | Cliente em endpoint de operador → 403 | TC-012 | |
| 0006 | BOLA 404/403 uniforme | TC-021 | Regra canônica: D-8 |
| 0006 | `?access_token=` → 400 | TC-018 | TA-003 |
| 0007 | SoR errado rejeitado | TC-093 | |
| 0007 | Cutover com reconciliação zero | TC-094 | |
| 0007 | Rollback antes e depois do primeiro lançamento | TC-095 | |
| 0007 | Janela batch só com quotas do CAE | TC-096 | |
| 0008 | RTT medido entre sites | — | **Não é teste do test-analyst**: medição de DevOps; registrar como evidência de infra |
| 0008 | Drill regional com RPO/RTO | TC-099 | |
| 0008 | k6 de commit com sync intra-região | TC-310 | |
| 0008 | RECOVERY e reconciliação pós-failover com diferença zero | TC-099, 101 | |
| 0009 | Testes de arquitetura no CI | TC-131, 200..204 | |
| 0009 | Revisão de custo operacional por DevOps | — | Revisão, não teste |
| 0009 | Nenhum 2PC | TC-201 | |
| 0010 | k6 hot account 2× sem violar invariantes | TC-306 | |
| 0010 | Falha entre pernas sem diferença | TC-142, 143 | |
| 0010 | Clearing fecha em zero após N ciclos | TC-141 | |
| 0011 | Scanner de PII/PAN em logs, eventos e fixtures | TC-150, 152, 156 | |
| 0011 | Teste de crypto-shredding | TC-155 | |
| 0011 | Inventário de stores classificados | — | Documental (NFR-080); o test-analyst pode verificar o schema contra o inventário (TC-153) |
| 0012 | Documento de escopo aprovado pelo QSA | — | Externo |
| 0012 | Pentest de segmentação | TC-185 (complementar) | O pentest é externo |
| 0012 | Scanner de PAN fora do CDE com 0 ocorrências | TC-156 | |
| 0013 | `dotnet --info` e build limpo com evidência | TC-210 | |
| 0013 | Política de suporte em fonte oficial | — | Documental (orquestrador) |
| 0014 (Proposed) | Critérios 1–6 | TC-400..413 | Depende de D-1 |

### 4.5 Contagem de casos
| Fase | P0 | P1 | P2 | Total |
|---|---|---|---|---|
| F0 (skeleton) | 25 | 11 | 2 | 38 |
| F1 (domínio Core) | 22 | 12 | 1 | 35 |
| F2 (Payments/CAE/Statement/Card/Party) | 20 | 15 | 0 | 35 |
| F3 (infra/hml/SLOs) | 11 | 18 | 4 | 33 |
| FG (governança) | 7 | 4 | 2 | 13 |
| **Total** | **85** | **60** | **9** | **154** |

Os números contam linhas do catálogo §4.2. TC-150 é contado em F0 porque tem uma parte F0 executável. A contagem foi gerada por script sobre este arquivo e precisa ser recalculada a cada revisão.

---

## 5. Suítes P0 detalhadas

### 5.1 Autorização negativa (TC-001..026)
- **Fixture:** `FakeIdp`, ver §6.1. Cada classe de teste recebe um par de chaves novo, gerado em runtime.
- **Matriz de tokens:** uma Theory com `TokenMutation` enumerada (`AlgNone`, `HsWithPublicKey`, `DisallowedAlg(x)`, `TamperedPayload`, `UnknownKid`, `MaliciousKid(x)`, `EmbeddedJku`, `EmbeddedX5u`, `EmbeddedJwk`, `WrongIss`, `WrongAud`, `MissingAud`, `ExpiredBeyondSkew`, `MissingExp`, `NbfFuture`, `ServiceAudience`). Cada mutação é aplicada a **todo** endpoint protegido descoberto por TC-022, não a um endpoint de exemplo.
- **Asserções comuns:** status 401; `WWW-Authenticate` presente sem descrição que ajude o atacante (por exemplo, sem `error_description` detalhando a falha); 0 efeitos colaterais; 0 requisições de saída inesperadas (contador do FakeIdp e do listener atacante); nenhum token em logs (LogCapture).
- **JWKS indisponível (TC-015):** três variantes (503, timeout acima do deadline do backchannel, porta fechada), no cold start e após o expurgo do cache. Esperado: 401. Um 500 é falha, porque indica fail-unsafe de observabilidade e contrato.
- **BOLA (TC-021):** matriz `endpoint × verbo × {recurso próprio, recurso de terceiro, recurso inexistente, ID malformado}`.
  - O recurso próprio dá 2xx.
  - Terceiro e inexistente precisam ser **byte-equivalentes**, normalizando `traceId`/correlation-id: status, `type` e `title`.
  - Timing: diferença de mediana abaixo de uma tolerância calibrada (registrada como métrica, não como gate, para evitar flakiness).
- **Token em query (TC-018):** 400 Problem Details também quando há header válido junto. Testar `access_token`, `token`, `id_token` e `bearer`, em qualquer caixa.

### 5.2 Idempotência (TC-040..051)
- Conta sintética isolada por teste; PostgreSQL efêmero com as migrações reais.
- **Paralelismo real (TC-042):** `Task.WhenAll` com uma barreira (`Barrier`/`TaskCompletionSource`) para disparar as 10 requisições juntas. Repetir N = 20 iterações por execução para aumentar a chance de pegar uma race. Asserções sobre o banco: `count(journal where key=K) = 1`, `count(outbox where key=K) = 1`.
- **Timeout pós-commit (TC-045):** seam H-03 (`AfterCommitFault`) descarta a resposta depois do commit. O cliente recebe timeout e reenvia. Compara o corpo canônico (JSON normalizado) e o status com a resposta gravada; exige `Idempotent-Replayed: true`.
- **Em andamento (TC-044):** seam `BeforeCommitBarrier` segura a 1ª requisição depois do registro IN_PROGRESS. Esperado: 409 com `Retry-After` > 0.
- **Divergente (TC-043):** variar só um campo por vez (valor, destino, moeda) em uma Theory.

### 5.3 Concorrência (TC-070, 071)
Saldo = 10 × valor. 100 débitos com chaves distintas, disparados com barreira e pool de conexões compatível com a configuração de produção.

Asserções:
- 10 APPROVED;
- 90 DECLINED com motivo `insufficient-funds` (nenhum 5xx, nenhum UNKNOWN);
- saldo contábil e disponível = 0;
- Σ D = Σ C global;
- nenhuma posição negativa em nenhum instante (consultar o histórico de sequência).

Repetir com 1 000/100 em execução noturna (P1).

### 5.4 Fencing (TC-090..092)
- **TC-090:** atualizar `partition_control.epoch` para 6 por SQL administrativo e executar o comando com token 5. Esperado: erro de fencing; 0 linhas novas em journal, outbox e idempotência.
- **TC-091:** writer A lê o estado e para numa barreira antes do commit. Writer B incrementa o epoch e comita. A é liberado. Esperado: A falha. Isso só passa se a verificação do epoch estiver **na mesma transação** e protegida por lock ou condição.

### 5.5 UNKNOWN nunca vira APPROVED/DECLINED (TC-110..114, 124, 125)
- **Unidade:** tabela completa `estado × evento → estado|erro`. Theory gerada a partir de um enum, para que um estado novo sem regra quebre o teste.
- **Integração:** seam no cliente gRPC Payments → Core: o servidor comita e o cliente recebe `DeadlineExceeded`. Esperado:
  - ordem em UNKNOWN;
  - `GetCommandResult(key)` resolve para o resultado original;
  - nenhum novo `DebitLeg` com chave diferente (inspecionar as chaves recebidas pelo Core);
  - CAE fake com 0 chamadas.

### 5.6 CAE (TC-115..127)
`ISigner` de teste com chave gerada em runtime (nunca uma chave no repositório) e `FakeTimeProvider`. Cada mutação tem seu caso: esgotada, expirada, assinatura inválida, epoch divergente, escopo divergente, relógio adulterado e replay. A semântica DECLINED vs UNAVAILABLE é parametrizada por política (TA-006). O invariante Σ aprovado ≤ quota é verificado depois de cada teste.

### 5.7 Ledger append-only e Σ D = Σ C (TC-072, 074..077)
A role do serviço é usada para `UPDATE` e `DELETE` (esperado: erro). A role de superuser do container serve só para **adulterar** dados e provar que o verificador detecta. O job de invariantes roda após **todo** teste de integração financeira (fixture de teardown), com falha se houver quebra.

### 5.8 Read model desatualizado (TC-130, 131)
O read model e o cache são semeados com um saldo maior que o do ledger. O débito precisa ser decidido pelo ledger. O arch-test TC-131 impede regressões estruturais desde F0.

### 5.9 Redação de PII, PAN e token (TC-019, 150, 151)
- **Captura:** provider `ILogger` em memória; OpenTelemetry `InMemoryExporter` para spans e logs; interceptação do outbox e da DLQ; corpos de resposta de erro.
- **Detectores do `PiiScanner`:**
  - sequência de 13–19 dígitos (com ou sem separadores) válida em Luhn;
  - CPF em formato `000.000.000-00` ou com 11 dígitos e DV válido;
  - regex de JWT `eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.`;
  - os valores exatos injetados no teste (canários únicos por execução).
- **Falsos positivos:** IDs numéricos longos podem passar no Luhn. Por isso a asserção primária é sobre os **canários injetados**, e a heurística vira um relatório P1.

---

## 6. Infraestrutura de teste

### 6.1 IdP fake
- Chaves RSA (RS256/PS256) e ECDSA (ES256) **geradas em runtime** por fixture. Nunca são persistidas em disco nem no repositório.
- JWKS e documento de discovery servidos por um `HttpMessageHandler` in-process (TestServer) ou por Kestrel em porta efêmera (cenário Playwright).
- O host em teste aponta para o fake só por configuração do ambiente `Testing` (Authority/MetadataAddress e backchannel). A validação continua com `Microsoft.AspNetCore.Authentication.JwtBearer`, sem bypass (H-02).
- O `JwtFactory` monta tokens válidos e mutações (§5.1). Contadores de requisição servem a TC-007/008/009/015.
- Em hml, os tokens vêm de clients de teste do IdP escolhido, com segredos no secret manager.

### 6.2 Banco efêmero
- **Recomendado:** Testcontainers (`Testcontainers.PostgreSql`) com a imagem e a versão iguais às de produção, migrações reais e roles reais.
- Isolamento:
  - um container por coleção xUnit;
  - um banco por classe, criado a partir de um template para ganhar velocidade;
  - contas sintéticas únicas por teste.
- **Dependência de Docker** no desenvolvimento e no CI (PT-04). Alternativas se o Docker não estiver disponível no CI:
  - (a) serviço PostgreSQL do runner (`services:`);
  - (b) instância compartilhada de dev com schema por execução.

  As duas reduzem o isolamento e precisam de decisão (D-4).
- Para falhas de rede (latência, reset, timeout), usar um container Toxiproxy entre a aplicação e o banco ou o gRPC.

### 6.3 Relógio controlável
`TimeProvider` injetado em todo código de expiração (holds, quotas, idempotência, leases, netting), com `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing) nos testes. O arch-test TC-203 proíbe `DateTime.UtcNow` direto (H-01). Limitação: a validação de `exp`/`nbf` do JwtBearer pode não usar o `TimeProvider`, então TC-013 usa margens sobre o relógio real.

### 6.4 Injeção de falhas
Seams solicitados ao `developer` (H-03), registráveis **apenas** no ambiente `Testing` e verificados por TC-218:
- `BeforeCommitBarrier`;
- `AfterCommitFault` (descartar a resposta ou lançar timeout depois do commit);
- interceptor no cliente gRPC (`DeadlineExceeded` depois de o servidor concluir);
- falha no relay do outbox;
- crash simulado do CAE depois do journal.

Fakes externos: antifraude (latência e timeout configuráveis), control plane (declaração de CONTINGENCY), CAE (contador de chamadas), broker (Testcontainers, ou in-memory onde a semântica permitir).

### 6.5 Dados sintéticos
- Geradores determinísticos com seed registrada na evidência.
- Nomes obviamente fictícios ("Cliente Sintetico 000123").
- **PAN:** apenas números de teste publicados pelas bandeiras ou fornecidos pela processadora, em allowlist ([VALIDAR processadora/bandeiras]).
- **CPF:** apenas da lista sintética aprovada (D-9). Um CPF com DV válido pode coincidir com o de uma pessoa real (TA-007), então, onde a validação de DV não é exercitada, preferir valores com DV inválido.
- **Tokens:** sempre emitidos em runtime; nunca versionados.
- k6: contas sintéticas em faixa dedicada por execução (`runId`) para a reconciliação.
- Cleanup idempotente.

---

## 7. Plano k6 (TC-300..312)

### 7.1 Regras
- Scripts em `tests/Performance/k6/`.
- Modelo aberto: `constant-arrival-rate` e `ramping-arrival-rate`.
- Execução apenas em hml ou em ambiente dedicado. **Nunca produção** sem autorização explícita e plano de segurança.
- Tokens via variáveis de ambiente/secret do runner (`K6_BC_TOKEN_*`), nunca commitados.
- Toda execução registra versão do script, commit, ambiente, parâmetros, seed, resultado e limitações.

### 7.2 Dados e distribuição
- ≥ 1M contas sintéticas pré-carregadas em hml.
- Seleção com distribuição Zipf, com expoente a calibrar (proposta inicial s ≈ 1,0) e documentado.
- Um cenário deliberado de concorrência na mesma conta e na contrapartida (hot account).
- Idempotency key nova por iteração, exceto nos cenários de replay.

### 7.3 Cenários
| TC | Cenário | Perfil (capacity §6) | Aceite candidato |
|---|---|---|---|
| TC-300 | Smoke | 1–5 VU, 2 min, todas as rotas | 0 erros de negócio; checks 100% |
| TC-301 | Baseline | Pico normal do cenário B, 30 min | Tabela 7.4; erro técnico < 0,1% |
| TC-302 | Stress | Até 2× o pior dia ou até saturar | 429/503 com Problem Details; sem cascata; 0 perda ou duplicidade (reconciliação) |
| TC-303 | Spike | 0 → 3× o pico em 30 s | p99 recuperado em ≤ 2 min |
| TC-304 | Soak | 70% do pior dia, 8–24 h | Deriva de p99 < 10%; lag do outbox e memória estáveis |
| TC-305 | Capacity | Degraus de +10% | Curva throughput × latência por pod e por shard |
| TC-306 | Hot account | 2× a demanda da contrapartida | 0 violação de saldo; journal balanceado; clearing zera |
| TC-307 | Replay | 5–10% de requisições repetidas com a mesma chave e payload, inclusive após timeout; 1% com payload divergente | 0 duplicidade no ledger; divergentes = 422 |
| TC-308 | Failover | Queda de writer, AZ e região sob carga (com o caos) | 0 split-brain; UNKNOWN resolvidos; reconciliação com diferença zero; RTO medido |
| TC-309 | Extrato | Primeira página e páginas até 50 itens | Tabela 7.4 |
| TC-310 | Commit sync intra | Escrita sustentada | Commit do ledger p99 ≤ 40 ms (candidato) |
| TC-311 | Isolamento T0/T1 | Saturar o extrato e medir T0 | T0 dentro do SLO (NFR-005) |
| TC-312 | Autenticação sob carga | 20% de tokens inválidos | 401 sem degradar válidos; refresh do JWKS limitado |

### 7.4 Thresholds candidatos (fixar só após aprovação dos SLOs, PERF-006)
Fonte: tabela harmonizada (consolidação §2), que prevalece sobre `nfr.md`. Os valores ficam em `thresholds/candidate.json`. O arquivo `approved.json` só será criado depois da aprovação. Até lá, falhar um threshold candidato gera um **finding**, não um bloqueio.

| Métrica (tag `op`) | p95 | p99 |
|---|---|---|
| `card_authorization` | ≤ 150 ms | ≤ 250 ms |
| `transfer_accepted` | ≤ 250 ms | ≤ 500 ms |
| `balance` | ≤ 100 ms | ≤ 200 ms |
| `statement_first_page` | ≤ 150 ms | ≤ 300 ms |
| `statement_page` (≤ 50 itens, ≤ 90 dias) | ≤ 300 ms | ≤ 800 ms |
| `http_req_failed` (técnico) | < 0,1% | — |
| `business_unknown_rate` (métrica custom) | ≤ 0,01% | — |
| `checks` | > 99,9% | — |

Frescor do extrato (p95 ≤ 1 s, p99 ≤ 5 s) e idade do outbox (p99 ≤ 2 s) são medidos pela telemetria do ambiente durante o teste, não pelo k6.

### 7.5 Checks de negócio e reconciliação pós-teste
- Os checks validam o status de negócio no corpo (`APPROVED`, `DECLINED` com motivo, `UNKNOWN`, `PENDING`). O k6 registra contadores por status.
- **Reconciliação obrigatória** depois de cada execução financeira:
  1. job de invariantes (Σ D = Σ C, sequência sem lacunas) sem quebras;
  2. contagem de APPROVED no k6 igual aos lançamentos da faixa `runId`;
  3. 0 chaves com mais de um efeito;
  4. todos os UNKNOWN resolvidos dentro do prazo [VALIDAR];
  5. clearing zerado.

  Isso depende de os jobs de reconciliação estarem acessíveis em hml (H-09, TA-011).

---

## 8. Testes de governança (SEC-REQ-030, G-14, ADR-0014)

### 8.1 O que testar
- **Guard (`scripts/guard-agent-write.py`):** TC-400..406. Matriz de stdin (não JSON, tipos inesperados, campos ausentes), papel, caminho (travessia, absoluto, caixa, ADS, UNC, junction) e cwd.
- **Bash (TC-407):** só é aplicável depois que o hook Bash da ADR-0014 existir. Hoje o esperado é **falhar**, o que documenta G-01.
- **Log (`scripts/log-agent-message.py`):** TC-408..410. O teste roda numa **cópia** do script em um diretório temporário que imita a estrutura do repositório, com o hash do script comparado ao original. Isso é obrigatório porque o script resolve a raiz por `__file__`, e rodá-lo no lugar escreveria no log real (proibido).
- **Validador (TC-411)** e **servidor/CI (TC-412)**.

### 8.2 Questão aberta: framework (decisão do usuário, D-1)
O CLAUDE.md fixa xUnit para unidade. Os scripts são Python.

| Opção | Prós | Contras |
|---|---|---|
| **(1) xUnit invocando processo** (`Process` com stdin controlado, cwd temporário, asserção de exit code, stderr e arquivo resultante) — **recomendada** | Mantém a stack mandatória; roda no mesmo `dotnet test` do CI; testa o contrato real do hook (exit code e stdin), que é o que o Claude Code consome | Caixa-preta, sem cobertura de linhas Python; depende de `python3` no runner |
| (2) pytest | Idiomático para Python; permite testes de função | Introduz um framework fora da stack mandatória; exige alteração da regra ou exceção registrada em ADR |

A recomendação é a opção 1. Ela fica **pendente de decisão do usuário** junto com a ADR-0014 (Proposed). Até lá, nenhum projeto de governança será criado.

### 8.3 Evidência já coletada (sondas não destrutivas, 2026-11-09, commit `b71c714`, host Windows 11, Python 3.15.2)
1. **Guard (G-02, TA-001):** `printf '<entrada>' | python3 scripts/guard-agent-write.py test-analyst; echo $?`
   - `not-json` → 2
   - `{"tool_input":{}}` → 2
   - `src/a.cs` → 2
   - `tests/a.cs` → 0
   - **`[]` → 1**
   - **`"str"` → 1**
   - **`{"tool_input":"x"}` → 1**
   - **`{"tool_input":{"file_path":123}}` → 1**

   Pela semântica de hooks do Claude Code, só o exit 2 bloqueia, então exit 1 é **fail-open**. Na prática o stdin é montado pelo harness, o que reduz a explorabilidade, mas o comportamento viola o requisito fail-closed.
2. **Log (G-04, TA-002):** cópia do script (SHA-256 igual ao original, prefixo `71f4a3ba5a2df6f9`) num diretório temporário fora do repositório; 50 processos concorrentes por rodada.
   - Rodadas com exit code capturado: 5. Em todas, 50/50 processos terminaram com exit 0.
   - Linhas gravadas por rodada: 50, 50, 50, **49**, **49**.
   - Rodada exploratória anterior: 50, **48**, 50.

   Conclusão: há **perda silenciosa** de registros. Sem `fcntl` no Windows, o script não usa lock. Nenhuma linha corrompida foi observada; as linhas faltaram.

---

## 9. Critérios, evidência, ambientes, dados, riscos e dependências

### 9.1 Critérios de entrada
- **F0:** skeleton compilando (TC-210), arquivos de build resolvidos (D-2), endpoint protegido disponível (PT-02), seams H-02 disponíveis.
- **F1:** migrações do Core, roles do banco, seams H-01/H-03/H-04, Docker no CI (D-4).
- **F2:** contratos de Payments, CAE, Statement e Party; fakes definidos; políticas de Risco parametrizáveis.
- **F3:** hml provisionado, k6 instalado no runner, SLOs aprovados (para gates), ferramenta de caos e responsável definidos (TA-008).
- **FG:** decisão D-1 e itens aprovados da ADR-0014.

### 9.2 Critérios de saída
- 100% dos P0 da fase passando, em 3 execuções consecutivas sem flakiness.
- P1 ≥ 95%, com as falhas registradas como findings e dono definido.
- Job de invariantes sem quebras depois da suíte.
- Scanner de PII (TC-150/152) sem achados.
- Nenhum teste em quarentena sem finding associado.
- Defeito de produção revelado vira finding para o `developer`; o `test-analyst` não corrige produção.

### 9.3 Evidência obrigatória
Cada execução registra comando exato, resultado (TRX/JUnit; resumo k6 em JSON), commit (`git rev-parse HEAD`), ambiente, seed e duração. Os resultados brutos ficam em `tests/TestResults/` (a ser ignorado no git: pedir ao orquestrador ou developer que inclua no `.gitignore`). O resumo vai para `docs/evolution/` pelo orquestrador. Nunca afirmar que os testes passaram sem essa evidência.

### 9.4 Ambientes
- **dev:** local e CI, com Testcontainers.
- **hml:** carga, caos e IdP real, apenas com dados sintéticos.
- **Produção:** proibida.
- Credenciais de teste de hml ficam no secret manager.

### 9.5 Riscos e dependências
| ID | Risco ou dependência | Impacto | Mitigação |
|---|---|---|---|
| R-01 | SLOs não aprovados (PERF-006) | Thresholds k6 não podem bloquear | Thresholds candidatos geram findings |
| R-02 | Domínio ainda inexistente | 80% dos P0 em F1/F2 | Começar por F0 e arch-tests |
| R-03 | Docker indisponível no CI | Sem integração realista | D-4 |
| R-04 | IdP não escolhido | TC-025/027 bloqueados | FakeIdp em dev; hml depois da ADR do IdP |
| R-05 | Semânticas de Risco pendentes | TC-115/124/125/123 sem oráculo fixo | Parametrização e [VALIDAR] |
| R-06 | Ambiente hml e ferramenta de caos inexistentes | F3 inteiro bloqueado | Planejar com DevOps; Toxiproxy local como ensaio parcial |
| R-07 | Flakiness em testes de concorrência | Falsos positivos ou negativos | Barreiras explícitas, N iterações, sem `sleep`, seeds registradas |
| R-08 | Seams de teste vazando para produção | Falha de segurança | Registro só em `Testing` e TC-218 |
| R-09 | Volume de dados sintéticos (≥ 1M contas) | Custo e tempo de carga | Gerador em massa direto no banco de hml (H-09) |

---

## 10. Findings deste plano (TA-xxx)
| ID | Sev. | Descrição | Evidência | Encaminhamento |
|---|---|---|---|---|
| TA-001 | Alta | O guard retorna **exit 1** (não bloqueante) para JSON bem formado com tipos inesperados: fail-open. Confirma G-02 (antes "não verificado") | §8.3 item 1 | `developer`, com autorização do usuário (ADR-0014 item 2) |
| TA-002 | Média-Alta | O log JSONL **perde registros silenciosamente** em appends concorrentes no Windows (até 2/50), com exit 0 | §8.3 item 2 | `developer`, com autorização do usuário (ADR-0014 item 4); manter a gravação sequencial pelo orquestrador |
| TA-003 | Média | Códigos divergentes: SEC-REQ-006 ("400/401") vs. ADR-0006 ("400") para token em query; SEC-REQ-009 ("409/422") vs. ADR-0003/CF-10. O plano adota as ADRs | Documentos citados | security-privacy harmonizar no threat model v0.2 |
| TA-004 | Média | "404/403 uniforme" sem regra canônica. Proposta: recurso de terceiro ou inexistente → 404; falta de scope/role no endpoint → 403 | ADR-0006, SEC-REQ-007/027 | Decisão D-8 (security-privacy + arquitetura) |
| TA-005 | Média | `global.json`, `Directory.Build.props`, `Directory.Packages.props` e `nuget.config` em `src/` não se aplicam a `tests/` (lookup ascendente). Consequência: SDK não fixado e testes sem package source mapping (TM-023). **Verificado** no skeleton em andamento (não commitado) em 2026-11-09: `src/` contém `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `NuGet.config` e `BancoCarrefour.slnx`, e a raiz não contém nenhum deles | §3.1 | D-2 (orquestrador + developer) |
| TA-006 | Média | Semântica de quota esgotada (DECLINED vs UNAVAILABLE), de antifraude em timeout e de saldo negativo em STIP não definida | ADR-0005, SEC-REQ-015/026 | Risco [VALIDAR] |
| TA-007 | Média | Um CPF "sintético" com DV válido pode coincidir com um CPF real; falta uma lista aprovada | — | Compliance (D-9) |
| TA-008 | Média | Experimentos de caos exigem manifests Kubernetes, que o `test-analyst` não pode escrever. Falta definir quem materializa | CLAUDE.md | Orquestrador: proposta de spec em `tests/Chaos/` e manifests pelo developer em `infra/` |
| TA-009 | Baixa | k6 não está instalado neste host e não há hml | PT-05 | DevOps/developer |
| TA-010 | Baixa | FluentAssertions ≥ 8 tem licença comercial | — | D-3 |
| TA-011 | Média | A reconciliação pós-k6 e a medição de RPO exigem identificar os comandos por execução e acessar os jobs de invariantes e reconciliação em hml | §7.5 | `developer` (H-09) |
| TA-012 | Baixa | O limite de refresh do JWKS em `kid` desconhecido ("rate limited") não tem valor definido; TC-007 precisa de um oráculo | ADR-0006 | security-privacy/developer definirem o parâmetro |

## 11. Handoffs solicitados
**Ao `developer` (seams e pré-requisitos no código de produção; o `test-analyst` não os implementa):**
- **H-01:** `TimeProvider` injetado em todo código temporal; sem `DateTime.UtcNow` direto (opcional: banned API analyzer).
- **H-02:** configuração do JwtBearer sobrescrevível no ambiente `Testing` (Authority/MetadataAddress, ValidIssuer/ValidAudiences, backchannel handler), sem relaxar a validação; startup falha se configuração de teste aparecer em `Production` (TC-218).
- **H-03:** seams de falha registráveis só em `Testing`: `BeforeCommitBarrier`, `AfterCommitFault`, interceptor no cliente gRPC, falha no relay e crash do CAE depois do journal.
- **H-04:** migrações executáveis contra PostgreSQL efêmero; role do serviço só com INSERT no journal; SQL administrativo para manipular o epoch em teste.
- **H-05:** geração de IDs injetável (opcional, para determinismo).
- **H-06:** redação na origem (por exemplo, `Microsoft.Extensions.Compliance.Redaction` com classificações) e possibilidade de adicionar exporters em memória nos testes.
- **H-07:** policy de autorização explícita em toda rota e método gRPC, com allowlist anônima declarada.
- **H-08:** decisão e execução da opção de build (§3.1); inclusão ou referência dos projetos de teste na solução; `tests/TestResults/` no `.gitignore`.
- **H-09:** hml com carga em massa de dados sintéticos e jobs de invariantes e reconciliação consultáveis pelo pipeline de teste.
- **H-10:** abstração `ISigner` para quotas CAE, com implementação de teste baseada em chave gerada em runtime.

**Decisões necessárias:**
- **D-1 (usuário):** framework dos testes de governança (recomendado: xUnit invocando processo), junto com a ADR-0014.
- **D-2 (orquestrador/developer):** localização dos arquivos de build (recomendado: raiz).
- **D-3 (orquestrador):** aprovação de dependências de teste: xUnit (v3, confirmar o adaptador Playwright), Microsoft.Playwright, Microsoft.AspNetCore.Mvc.Testing, Testcontainers.PostgreSql, Microsoft.Extensions.TimeProvider.Testing, biblioteca de arch-test (NetArchTest.eNhancedEdition ou ArchUnitNET), property-based opcional (FsCheck ou CsCheck), biblioteca de asserções.
- **D-4 (orquestrador/DevOps):** Docker nos runners de CI.
- **D-5 (negócio):** aprovação dos SLOs (PERF-006).
- **D-6 (Risco):** semânticas de TA-006 e CF-07.
- **D-7 (orquestrador):** responsabilidade pelos experimentos de caos (TA-008).
- **D-8 (security-privacy):** regra canônica 404/403 (TA-004).
- **D-9 (Compliance):** lista de CPFs sintéticos e allowlist de PANs de teste.
