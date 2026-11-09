---
name: platform-nginx-docker-kubernetes
description: "Projeta e revisa NGINX, Docker e Kubernetes para workloads bancários resilientes. Use em ingress, deploy, hardening, HA e DR."
---

# NGINX, Docker e Kubernetes

## NGINX
- TLS moderno, headers de segurança, upstreams, health checks apropriados, timeouts, keepalive, rate limiting e limites de corpo.
- Não retry automático de POST financeiro sem idempotency key e política explícita.
- Preservar `Authorization` de forma segura; nunca logar bearer token.
- Separar readiness de liveness; evitar cascata de reinícios.

## Docker
- Multi-stage builds, imagens mínimas e não-root, filesystem read-only quando viável, drop capabilities, pinning por digest, SBOM e scan.
- Sem segredos em ARG/ENV/layers; usar secret mounts/runtime secret manager.

## Kubernetes
- Requests/limits, HPA com métricas adequadas, PDB, topologySpreadConstraints, anti-affinity, probes e graceful shutdown.
- NetworkPolicies, RBAC mínimo, Pod Security Standards, service account dedicada e workload identity.
- Não usar readiness para mascarar dependência degradada sem estratégia.
- Stateful data plane exige desenho específico; não presumir que Kubernetes substitui HA de banco.

## DR
- Separar cluster/zona/região, DNS/traffic manager, fencing, runbooks, backup imutável e restore testado.
- Validar RPO/RTO medidos.
- Agente read-only: findings para Developer; não editar manifests, Dockerfiles ou NGINX.
