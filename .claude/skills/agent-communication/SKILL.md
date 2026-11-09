---
name: agent-communication
description: Registra delegações, mensagens, findings, decisões e handoffs dos agentes em trilha JSONL append-only. Use sempre que o orquestrador coordenar agentes.
---

# Comunicação auditável entre agentes

O orquestrador é o único escritor do arquivo `docs/governance/agent-communications.jsonl`. Agentes especialistas retornam suas mensagens ao orquestrador; não editam o log.

Antes de cada delegação, registrar `delegation`. Após cada resposta, registrar `response`. Findings, decisões, pedidos de correção, handoffs e encerramentos também devem ser eventos próprios. Não registrar somente resumo final: registrar a existência de cada troca e um resumo fiel suficiente para reconstruir o fluxo.

Use `python3 scripts/log-agent-message.py --from <agent> --to <agent|team> --type <event> --task <task> --summary <summary>`. Opcionalmente informe `--finding-ids`, `--artifacts` e `--status`. O script acrescenta uma linha JSON com lock de arquivo.

Não incluir segredos, tokens, dados reais de clientes, PAN/CVV/PIN ou payloads financeiros identificáveis. Não reescrever nem truncar o arquivo. Validar JSONL ao final de cada ciclo.
