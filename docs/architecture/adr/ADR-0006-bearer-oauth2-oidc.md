# ADR-0006 — Autenticação Bearer via OAuth2/OIDC com IdP externo

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09)
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumos de security-privacy, software-architect, solution-architect
- Revisores: usuário (aprovado em 2026-11-09); escolha do produto de IdP pendente (D-IdP)

## Contexto
O CLAUDE.md exige APIs com `Authorization: Bearer`, validação completa de token e nenhuma autenticação própria. Os canais incluem browser, mobile, dispositivos, parceiros e operadores.

## Problema
Definir o perfil de tokens e de validação que cubra todos os canais com Zero Trust, sem colocar o IdP no caminho síncrono de cada transação.

## Requisitos e restrições
NFR-006, NFR-026/027, NFR-070..078. TM-001..008, TM-020, TM-024, TM-033, TM-040/041.

## Alternativas consideradas
1. **Validação apenas no gateway.** Rejeitada: viola Zero Trust.
2. **Introspecção a cada chamada.** Rejeitada como padrão: latência e acoplamento ao IdP. Admitida para revogação de operador/alto risco.
3. **Tokens no browser (SPA pura).** Rejeitada: risco de exfiltração; adotar BFF.
4. **IdP SaaS vs self-hosted.** Decisão de produto separada, condicionada a residência e certificação OIDC/FAPI.

## Decisão
- **Fluxos:**
  - Web via BFF (Authorization Code + PKCE, tokens no servidor, cookie Secure/HttpOnly/SameSite).
  - Mobile com Authorization Code + PKCE e secure storage do SO.
  - Parceiros com client credentials e `private_key_jwt` + mTLS.
  - Operadores com MFA, JIT e audience separada.
  - Dispositivos com certificado por terminal + token de dispositivo.
- **Validação em toda API:** gateway pré-valida **e** cada serviço revalida com `Microsoft.AspNetCore.Authentication.JwtBearer`.
  - Algoritmos permitidos fixos (RS256/PS256/ES256); `none` e HS* rejeitados.
  - `iss` e `aud` exatos por API; `exp`/`nbf` obrigatórios; clock skew ≤ 60 s.
  - `kid` resolvido só no JWKS do issuer configurado; `jku`/`x5u`/`jwk` ignorados.
  - JWKS em cache, com refresh por TTL e em `kid` desconhecido (rate limited); **fail-closed** sem JWKS válido.
- **Autorização:** scopes/roles por endpoint **e** por recurso (subject ↔ conta ↔ tenant) em toda leitura e escrita; 401/403/404 uniformes.
- **Tokens:**
  - Access token de cliente com TTL de 5–10 min (proposto); refresh rotativo com detecção de reuso.
  - Sender-constrained (DPoP ou mTLS-bound) para parceiros, Open Finance e operações de alto risco [avaliar perfil FAPI com Compliance].
- **Serviço-a-serviço:** mTLS com workload identity (SPIFFE). Token exchange (RFC 8693) em vez de repassar o token do usuário a domínios diferentes.
- **Higiene:**
  - Token proibido em query string (400), URL, logs e eventos.
  - Header `Authorization` redigido em todos os loggers e no OTel.
  - NGINX sem `$http_authorization` no log.

## Consequências positivas
Superfície de autenticação padronizada; IdP fora do caminho por transação (p99 de validação ≤ 2 ms); revogação controlada por TTL curto.

## Consequências negativas e trade-offs
Refresh frequente aumenta a carga no IdP (sizing em capacity-model §4); DPoP/mTLS complica clientes; múltiplas audiences exigem governança de registro de clients.

## Segurança, privacidade e conformidade
SEC-REQ-001..008, 019, 027. Se o IdP for SaaS fora do Brasil, aplica-se a ADR-0011 (transferência internacional [VALIDAR]).

## Operação, performance e DR
Teste de rotação de JWKS em game day; alerta de falha de refresh do JWKS; IdP com SLO ≥ T0 para login (NFR-026).

## Plano de migração e rollback
Clientes registrados por ambiente; rotação de chaves com sobreposição.

## Critérios de validação
Suíte negativa Playwright/xUnit: `alg:none`, HS256 com chave pública, `kid` malicioso, `jku` externo, `aud`/`iss` errados, expirado + skew + 1 s, JWKS indisponível → 401; token de cliente em endpoint de operador → 403; matriz BOLA → 404/403 uniforme; `?access_token=` → 400.

## Referências e ADRs relacionados
ADR-0011, 0014. RFC 8725, RFC 9700, RFC 9449, RFC 8705, RFC 8693. `docs/security/threat-model.md`.
