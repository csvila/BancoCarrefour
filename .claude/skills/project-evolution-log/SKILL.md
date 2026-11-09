---
name: project-evolution-log
description: "Mantém histórico cronológico, rastreável e auditável da evolução do projeto. Use no início e fim de cada tarefa, sessão ou marco."
---

# Documentação da evolução do projeto

Manter `docs/evolution/` com arquivos `YYYY-MM-DD-<slug>.md`, mais `docs/governance/PROJECT-STATUS.md` e `DECISIONS.md`.

Cada registro inclui: objetivo, contexto, escopo, agentes envolvidos, tarefas delegadas, decisões, ADRs, C4 alterado, arquivos impactados, comandos executados, resultados, evidências, findings abertos/fechados, riscos, pendências, próximo passo e commit.

Não reescrever histórico para esconder falhas. Corrigir por nova entrada, mantendo trilha cronológica. Distinguir fato, hipótese, decisão e recomendação. Não incluir segredos, tokens, dados pessoais reais ou dados financeiros de produção.

O orquestrador é responsável por registrar evolução e comunicações. Especialistas retornam conteúdo estruturado; Developer pode atualizar documentação técnica ligada à implementação, mas nunca editar o log JSONL de comunicação.
