# Protocolo de comunicação entre agentes

## Registro obrigatório
Toda delegação, prompt de tarefa, resposta, finding, decisão, handoff e encerramento deve gerar um evento JSON por linha em `agent-communications.jsonl`. O arquivo é append-only. Não editar nem reordenar linhas antigas.

## Fluxo
1. Orquestrador cria `messageId` e registra `delegation` antes de chamar o agente.
2. Agente devolve resultado estruturado ao orquestrador.
3. Orquestrador registra `response` e, se aplicável, `finding`/`handoff`.
4. Se houver correção, orquestrador registra solicitação ao `developer` ou `test-analyst`, incluindo ID do finding e critérios de aceite.
5. Agente autorizado responde; orquestrador registra resultado, arquivos e evidências.
6. Fechar finding somente após verificação independente.

## Campos mínimos
`schemaVersion`, `timestamp` UTC, `messageId`, `from`, `to`, `type`, `task`, `summary`, `findingIds`, `artifacts`, `status`.

## Privacidade
Não gravar tokens, segredos, PAN/CVV/PIN, CPF real, dados de conta reais, payloads financeiros ou dados pessoais desnecessários. Usar referências sintéticas.

## Responsável
O agente principal/orquestrador é o escrivão central. Subagentes não devem editar este arquivo. Para múltiplas sessões simultâneas, usar lock/append atômico e validar JSONL após cada gravação.
