# Parking Management — Gestão de Estacionamento

API REST em .NET 8 para controle de vagas, entrada/saída de veículos (via webhook) e cálculo de receita por setor — desenvolvida para o desafio técnico da Estapar (`Teste .NET.docx`), com Clean Architecture, DDD e CQRS.

> A documentação técnica aprofundada de cada camada (o "porquê" de cada decisão) está em [`docs/`](docs/) — este README cobre o essencial para rodar, testar e entender o projeto.

---

## Tecnologias

| Item | Definição |
|------|-----------|
| Stack | .NET 8, C# 12, ASP.NET Core Web API, EF Core 8, SQL Server, DbUp |
| Arquitetura | Clean Architecture + DDD + CQRS (MediatR) |
| Testes | xUnit, Bogus, FluentAssertions, NSubstitute, Testcontainers |
| Validação | FluentValidation |
| Persistência | EF Core + SQL Server (migrations aplicadas via DbUp, não `dotnet ef`) |
| Integração externa | Typed HttpClient consumindo o simulador da Estapar (`GET /garage`) |
| Entrada de dados | Webhook (`POST /webhook`) recebendo eventos `ENTRY`, `PARKED`, `EXIT` |

---

## Arquitetura

```
ParkingManagement.Api              ← Controllers, webhook, middlewares, Swagger
ParkingManagement.Application      ← Casos de uso (CQRS via MediatR), validators, Result pattern
ParkingManagement.Domain           ← Regras de negócio (Sector, Spot, ParkingSession, VOs, eventos)
ParkingManagement.Infrastructure   ← EF Core, repositórios, cliente HTTP do simulador
ParkingManagement.Infrastructure.Migrations ← Scripts SQL versionados (DbUp)
```

Cada camada depende apenas da camada mais interna (Domain no centro, sem dependências de nenhum outro projeto). Detalhes de cada uma:

- [`docs/documentacao-tecnica-dominio.md`](docs/documentacao-tecnica-dominio.md)
- [`docs/documentacao-tecnica-application.md`](docs/documentacao-tecnica-application.md)
- [`docs/documentacao-tecnica-infrastructure.md`](docs/documentacao-tecnica-infrastructure.md)
- [`docs/documentacao-tecnica-api.md`](docs/documentacao-tecnica-api.md)
- [`docs/documentacao-tecnica-migrations.md`](docs/documentacao-tecnica-migrations.md)
- [`docs/documentacao-tecnica-testes.md`](docs/documentacao-tecnica-testes.md)

---

## Como executar

### Pré-requisitos

| Ferramenta | Por quê |
|------------|---------|
| .NET SDK 8+ | Compilar e rodar a API e as migrations |
| Docker Desktop | SQL Server (e, para os testes de integração, um container efêmero via Testcontainers) |
| PowerShell (opcional) | Scripts do guia de testes manuais |

### Opção 1 — Docker Compose (mais simples)

```powershell
docker compose up --build
```

Sobe, na ordem correta (`depends_on` + healthchecks), três serviços:
1. `sqlserver` — SQL Server 2022
2. `migrations` — aplica os scripts DbUp e encerra
3. `api` — sobe em `http://localhost:5089`, com Swagger em `http://localhost:5089/swagger`

> `Simulator:BaseUrl` no `docker-compose.yml` aponta para `http://host.docker.internal:3000` — ou seja, espera um simulador (real ou stub) rodando na sua máquina host, fora do container. Se não houver nada nesse endereço, a API sobe normalmente mesmo assim (ver seção "Testando sem o simulador" abaixo).

### Opção 2 — Rodando localmente (sem Docker Compose)

```powershell
# 1. Banco de dados
docker run -d --name parking-sqlserver -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Your_password123" -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest

# 2. Migrations
dotnet run --project src/ParkingManagement.Infrastructure.Migrations -- "Server=localhost,1433;Database=ParkingManagement;User Id=sa;Password=Your_password123;TrustServerCertificate=True;"

# 3. API
dotnet run --project src/ParkingManagement.Api
```

### Configuração do simulador (`Simulator:BaseUrl`)

O enunciado não disponibiliza o simulador real da Estapar neste ambiente. A URL fica em `src/ParkingManagement.Api/appsettings.json` (`Simulator:BaseUrl`, padrão `http://localhost:3000`) ou na variável de ambiente `Simulator__BaseUrl` (usada no `docker-compose.yml`) — nunca hardcoded no código.

Ao iniciar, a API tenta sincronizar automaticamente a garagem via `GET /garage` (`GarageSyncStartupService` — implementa a RN-1 do enunciado). Se não houver nada respondendo nesse endereço, a falha é logada como aviso e **a API sobe normalmente mesmo assim** — `/webhook` e `/revenue` só funcionam depois que a garagem for sincronizada (automaticamente, ou por um dos meios abaixo).

### Testando sem o simulador

Sem o simulador real disponível, há duas formas de popular a garagem manualmente:

| Endpoint | Depende do simulador? | Uso |
|----------|------------------------|-----|
| `POST /garage/sync` | Sim — chama `GET /garage` de novo | Repetir a sincronização real sem reiniciar a API |
| `POST /garage/seed` | **Não** — recebe a configuração direto no corpo | Testar rapidamente sem manter um stub externo rodando |

Guia completo, passo a passo, com os dois caminhos e o roteiro de teste manual (Swagger/`Invoke-RestMethod`) validando cada regra de negócio com números reais: **[`docs/guia-testes-manuais.md`](docs/guia-testes-manuais.md)**.

---

## Como rodar os testes

```powershell
dotnet test
```

| Suíte | Testes | Requer Docker? |
|-------|--------|-----------------|
| `ParkingManagement.Domain.Tests` | 56 | Não |
| `ParkingManagement.Application.Tests` | 38 | Não |
| `ParkingManagement.Integration.Tests` | 2 | Sim (Testcontainers sobe um SQL Server efêmero) |
| **Total** | **96** | |

Detalhes da estratégia de testes (pirâmide, ferramentas, um bug real de persistência que os testes de integração revelaram durante o desenvolvimento): [`docs/documentacao-tecnica-testes.md`](docs/documentacao-tecnica-testes.md).

---

## Endpoints da API

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/webhook` | Recebe eventos `ENTRY`, `PARKED`, `EXIT` do simulador (discriminados por `event_type`) |
| `GET` | `/revenue?sector=A&date=2025-01-01` | Receita total de um setor numa data (query string — ver nota abaixo) |
| `POST` | `/garage/sync` | Repete a sincronização com o simulador externo (`GET /garage`) |
| `POST` | `/garage/seed` | Configura setores/vagas direto no corpo da requisição, sem depender do simulador |

> **Nota sobre `GET /revenue`:** o enunciado ilustra esse endpoint com um corpo JSON de request, o que é atípico para `GET`. Implementamos os mesmos campos como query string, por aderência às convenções REST — decisão documentada em `docs/documentacao-tecnica-api.md`.

Swagger/OpenAPI disponível em `/swagger` (ambiente `Development`).

---

## Estrutura da Solution

```
ParkingManagement.slnx
├── src/
│   ├── ParkingManagement.Domain/
│   ├── ParkingManagement.Application/
│   ├── ParkingManagement.Infrastructure/
│   ├── ParkingManagement.Infrastructure.Migrations/
│   └── ParkingManagement.Api/
└── tests/
    ├── ParkingManagement.Domain.Tests/
    ├── ParkingManagement.Application.Tests/
    └── ParkingManagement.Integration.Tests/
```

---

## Histórico de entregas

- Setup da solution (5 projetos `src/` + 3 `tests/`), `Directory.Build.props` com `Nullable`/`TreatWarningsAsErrors`.
- Domínio rico: `Sector`, `Spot`, `ParkingSession` (aggregate root), VOs (`LicensePlate`, `GeoCoordinate`, `PricingSnapshot`), eventos de domínio, regras RN-3 a RN-10.
- Migrations DbUp: schema `parking`, tabelas `Sectors`/`Spots`/`ParkingSessions`, índices.
- Infrastructure: `ParkingDbContext` (também `IUnitOfWork`), repositórios, `GarageSimulatorClient` (HttpClient tipado + resiliência).
- Application: MediatR + `ValidationBehavior`/`LoggingBehavior`, Result pattern, casos de uso `SyncGarage`, `RegisterEntry`, `RegisterParked`, `RegisterExit`, `GetRevenue`.
- API: `WebhookController`, `RevenueController`, `GarageSyncStartupService` (sincronização automática no boot — RN-1), `ExceptionHandlingMiddleware`, Swagger.
- Testes: 56 unitários de domínio, 38 de Application (handlers/validators com mocks), 2 de integração end-to-end (API + SQL Server real via Testcontainers).
- `GarageController.Seed` (`POST /garage/seed`) + `GarageUpsert` (upsert compartilhado com `SyncGarage`) — endpoint auxiliar para testar o fluxo completo sem depender do simulador externo, com testes de handler e validator dedicados.
- Docker Compose (SQL Server + migrations + API) e guia de testes manuais completo.

---

## Premissas adotadas

O enunciado (`Teste .NET.docx`) tem pontos que exigiram interpretação:

| Ponto | Premissa adotada | Justificativa |
|-------|-------------------|----------------|
| Lotação para preço dinâmico | Calculada **globalmente** (garagem toda) no momento do `ENTRY` | O setor só é conhecido depois, no evento `PARKED` |
| Bloqueio de entrada a 100% | Capacidade **total** da garagem (soma de todos os setores) | Consistente com "único grupo de cancelas" na entrada |
| Correlação de eventos | No máximo **uma sessão ativa por placa** por vez | Os payloads só trazem `license_plate`, não um `session_id` |
| Vaga do evento `PARKED` | Match **exato** de coordenadas (`lat`/`lng`) contra as vagas sincronizadas | O simulador reporta coordenadas idênticas às cadastradas |
| `GET /revenue` | Implementado com query string, não corpo JSON | Aderência às convenções REST/HTTP |
| Base URL do simulador | Configurável via `Simulator:BaseUrl`, nunca hardcoded | Simulador real não foi disponibilizado neste ambiente |
| Banco de dados | Mantido SQL Server (sugestão do enunciado) | Sem motivo para desviar |

Detalhamento completo: [`PLAN.md`](PLAN.md), seção 12.

---

## Decisões técnicas (resumo)

- **DbUp em vez de EF Core Migrations** — controle total do SQL, revisável, independente do ORM.
- **Result pattern para falhas esperadas** (ex: sessão não encontrada) + **`DomainException`** para regras de negócio violadas (ex: garagem cheia) — separação que evita o custo de exceções para cenários previsíveis num sistema orientado a webhook.
- **`IUnitOfWork` explícito** — corrigiu um bug real de persistência descoberto pelos testes de integração (handlers mutavam entidades no *Change Tracker* mas nunca chamavam `SaveChangesAsync`).
- **`PricingSnapshot` travado na entrada** — garante que o multiplicador de preço usado na cobrança é sempre o da entrada, nunca o da saída, mesmo que a lotação mude entretanto.
- **Falha na sincronização com o simulador não derruba a API** — sem o simulador real disponível no ambiente do desafio, um `fail-fast` deixaria a aplicação inacessível até para inspeção via Swagger.

Cada camada tem seu próprio documento com o raciocínio completo por trás de cada escolha (links na seção "Arquitetura").

---

## Melhorias futuras

- Publicar os `DomainEvents` já capturados pelo `AggregateRoot` após `SaveChangesAsync` (hoje eles são levantados mas não despachados a nenhum handler).
- Match de coordenadas por distância mínima (haversine) em vez de igualdade exata, para tolerar imprecisão de GPS de um simulador real.
- Apontar `Simulator:BaseUrl` para o simulador real da Estapar assim que disponibilizado, e remover o endpoint `/garage/seed` (ou mantê-lo só como utilitário de teste, claramente sinalizado como não parte do contrato oficial).
