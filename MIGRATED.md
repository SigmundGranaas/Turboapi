# This repo has moved into the turbo monorepo

The .NET API previously hosted here now lives at:

> **https://github.com/SigmundGranaas/turbo** → [`apps/api/`](https://github.com/SigmundGranaas/turbo/tree/main/apps/api)

Operational / deployment files moved out to the monorepo's `infra/` tree:

| Was here | Is now |
| --- | --- |
| `compose.*.yaml`         | `infra/compose/`        |
| `k8s/`                   | `infra/k8s/`            |
| `prometheus.yml`, `loki-config.yml`, `otel-collector.yml`, `promtail-config.yml` | `infra/observability/` |
| `migrations/shared-db-init.sql` | `infra/migrations/`     |
| `performance/`           | `infra/performance/`    |
| `.env.shared`            | `infra/env/.env.shared` |

GitHub Actions for the API were copied across as `api_dotnet.yml`,
`api_image_build.yaml`, and `api_k6_performance.yml`, each with `paths:`
filters scoped to `apps/api/**` (+ `infra/**` where relevant).

The pre-merge state of this repository is tagged `monorepo-merge-pre-flight`.
Open new issues and PRs against the monorepo from now on.
