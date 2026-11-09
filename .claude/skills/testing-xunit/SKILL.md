---
name: testing-xunit
description: "Cria testes unitários xUnit para domínio e aplicação financeira. Use ao implementar regras de negócio, invariantes, idempotência e concorrência."
---

# Testes unitários xUnit

- Projeto de testes separado em `tests/Unit`.
- xUnit, FluentAssertions (se aprovado) e NSubstitute/Moq quando necessário; evitar mockar entidades simples.
- AAA (Arrange, Act, Assert), nomes `Method_State_Expected`.
- Testar partidas dobradas, precisão monetária, estados, idempotência, payload mismatch, limites, concorrência, timeout/UNKNOWN, reversões e autorização.
- Testes determinísticos; relógio injetável, IDs controláveis e sem dependência de rede/tempo real.
- Teoria xUnit para matrizes de regras.
- Não testar detalhes internos frágeis.
- Test Analyst pode alterar somente `tests/**`. Defeito de produção deve virar finding para Developer.
