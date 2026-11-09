---
name: c4-model
description: "Cria e mantém os quatro níveis C4 (Context, Container, Component, Code) para arquitetura bancária. Use ao iniciar ou alterar arquitetura, fronteiras, integrações ou implantação."
---

# C4 Model — quatro níveis obrigatórios

1. **Context (C1):** pessoas, sistemas externos, limites de confiança, objetivos e relações.
2. **Container (C2):** aplicações/serviços, stores, brokers, protocolos, runtime e responsabilidades. Container C4 não significa Docker container.
3. **Component (C3):** componentes internos de cada container relevante, interfaces e dependências.
4. **Code (C4):** classes/interfaces/pacotes relevantes; usar diagramas UML/mermaid quando o nível de detalhe for útil. Não gerar diagramas de cada classe sem valor.

## Entregáveis
- `docs/architecture/c4/01-context.md`
- `docs/architecture/c4/02-containers.md`
- `docs/architecture/c4/03-components.md`
- `docs/architecture/c4/04-code.md`
- `docs/architecture/c4/README.md` com índice, escopo, versão e links.

## Regras
- Usar Mermaid (`C4Context`, `C4Container`, `C4Component`, `classDiagram`) se suportado pelo renderer; alternativamente diagramas C4 PlantUML.
- Mostrar limites de confiança, dados pessoais, canais, API Gateway, IAM/Bearer, autorização, ledger, outbox, broker, read models, CAE, core legado, observabilidade e integrações externas.
- Diferenciar fluxo síncrono de evento assíncrono; indicar protocolo e direção.
- Não chamar Kafka de ledger; não desenhar Redis como fonte oficial de saldo.
- Mostrar autoridade única de escrita, particionamento, fencing e failover.
- Incluir legenda, premissas, decisões e riscos.
- C4 Code é derivado do código real após implementação; antes disso marcar como visão proposta.
- Especialistas arquitetos produzem conteúdo e encaminham ao orquestrador para persistência. Somente Developer pode editar arquivos do projeto, conforme governança.

## Processo
1. Ler ADRs e status do projeto.
2. Mapear atores e sistemas externos.
3. Criar C1 e validar fronteiras.
4. Criar C2 com responsabilidades e tecnologias candidatas.
5. Criar C3 para caminhos críticos: autorização, ledger, extrato, contingência, IAM.
6. Criar C4 com contratos, classes e dependências reais.
7. Fazer revisão cruzada com segurança, DevOps, observabilidade e performance.
8. Atualizar versão/data e registrar mudanças na evolução.
