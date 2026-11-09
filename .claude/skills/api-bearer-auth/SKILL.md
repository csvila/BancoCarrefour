---
name: api-bearer-auth
description: "Implementa e revisa autenticação e autorização de APIs com Bearer Token. Use em endpoints, gateway, serviços e integrações."
---

# API Bearer Token

- Usar OAuth 2.0 / OpenID Connect e JWT assinado quando apropriado; não inventar protocolo de token.
- Validar assinatura com algoritmo allowlist, issuer, audience, exp, nbf, key ID e rotação JWKS. Rejeitar `none` e downgrade.
- Validar scopes/roles e autorização por recurso/conta em cada operação; autenticação não implica autorização.
- `Authorization: Bearer` somente via TLS; nunca query string, URL, logs, traces ou mensagens de erro.
- Tokens curtos, refresh seguro conforme fluxo, revogação/denylist quando necessário e proteção contra replay.
- Separar tokens de usuário, serviço e operador; mTLS/workload identity para tráfego interno quando aplicável.
- Não persistir bearer token em localStorage se houver alternativa mais segura; para web avaliar BFF e cookies Secure/HttpOnly/SameSite. Mobile usar secure storage do SO.
- Para POS/ATM considerar device identity, certificados e gestão de chaves.
- Responder 401 para autenticação inválida e 403 para autorização insuficiente; não revelar existência de contas.
- Testar expiração, issuer/audience errados, assinatura inválida, algoritmo proibido, key rotation, scope ausente, IDOR e token replay.
