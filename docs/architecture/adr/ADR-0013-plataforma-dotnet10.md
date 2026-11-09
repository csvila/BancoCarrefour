# ADR-0013 — Plataforma .NET 10 (LTS) / C# 14 e baseline de build

- Status: **Accepted** (aprovada pelo usuário em 2026-11-09) — confirmar política de suporte em fonte oficial (critério de validação)
- Data: 2026-11-09
- Autores: orquestrador (consolidação); insumo de software-architect (SW-008, D7)
- Revisores: usuário (aprovado em 2026-11-09)

## Contexto
O CLAUDE.md exige C# 14 / .NET 10, com a condição de "confirmar o SDK instalado e o suporte de produção antes de fixar o target framework". O orquestrador verificou o **SDK 10.0.403** instalado (`dotnet --list-sdks`, 2026-11-09). Segundo o software-architect, com fontes citadas (blog .NET da Microsoft, InfoWorld, heise), o .NET 10 é LTS com suporte até novembro de 2028, e o .NET 8 e o .NET 9 saem de suporte em 2026-11-10. **A data exata de fim de suporte deve ser confirmada na página oficial de política de suporte da Microsoft antes de Accepted.**

## Problema
Fixar o target framework e um baseline de build reproduzível e seguro.

## Alternativas consideradas
1. **.NET 8/9:** fim de suporte iminente. Rejeitada.
2. **.NET 10 LTS (`net10.0`), C# 14 padrão.** Recomendada.
3. **.NET 11 (STS) quando lançado:** não serve como base de produção bancária.

## Decisão
- `net10.0`, C# 14 (padrão do SDK).
- `global.json` fixando SDK 10.0.4xx com `rollForward: latestPatch`.
- `Nullable` enable; `TreatWarningsAsErrors`; analyzers (.NET + segurança); Central Package Management; lock files + **package source mapping** (TM-023).
- Imagens `mcr.microsoft.com/dotnet/aspnet:10.0-*-chiseled` não-root, pin por digest.
- Testes: xUnit (unidade); Microsoft.Playwright para .NET (integração API/UI) + WebApplicationFactory onde couber; k6 (carga).

## Consequências positivas
Suporte longo; build reproduzível; base para supply chain segura.

## Consequências negativas e trade-offs
Dependências precisam suportar .NET 10; patches mensais exigem pipeline de atualização.

## Segurança, privacidade e conformidade
Package source mapping contra dependency confusion; SBOM e assinatura (topology §6).

## Operação, performance e DR
Server GC; `DOTNET_GCHeapHardLimit` conforme o limite do container; Native AOT a avaliar no Card Authorization.

## Plano de migração e rollback
Greenfield.

## Critérios de validação
`dotnet --info` e build limpo com evidência (comando, resultado, commit); confirmação da política de suporte em fonte oficial.

## Referências e ADRs relacionados
ADR-0009. Fontes citadas pelo software-architect: devblogs.microsoft.com/dotnet (?p=60346), infoworld.com (4191704), heise.de (-11120775).
