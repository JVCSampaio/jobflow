# JobFlow

[![CI](https://github.com/JVCSampaio/jobflow/actions/workflows/ci.yml/badge.svg)](https://github.com/JVCSampaio/jobflow/actions/workflows/ci.yml) · [Portfólio](https://github.com/JVCSampaio)

**Backend de jobs** em **C# / .NET 10** (ASP.NET Core Web API): autenticacao JWT, rate limiting, exception handling global, background worker com retry/backoff, EF Core + PostgreSQL e testes de integracao — tudo dentro de **Docker** com **CI** no GitHub Actions.

O serviço mantém o estado dos jobs, executa tarefas em segundo plano e permite novas tentativas após falhas. A documentação apresenta a arquitetura, a API e as instruções de execução.

## Funcionalidades e implementação

| Capacidade | Implementação |
|---|---|
| **Autenticacao JWT** | `AuthController` + `JwtBearer` (HMAC-SHA256) |
| **Rate limiting** | `RateLimitMiddleware` (janela fixa por IP, 60 req/min) |
| **Exception handling** | `ExceptionMiddleware` mapeia excecoes para status HTTP |
| **Retry/backoff** | `JobProcessor` (maquina de estados pura, testavel) |
| **Background worker** | `JobWorker` (`IHostedService`) |
| **EF Core + migrations** | `JobFlowDbContext` + `Migrations/` |
| **Testes de integracao** | `WebApplicationFactory` + InMemory |

## Arquitetura

```
        ┌──────────────────────────────────────┐
        │  Client                              │
        └───────────────┬──────────────────────┘
                        │  HTTP + Bearer JWT
                        ▼
        ┌──────────────────────────────────────┐
        │  ASP.NET Core Web API                │
        │  ExceptionMiddleware → RateLimit     │
        │  → Auth → Controllers                │
        └───────────────┬──────────────────────┘
                        │
          ┌─────────────┴─────────────┐
          ▼                           ▼
   ┌─────────────┐            ┌─────────────────┐
   │  DbContext  │            │  JobWorker      │
   │  (EF Core)  │            │  (IHostedService)│
   └──────┬──────┘            └────────┬────────┘
          │                            │ claim + process
          ▼                            ▼
   ┌─────────────┐            ┌─────────────────┐
   │  PostgreSQL │            │  JobProcessor   │
   │  users, jobs│            │  retry/backoff  │
   └─────────────┘            └─────────────────┘
```

## Como rodar

### Docker (recomendado)

```bash
docker compose up --build
```

Sobe o Postgres, cria as tabelas (migrations no startup) e sobe a API em `http://localhost:8080`.

### Manual (sem Docker)

```bash
# 1. banco (docker ou local)
docker run -d -p 5432:5432 -e POSTGRES_USER=jobflow \
  -e POSTGRES_PASSWORD=jobflow -e POSTGRES_DB=jobflow postgres:16-alpine

# 2. rodar a API
export JWT_SIGNING_KEY="***"
export DATABASE_URL="Host=localhost;Port=5432;Database=jobflow;Username=jobflow;Password=jobflow"
dotnet run --project src/JobFlow.Api
```

## API (exemplos)

| Endpoint | Auth | O que retorna |
|---|---|
| `GET /health` | — | `{"status":"ok"}` |
| `POST /api/auth/register` | — | `201` + JWT token |
| `POST /api/auth/login` | — | `200` + JWT token |
| `POST /api/jobs` | Bearer | `201` + job criado |
| `GET /api/jobs` | Bearer | lista de jobs |
| `GET /api/jobs/{id}` | Bearer | job |
| `POST /api/jobs/{id}/retry` | Bearer | job re-enfileirado |
| `DELETE /api/jobs/{id}` | Bearer | `204` |

Exemplo de fluxo:

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"username":"demo","email":"demo@example.com","password":"***"}' | jq -r .token)

curl -s -X POST http://localhost:8080/api/jobs \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"name":"process-order","payload":"order-42"}'
```

## Testes e CI

```bash
dotnet test JobFlow.slnx        # 19 testes (unit + integracao)
```

- **Unit:** `JobProcessorTests` (retry/backoff), `UserServiceTests` (BCrypt + JWT round-trip).
- **Integracao:** `ApiIntegrationTests` sobe a API real via `WebApplicationFactory` com banco InMemory — register, login, CRUD de jobs, retry, rate limit.

O workflow `.github/workflows/ci.yml` roda build + testes em cada push.

## Decisoes tecnicas (o "por que")

- **`JobProcessor` puro (sem DB).** A maquina de estados (retry/backoff) e separada do worker, entao e testavel sem banco ou network.
- **`WebApplicationFactory` + InMemory nos testes.** Roda a API de verdade (middleware, JWT, controllers) contra um banco InMemory — integracao sem Postgres.
- **Rate limiter singleton (sem `RequestDelegate` ctor).** Invocado via delegate que resolve o singleton do DI — padrao para middlewares que nao seguem o ctor tradicional.
- **Exception middleware global.** Mapeia `DbUpdateException`→409, `KeyNotFoundException`→404, `InvalidOperationException`→409; loga o resto com correlation id.
- **Migrations no startup (gateable).** `RUN_MIGRATIONS_ON_STARTUP` permite desligar em testes; em producao sobe o schema automaticamente.

## Estrutura

```
jobflow/
├── src/JobFlow.Api/
│   ├── Controllers/          # AuthController, JobsController
│   ├── Middleware/           # ExceptionMiddleware, RateLimitMiddleware
│   ├── Services/             # UserService (BCrypt + JWT)
│   ├── Workers/              # JobProcessor (state machine), JobWorker (IHostedService)
│   ├── Data/                 # JobFlowDbContext, Entities, DesignTimeDbContextFactory
│   ├── DTOs/                 # Dtos
│   ├── Migrations/           # InitialCreate
│   └── Program.cs            # JobFlowApi (entry point + DI + pipeline)
├── tests/JobFlow.Tests/
│   ├── JobProcessorTests.cs
│   ├── UserServiceTests.cs
│   ├── ApiIntegrationTests.cs
│   └── TestHost.cs
├── .github/workflows/ci.yml
├── Dockerfile
├── docker-compose.yml
├── JobFlow.slnx
├── LICENSE
└── README.md
```

## Limitacoes e roadmap

**Limitacoes (honestas):**
- Rate limiter em memoria (single-instance); em producao com multi-instancia, usar Redis.
- Worker processa em loop simples; para alto volume, usar fila (RabbitMQ/Azure Service Bus).
- Sem pagination/offset na lista de jobs (apenas `take`).

**Roadmap (proposital):**
- **Fila externa** (RabbitMQ) para desacoplar API e worker.
- **Distributed tracing** (OpenTelemetry) para observabilidade.
- **OpenAPI/Swagger** exposto em `/swagger`.

## Licenca

MIT — veja [LICENSE](LICENSE).
