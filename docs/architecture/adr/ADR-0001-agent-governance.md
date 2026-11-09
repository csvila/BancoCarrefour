# ADR-0001 — Governança do time multiagente

- Status: Accepted
- Data: 2026-11-02
- Contexto: prevenir alterações concorrentes e preservar segregação de funções.
- Decisão: oito subagentes; somente `developer` altera produção/infra executável; somente `test-analyst` altera testes; seis especialistas read-only. O orquestrador registra todas as comunicações em JSONL append-only.
- Consequências: maior rastreabilidade e menor risco de alterações não autorizadas; orquestrador torna-se responsável pelo registro central.
- Validação: revisar permissões e hooks dos agentes; testar tentativas de escrita fora do escopo.
