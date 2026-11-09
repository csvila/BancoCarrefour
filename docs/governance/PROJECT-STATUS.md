# Status do projeto

- Estado: Bootstrap / arquitetura em elaboração.
- Escopo: plataforma bancária resiliente, core híbrido, 1–10 milhões de clientes.
- Stack: C# 14 / .NET 10, Bearer Token, xUnit, Playwright .NET, k6, NGINX, Docker, Kubernetes.
- Agentes: 8 agentes especializados definidos em `.claude/agents/`.
- Regra de escrita: somente `developer` altera produção/infra executável; somente `test-analyst` altera `tests/**`. Demais agentes read-only.
- Próximos passos: validar premissas, aprovar ADRs iniciais, produzir C4 C1–C4, definir bounded contexts e contratos, criar solution skeleton.
- Riscos em aberto: autoridade do ledger/core; escopo de autorização offline; topologia de regiões; requisitos PCI; SLO/RTO/RPO finais; volume real de TPS.
