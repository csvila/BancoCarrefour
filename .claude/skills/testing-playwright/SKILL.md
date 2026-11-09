---
name: testing-playwright
description: "Cria testes de integração e ponta a ponta usando Microsoft Playwright para .NET. Use para validar APIs e jornadas Web/Mobile web."
---

# Integração com Playwright .NET

- Usar `Microsoft.Playwright` em projetos `tests/Integration`.
- Para API, usar `APIRequestContext`; para UI, browser contexts isolados.
- Subir aplicação real em ambiente de teste; opcionalmente usar `WebApplicationFactory` para host de teste quando isso não substituir a verificação de integração.
- Testar Bearer token: válido, expirado, assinatura inválida, audience/issuer, scopes, autorização por conta e ausência de token.
- Fluxos financeiros: request, resposta, retry com mesma idempotency key, timeout após commit, consulta de status, reversão e extrato.
- Usar dados sintéticos, contas isoladas e cleanup idempotente. Nunca dados reais.
- Capturar trace, screenshot e logs sem credenciais.
- Testes devem validar status HTTP, contrato, persistência e efeitos downstream; evitar dependência de sleeps fixos.
- Test Analyst altera somente `tests/**`.
