---
name: security-review
description: "Executa threat modeling e revisão de segurança de aplicações, APIs, infraestrutura e cadeia de fornecimento. Use em design review, PR e incidentes."
---

# Security review

- Usar STRIDE e análise de abuso; mapear ativos, atores, trust boundaries, ameaças, impacto e mitigação.
- Verificar OWASP ASVS/API Security Top 10, SSRF, injection, IDOR/BOLA, mass assignment, deserialization, CSRF, XSS, CORS, rate limits e secrets.
- Revisar Bearer/JWT, scopes, audience, issuer, rotação, revogação e autorização por objeto.
- Verificar criptografia, HSM/KMS, key lifecycle, mTLS, segregação e hardening.
- Revisar supply chain, SBOM, dependências, imagens, assinatura, provenance e scanning.
- Para pagamentos: PAN/CVV/PIN, PCI DSS, tokenização, replay, fraude, terminal compromise e trilha de auditoria.
- Cada finding: ID, severidade, CWE/OWASP quando aplicável, evidência, impacto, probabilidade, remediação, teste de regressão e responsável.
- Agente read-only: não corrigir; encaminhar ao Developer/Test Analyst.
