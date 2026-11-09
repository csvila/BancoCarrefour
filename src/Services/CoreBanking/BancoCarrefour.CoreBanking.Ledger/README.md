# CoreBanking.Ledger

Módulo do monolito modular Core Banking (ADR-0009). Responsável futuro: ledger de partidas dobradas,
lançamentos imutáveis (correção por reversão/compensação), Outbox transacional e registro de idempotência,
gravados na mesma transação por partição (ADR-0003).

Estado: skeleton (Etapa 2). Sem entidades ou casos de uso. Não depende de cache nem de read models.
