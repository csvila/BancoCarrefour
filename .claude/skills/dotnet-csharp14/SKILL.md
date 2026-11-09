---
name: dotnet-csharp14
description: "Define padrões de implementação segura em C# 14 e .NET para APIs bancárias. Use ao criar ou revisar código C#."
---

# C# 14 / .NET

- Confirmar SDK e target framework suportados no ambiente; C# 14 acompanha .NET 10, mas não presumir disponibilidade sem verificar `dotnet --info`.
- Nullable reference types habilitado, analyzers e warnings tratados.
- Preferir async/await, CancellationToken em I/O, HttpClientFactory e timeouts explícitos.
- Evitar `.Result`, `.Wait()`, fire-and-forget não supervisionado e retry cego.
- Usar ProblemDetails, OpenAPI, versionamento e validação de entrada.
- Injeção de dependência por construtor; evitar service locator.
- Separar domínio, aplicação, infraestrutura e API sem criar camadas artificiais.
- Dinheiro: decimal ou tipo Money explícito; moeda e escala explícitas; nunca double/float.
- Datas: UTC + timezone/valor de negócio explícito; não inferir data contábil do servidor.
- Persistência: transações explícitas, isolamento escolhido conscientemente, optimistic concurrency ou locks quando necessário.
- Não expor entidades ORM diretamente em contratos.
- Logs estruturados sem PII/secrets; correlation ID e trace context.
- Configuração validada no startup; segredos via secret provider.
- Não capturar exceções genéricas para retornar sucesso.
