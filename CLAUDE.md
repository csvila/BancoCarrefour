# Instruções do projeto

## Missão
Construir uma plataforma bancária resiliente para 1–10 milhões de clientes, com core híbrido, canais Web/Mobile/Totem/ATM/POS/APIs, C# 14, Bearer Token, alta disponibilidade, consistência financeira e conformidade LGPD.

## Governança inegociável de alterações
Somente dois agentes podem alterar código:
1. `developer`: implementação e correções de produção, infraestrutura como código e configuração executável.
2. `test-analyst`: exclusivamente criação e manutenção de testes, fixtures e dados de teste.

Os demais agentes são estritamente consultivos e somente leitura. Eles NÃO podem editar código, testes, configurações, documentação ou infraestrutura. Ao encontrar um problema, devem produzir um finding e solicitar ao `developer` ou `test-analyst` a alteração cabível. O agente principal/orquestrador registra todas as mensagens e delegações em `docs/governance/agent-communications.jsonl`.

O `developer` não deve alterar testes existentes, exceto quando explicitamente autorizado pelo usuário; mudanças em testes pertencem ao `test-analyst`. O `test-analyst` nunca altera produção, Dockerfiles, manifests Kubernetes, NGINX, scripts de deploy ou configuração de runtime.

## Processo obrigatório
1. Ler `docs/governance/PROJECT-STATUS.md` e `docs/governance/DECISIONS.md` antes de iniciar trabalho relevante.
2. Registrar cada delegação, mensagem, resposta, finding, decisão e handoff no log JSONL de comunicação. Nunca registrar segredos, tokens reais, dados pessoais ou payloads financeiros reais.
3. Toda alteração arquitetural relevante exige ADR antes da implementação.
4. Toda alteração de arquitetura deve atualizar os quatro níveis C4 quando aplicável.
5. Toda sessão de trabalho deve atualizar `docs/evolution/` e `PROJECT-STATUS.md`.
6. Não alegar que testes passaram sem executar e guardar evidência do comando, resultado e commit.
7. Não inventar requisitos regulatórios. Marcar dúvidas para validação jurídica/compliance.
8. Falhas de autorização nunca devem ser convertidas em aprovação silenciosa. `UNKNOWN` após timeout não equivale a `DECLINED` nem a `APPROVED`.

## Stack mandatória
- C# 14 / .NET 10 (confirmar SDK instalado e suporte de produção antes de fixar o target framework).
- APIs REST autenticadas por `Authorization: Bearer <token>`; validar assinatura, issuer, audience, expiração, algoritmo permitido, scopes/roles e rotação de chaves. Nunca implementar autenticação própria de tokens.
- Unidade: xUnit.
- Integração: Microsoft Playwright para .NET (`Microsoft.Playwright`) usando APIRequestContext e/ou navegador conforme o cenário; testes de integração de backend podem combinar xUnit + WebApplicationFactory, mas o requisito Playwright deve ser atendido onde interação de API/UI for necessária.
- Carga: k6 (scripts JavaScript do k6; não confundir com C#).
- Infra: NGINX, Docker e Kubernetes.

## Regras financeiras
- Ledger é fonte oficial; broker não é ledger.
- Partidas dobradas; lançamentos imutáveis; correções por reversão/compensação, nunca edição destrutiva.
- Idempotência obrigatória em comandos financeiros e nas integrações externas.
- Outbox transacional para publicar eventos após commit.
- Débito, reserva, captura, estorno, reversão, crédito pendente e liquidação são estados distintos.
- Saldo disponível e saldo contábil são conceitos distintos.
- Escrita financeira exige autoridade única por conta/partição, fencing token e proteção contra split-brain.
- Nunca assumir exactly-once ponta a ponta; projetar entrega at-least-once + idempotência + deduplicação.
- Cache e read models nunca autorizam débitos.
- Contingência/offline só pode aprovar operações cobertas por quotas/reservas previamente alocadas e política de risco aprovada. Se a decisão não puder ser tomada com segurança, recusar ou retornar estado não conclusivo.

## Segurança e privacidade
- Privacy/Security by Design e Zero Trust.
- Minimização de dados; não colocar PAN completo, CVV, PIN, bearer tokens, CPF completo ou payloads sensíveis em logs/eventos de forma desnecessária.
- Segredos apenas em secret manager/KMS/HSM; nunca em repositório.
- Criptografia em trânsito e repouso; autorização por conta/tenant/cliente em cada requisição.
- Tratar dados financeiros identificáveis como dados pessoais sob LGPD; não afirmar que todo dado financeiro é automaticamente dado pessoal sensível no sentido do art. 5º, II.
- Avaliar PCI DSS para o ambiente de dados de cartão e normas do BCB/CMN com Compliance/Jurídico.

## Qualidade
- Clean Architecture/vertical slices quando fizer sentido; evitar abstrações sem benefício.
- Async I/O, cancellation tokens, timeouts explícitos, bounded retries com jitter, circuit breaker e bulkheads.
- APIs versionadas, Problem Details, correlation/trace IDs, OpenAPI.
- Nenhum retry cego de operação financeira sem idempotency key.
- Testes de concorrência, replay, timeout após commit, failover, fencing, reconciliação e autorização negativa.

## Diretórios
- `src/`: código de produção (developer).
- `tests/`: testes (test-analyst).
- `infra/`: NGINX, Docker, Kubernetes e IaC (developer; DevOps apenas revisa).
- `docs/`: documentação; alterações documentais são feitas pelo orquestrador ou developer quando vinculadas à implementação. Especialistas apenas entregam conteúdo/findings.
- `docs/governance/agent-communications.jsonl`: trilha append-only de comunicação.

## Definition of Done
Build limpo; testes relevantes executados; revisão de segurança e privacidade; observabilidade; documentação e ADR/C4 atualizados; riscos conhecidos registrados; nenhum segredo/dado real; logs de comunicação completos.
