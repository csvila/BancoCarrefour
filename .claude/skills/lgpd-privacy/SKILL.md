---
name: lgpd-privacy
description: "Avalia privacidade e proteção de dados pessoais conforme LGPD no ciclo de vida bancário. Use em arquitetura, eventos, logs, analytics, fornecedores e incidentes."
---

# LGPD e Privacy by Design

- Identificar controlador, operador, encarregado, titulares, finalidades, bases legais e operações de tratamento. Não presumir base legal; encaminhar validação a Jurídico/Encarregado.
- Classificar dados: pessoais, sensíveis nos termos do art. 5º II, financeiros, dados de cartão, autenticação e segredos. Dados financeiros identificáveis são pessoais, mas não são automaticamente dados sensíveis na definição legal.
- Aplicar finalidade, adequação, necessidade, transparência, segurança, prevenção e responsabilização.
- Mapear fluxo e localização de dados, retenção, compartilhamento, suboperadores e transferências internacionais.
- Minimização em logs/eventos; mascaramento/tokenização; controle de acesso; criptografia; segregação por cliente/conta.
- Definir retenção e descarte considerando obrigações regulatórias/legais e defesa de direitos; não apagar sem validação.
- Avaliar RIPD quando necessário; registrar riscos residuais e mitigação.
- Direitos dos titulares, incident response e avaliação de impacto devem ser considerados no desenho.
- Não inserir PII real em prompts, testes, exemplos, fixtures ou logs.
- Agente read-only: findings para Developer/Test Analyst e validação com Jurídico/Encarregado.
