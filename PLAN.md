# PLAN.md — Desafio Técnico: Sistema de Gestão de Estacionamento (Estapar)

> API REST em .NET 8 para controle de vagas, entrada/saída de veículos (via webhook) e cálculo de receita por setor, com Clean Architecture, DDD e CQRS — arquitetura e organização de projeto replicadas do desafio `gestao-faturas`, regras de negócio definidas em `Teste .NET.docx`.

---

## 1. Visão Geral

| Item | Definição |
|------|-----------|
| **Stack** | .NET 8, C# 12, ASP.NET Core Web API, EF Core 8, SQL Server, DbUp |
| **Arquitetura** | Clean Architecture + DDD + CQRS |
| **Testes** | xUnit, Bogus, FluentAssertions, NSubstitute, padrão AAA |
| **Validação** | FluentValidation |
| **DTOs** | `record` (imutáveis) |
| **Persistência** | EF Core + SQL Server |
| **Migrations** | DbUp com scripts SQL versionados |
| **Integração externa** | Typed HttpClient consumindo o simulador da Estapar (`GET /garage`) |
| **Entrada de dados** | Webhook (`POST /webhook`) recebendo eventos `ENTRY`, `PARKED`, `EXIT` |

---

## 2. Estrutura da Solution

```
ParkingManagement.slnx
│
├── src/
│   ├── ParkingManagement.Domain/                   # Domínio rico (Aggregates, VOs, Events)
│   ├── ParkingManagement.Application/              # Casos de uso (CQRS, Features)
│   ├── ParkingManagement.Infrastructure/           # EF Core, Repositórios, DbContext, Client do Simulador
│   ├── ParkingManagement.Infrastructure.Migrations/# DbUp + Scripts SQL
│   └── ParkingManagement.Api/                      # API REST (Controllers, Webhook, Middlewares)
│
└── tests/
    ├── ParkingManagement.Domain.Tests/             # Testes de unidade do domínio
    ├── ParkingManagement.Application.Tests/        # Testes de Handlers/Validators
    └── ParkingManagement.Integration.Tests/        # Testes de integração API + DB
```

`Directory.Build.props` (idêntico ao `gestao-faturas`):

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>NU1900</NoWarn>
    <ImplicitUsings>enable</ImplicitUsings>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

---

## 3. Camada de Domínio (`ParkingManagement.Domain`)

### 3.1 Princípios

Domínio **rico**, com regras de negócio dentro das próprias entidades. Propriedades com `set` privado; mutação apenas via métodos do agregado. Mesma base (`Entity`, `AggregateRoot`, `ValueObject`, `DomainException`) do projeto de referência.

### 3.2 Estrutura de pastas

```
ParkingManagement.Domain/
├── Common/
│   ├── Entity.cs
│   ├── AggregateRoot.cs
│   ├── ValueObject.cs
│   ├── DomainException.cs
│   └── IDomainEvent.cs
│
├── Garage/
│   ├── Sector.cs                          # Entity — configuração sincronizada do simulador
│   ├── Spot.cs                            # Entity — vaga física, com Status
│   ├── SpotStatus.cs                      # Enum (Livre, Ocupada)
│   ├── Repositories/
│   │   ├── ISectorRepository.cs
│   │   └── ISpotRepository.cs
│   └── Errors/
│       └── GarageErrors.cs
│
├── Parking/
│   ├── ParkingSession.cs                  # Aggregate Root — ciclo de vida do veículo na garagem
│   ├── ParkingSessionStatus.cs            # Enum (Entrou, Estacionado, Finalizado)
│   ├── ValueObjects/
│   │   ├── LicensePlate.cs                # VO com validação de formato
│   │   ├── GeoCoordinate.cs               # VO (lat, lng)
│   │   └── PricingSnapshot.cs             # VO — basePrice + multiplicador aplicados na entrada
│   ├── Events/
│   │   ├── VehicleEnteredEvent.cs
│   │   ├── VehicleParkedEvent.cs
│   │   └── VehicleExitedEvent.cs
│   ├── Repositories/
│   │   └── IParkingSessionRepository.cs
│   └── Errors/
│       └── ParkingSessionErrors.cs
```

### 3.3 Modelagem das Entidades

#### `Sector` (Entity — configuração, sincronizada de `GET /garage`)

| Propriedade | Tipo | Observação |
|-------------|------|------------|
| `Id` | `Guid` | Identidade interna |
| `Code` | `string` | Código do setor vindo do simulador (`"A"`, `"B"`...) — chave de negócio |
| `BasePrice` | `decimal` | Tarifa base por hora do setor |
| `MaxCapacity` | `int` | Capacidade máxima de vagas do setor |

#### `Spot` (Entity — vaga física)

| Propriedade | Tipo | Observação |
|-------------|------|------------|
| `Id` | `Guid` | Identidade interna |
| `ExternalId` | `long` | `id` retornado pelo simulador |
| `SectorCode` | `string` | Setor ao qual a vaga pertence |
| `Coordinate` | `GeoCoordinate` (VO) | `lat`/`lng` — usada para correlacionar o evento `PARKED` |
| `Status` | `SpotStatus` | `Livre` (padrão) / `Ocupada` |

Comportamentos: `Ocupar()`, `Liberar()` — guards simples (não permite ocupar vaga já ocupada, nem liberar vaga já livre).

#### `ParkingSession` (Aggregate Root)

| Propriedade | Tipo | Observação |
|-------------|------|------------|
| `Id` | `Guid` | Identidade |
| `LicensePlate` | `LicensePlate` (VO) | Placa do veículo |
| `EntryTime` | `DateTime` | UTC, recebido no evento `ENTRY` |
| `PricingSnapshot` | `PricingSnapshot` (VO) | Multiplicador de lotação **travado no momento da entrada** (RN-4) |
| `ParkedAt` | `DateTime?` | Preenchido no evento `PARKED` |
| `ParkedCoordinate` | `GeoCoordinate?` (VO) | `lat`/`lng` recebidos no evento `PARKED` |
| `SpotId` | `Guid?` | Vaga correlacionada via coordenada (RN-5) |
| `SectorCode` | `string?` | Setor da vaga correlacionada — define o `BasePrice` aplicável |
| `ExitTime` | `DateTime?` | Preenchido no evento `EXIT` |
| `AmountCharged` | `decimal?` | Calculado na saída (RN-6) |
| `Status` | `ParkingSessionStatus` | `Entrou` → `Estacionado` → `Finalizado` |

Comportamentos (métodos públicos do agregado):

- `static IniciarEntrada(placa, entryTime, occupancyPercentage)` — fábrica. Bloqueia se `occupancyPercentage >= 100%` (RN-3/RN-8) e calcula/trava o `PricingSnapshot` (multiplicador) conforme RN-4.
- `RegistrarEstacionamento(spotId, sectorCode, coordinate, basePrice, parkedAt)` — transição `Entrou → Estacionado`; bloqueia se não estiver em `Entrou`.
- `RegistrarSaida(exitTime)` — transição `Estacionado → Finalizado`; calcula `AmountCharged` (RN-6); bloqueia se não estiver em `Estacionado`.
- `CalcularValorCobrado(exitTime)` — privado: minutos decorridos, 30 min grátis, arredondamento de hora cheia para cima, `BasePrice × Multiplicador × horasCobradas`.

> **Decisão de design:** a `ParkingSession` recebe `occupancyPercentage` e `basePrice` já calculados/consultados pela camada de Application (via repositório), mantendo o agregado puro e testável sem dependências de infraestrutura — mesmo padrão usado em `Fatura` no projeto de referência.

### 3.4 Mapeamento Regras de Negócio → Domínio

| RN | Regra | Onde é implementada |
|----|-------|---------------------|
| 1 | Sincronizar setores/vagas via `GET /garage` ao iniciar a aplicação | `Application` (comando de sync) + `Infrastructure` (client do simulador) |
| 2 | Webhook aceita `ENTRY`, `PARKED`, `EXIT` | `Api` (endpoint) → `Application` (3 commands) |
| 3 | Bloquear entrada se garagem estiver 100% cheia | `ParkingSession.IniciarEntrada` (guard) |
| 4 | Preço dinâmico na entrada (<25% -10%, ≤50% normal, ≤75% +10%, ≤100% +25%) | `ParkingSession.IniciarEntrada` → `PricingSnapshot` |
| 5 | `PARKED` associa a vaga (por coordenada) e o setor à sessão | `ParkingSession.RegistrarEstacionamento` |
| 6 | `EXIT` libera a vaga e calcula o valor (30 min grátis + hora cheia arredondada) | `ParkingSession.RegistrarSaida` / `CalcularValorCobrado` |
| 7 | Consulta de receita total por setor e data | `Application` (Query `GetRevenue`) |
| 8 | Reforço de bloqueio a 100% de lotação até liberar vaga | Idem RN-3, revalidado a cada `ENTRY` |
| 9 | *(premissa)* Lotação considerada em nível global da garagem | Ver seção 12 — premissa documentada |
| 10 | *(premissa)* Correlação de eventos por placa assume uma sessão ativa por placa | `IParkingSessionRepository.GetActiveByLicensePlateAsync` |

---

## 4. Camada de Application (`ParkingManagement.Application`)

### 4.1 Estrutura de pastas

```
ParkingManagement.Application/
├── Common/
│   ├── Behaviors/
│   │   ├── ValidationBehavior.cs
│   │   └── LoggingBehavior.cs
│   ├── Mediator/
│   │   ├── ICommand.cs
│   │   ├── IQuery.cs
│   │   ├── ICommandHandler.cs
│   │   └── IQueryHandler.cs
│   ├── Results/
│   │   ├── Result.cs
│   │   └── Error.cs
│   ├── Integrations/
│   │   └── IGarageSimulatorClient.cs     # Contrato — implementado em Infrastructure
│   └── Errors/
│       └── ApplicationErrorMessages.cs   # ★ Arquivo central de mensagens
│
├── Features/
│   ├── Garage/
│   │   └── Commands/
│   │       └── SyncGarage/
│   │           ├── SyncGarageResponse.cs
│   │           ├── SyncGarageHandler.cs  # Chama IGarageSimulatorClient, faz upsert de Sectors/Spots
│   │           └── SyncGarageValidator.cs
│   │
│   ├── Parking/
│   │   ├── Commands/
│   │   │   ├── RegisterEntry/
│   │   │   │   ├── RegisterEntryRequest.cs    # record — mapeia o evento ENTRY
│   │   │   │   ├── RegisterEntryResponse.cs
│   │   │   │   ├── RegisterEntryHandler.cs
│   │   │   │   └── RegisterEntryValidator.cs
│   │   │   ├── RegisterParked/
│   │   │   │   ├── RegisterParkedRequest.cs   # record — mapeia o evento PARKED
│   │   │   │   ├── RegisterParkedResponse.cs
│   │   │   │   ├── RegisterParkedHandler.cs
│   │   │   │   └── RegisterParkedValidator.cs
│   │   │   └── RegisterExit/
│   │   │       ├── RegisterExitRequest.cs     # record — mapeia o evento EXIT
│   │   │       ├── RegisterExitResponse.cs
│   │   │       ├── RegisterExitHandler.cs
│   │   │       └── RegisterExitValidator.cs
│   │   └── Queries/
│   │       └── GetRevenue/
│   │           ├── GetRevenueRequest.cs       # filtros: sector, date
│   │           ├── GetRevenueResponse.cs
│   │           ├── GetRevenueHandler.cs
│   │           └── GetRevenueValidator.cs
│
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs    # ★ AddApplication()
```

### 4.2 DTOs com `record`

```csharp
public sealed record RegisterEntryRequest(
    string LicensePlate,
    DateTime EntryTime
) : ICommand<Result<RegisterEntryResponse>>;

public sealed record RegisterEntryResponse(
    Guid SessionId,
    string LicensePlate,
    DateTime EntryTime,
    decimal PriceMultiplierApplied
);
```

```csharp
public sealed record GetRevenueRequest(
    string Sector,
    DateOnly Date
) : IQuery<Result<GetRevenueResponse>>;

public sealed record GetRevenueResponse(
    decimal Amount,
    string Currency,
    DateTime Timestamp
);
```

### 4.3 Arquivo central de mensagens de erro

`Common/Errors/ApplicationErrorMessages.cs` — mesmo padrão do projeto de referência:

```csharp
public static class ApplicationErrorMessages
{
    public static class Parking
    {
        public const string LicensePlateObrigatoria   = "A placa do veículo é obrigatória.";
        public const string SessaoAtivaNaoEncontrada  = "Nenhuma sessão ativa encontrada para esta placa.";
        public const string SessaoJaEstacionada       = "Esta sessão já foi registrada como estacionada.";
        public const string SessaoJaFinalizada        = "Esta sessão já foi finalizada.";
        public const string GaragemCheia               = "Não é possível registrar entrada: garagem no limite de capacidade.";
        public const string VagaNaoEncontradaPorCoordenada = "Nenhuma vaga corresponde às coordenadas informadas.";
    }

    public static class Garage
    {
        public const string SetorNaoEncontrado = "Setor não encontrado.";
        public const string FalhaSincronizacao = "Falha ao sincronizar a configuração da garagem com o simulador.";
    }

    public static class Revenue
    {
        public const string SetorObrigatorio = "O setor é obrigatório.";
        public const string DataObrigatoria  = "A data é obrigatória.";
    }
}
```

### 4.4 Validators (FluentValidation)

Cada caso de uso tem um validator dedicado, referenciando as constantes do arquivo central — mesmo padrão do `CreateFaturaValidator`.

### 4.5 ServiceCollectionExtensions modular

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly));
        services.AddValidatorsFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        return services;
    }
}
```

---

## 5. Camada de Infrastructure (`ParkingManagement.Infrastructure`)

### 5.1 Estrutura

```
ParkingManagement.Infrastructure/
├── Persistence/
│   ├── ParkingDbContext.cs
│   ├── Configurations/
│   │   ├── SectorConfiguration.cs
│   │   ├── SpotConfiguration.cs
│   │   └── ParkingSessionConfiguration.cs
│   └── Interceptors/
│       └── AuditableInterceptor.cs       # opcional
│
├── Repositories/
│   ├── SectorRepository.cs
│   ├── SpotRepository.cs                 # inclui FindByCoordinateAsync (RN-5)
│   └── ParkingSessionRepository.cs       # inclui GetActiveByLicensePlateAsync (RN-10)
│
├── ExternalServices/
│   └── GarageSimulatorClient.cs          # Typed HttpClient — GET /garage do simulador
│
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs    # AddInfrastructure(IConfiguration)
```

### 5.2 Pacotes NuGet

| Pacote | Versão | Por quê |
|--------|--------|---------|
| `Microsoft.EntityFrameworkCore` | 8.0.x | EF Core compatível com .NET 8 |
| `Microsoft.EntityFrameworkCore.SqlServer` | 8.0.x | Provider SQL Server |
| `Microsoft.Extensions.Http.Resilience` | 8.x | Retry/backoff nas chamadas ao simulador |

### 5.3 DbContext

`ParkingDbContext` mapeia `Sector`, `Spot` e `ParkingSession`:

- `GeoCoordinate` mapeado via `OwnsOne` (VO).
- `PricingSnapshot` mapeado via `OwnsOne` (VO).
- Índice único em `Sector.Code` e `Spot.ExternalId`.
- Índice composto em `(SectorCode, ExitTime)` para acelerar a query de receita (RN-7).
- Índice em `(LicensePlate, Status)` para localizar rapidamente a sessão ativa (RN-10).
- Snake_case ou PascalCase nas tabelas — a definir no primeiro script de migration (documentar a convenção escolhida).

### 5.4 Repositórios

- `ISpotRepository.FindByCoordinateAsync(lat, lng)` — resolve a vaga correspondente ao evento `PARKED` (match exato pelas coordenadas sincronizadas de `GET /garage`).
- `IParkingSessionRepository.GetActiveByLicensePlateAsync(placa)` — retorna a sessão em aberto (`Entrou` ou `Estacionado`) para correlacionar os eventos `PARKED`/`EXIT`.
- `IParkingSessionRepository.CountActiveBySectorAsync` / `CountActiveTotalAsync` — usados para calcular a lotação (global e por setor) no momento do `ENTRY`.

### 5.5 `GarageSimulatorClient`

Typed HttpClient que consome `GET /garage` do simulador (base URL configurável em `appsettings` via `Simulator:BaseUrl`), com política de retry/backoff (o simulador pode subir depois da API no `docker-compose`).

### 5.6 ServiceCollectionExtensions

```csharp
public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
{
    services.AddDbContext<ParkingDbContext>(opt =>
        opt.UseSqlServer(cfg.GetConnectionString("SqlServer")));

    services.AddScoped<ISectorRepository, SectorRepository>();
    services.AddScoped<ISpotRepository, SpotRepository>();
    services.AddScoped<IParkingSessionRepository, ParkingSessionRepository>();

    services.AddHttpClient<IGarageSimulatorClient, GarageSimulatorClient>(client =>
        {
            client.BaseAddress = new Uri(cfg["Simulator:BaseUrl"]!);
        })
        .AddStandardResilienceHandler();

    return services;
}
```

---

## 6. Camada de Migrations (`ParkingManagement.Infrastructure.Migrations`)

> Projeto **separado** do EF Core, seguindo o mesmo padrão do `Faturas.Infrastructure.Migrations`: o EF Core **não** gera migrations — o DbUp aplica scripts SQL versionados.

### 6.1 Estrutura

```
ParkingManagement.Infrastructure.Migrations/
├── Scripts/
│   ├── 0001_create_schema.sql
│   ├── 0002_create_table_sectors.sql
│   ├── 0003_create_table_spots.sql
│   ├── 0004_create_table_parking_sessions.sql
│   └── 0005_indexes.sql
├── MigrationRunner.cs                    # Programa console que roda DbUp
├── ParkingManagement.Infrastructure.Migrations.csproj
└── README.md
```

### 6.2 Pacote NuGet

- `dbup-sqlserver` — provider DbUp para SQL Server.

### 6.3 Convenções de scripts

Idênticas ao projeto de referência: numeração sequencial, `Embedded Resource`, idempotência (`IF NOT EXISTS`), sem rollback automático.

### 6.4 Execução

- Local: `dotnet run --project src/ParkingManagement.Infrastructure.Migrations -- "Server=localhost;..."`.
- Docker: roda como serviço `migrations` no `docker-compose`, com `depends_on: sqlserver (service_healthy)` e a API dependendo de `migrations (service_completed_successfully)`.

---

## 7. Integração com o Simulador da Estapar

Esta é a camada equivalente, em papel arquitetural, ao `Faturas.Web` do projeto de referência: não é uma interface de usuário, mas o **outro lado da integração** — o componente que fala com o mundo externo.

### 7.1 Sincronização inicial (`GET /garage`)

- Ao iniciar a aplicação, um `IHostedService` (`GarageSyncStartupService`) dispara o comando `SyncGarage`, que:
  1. Chama `IGarageSimulatorClient.GetGarageConfigurationAsync()`.
  2. Faz *upsert* de `Sector` (por `Code`) e `Spot` (por `ExternalId`) no banco.
  3. Loga o resultado (quantidade de setores/vagas sincronizados).
- Falha na sincronização inicial **não** derruba a API — é logada como `Warning` e a aplicação sobe normalmente. Essa política foi revisada em relação à versão inicial (que fazia fail-fast) porque o enunciado não disponibiliza o simulador real: sem essa mudança, rodar a API sem o simulador de pé a derrubava antes até do Swagger abrir. `/webhook` e `/revenue` retornam erro de "setor não encontrado" até a sincronização ser refeita.
- `POST /garage/sync` (ver seção 8) permite repetir a sincronização manualmente a qualquer momento, sem reiniciar a API — útil para apontar um stub local depois que a API já está de pé.

### 7.2 Recebimento de eventos (`POST /webhook`)

O simulador envia eventos para o endpoint `POST /webhook` da nossa API. Um único endpoint despacha para 3 comandos diferentes com base no campo `event_type`:

| `event_type` | Command despachado |
|--------------|---------------------|
| `ENTRY` | `RegisterEntryCommand` |
| `PARKED` | `RegisterParkedCommand` |
| `EXIT` | `RegisterExitCommand` |

---

## 8. Camada de API (`ParkingManagement.Api`)

### 8.1 Estrutura

```
ParkingManagement.Api/
├── Controllers/
│   ├── WebhookController.cs              # POST /webhook
│   ├── RevenueController.cs              # GET /revenue
│   └── GarageController.cs               # POST /garage/sync — reexecuta a sincronização manualmente
├── Contracts/
│   └── Webhook/
│       ├── VehicleEventEnvelope.cs       # DTO de entrada com discriminação por event_type
│       ├── EntryEventDto.cs
│       ├── ParkedEventDto.cs
│       └── ExitEventDto.cs
├── HostedServices/
│   └── GarageSyncStartupService.cs       # dispara SyncGarage no startup
├── Middlewares/
│   └── ExceptionHandlingMiddleware.cs
├── Program.cs
└── appsettings.json
```

### 8.2 Endpoints (exigidos pelo desafio)

| Método | Rota | Caso de Uso |
|--------|------|-------------|
| `POST` | `/webhook` | Despacha `RegisterEntry` / `RegisterParked` / `RegisterExit` conforme `event_type` |
| `GET`  | `/revenue` | `GetRevenue` — filtros `date` e `sector` |
| `POST` | `/garage/sync` | `SyncGarage` — reexecuta a sincronização com o simulador sem precisar reiniciar a API (ver seção 12) |

> **Decisão de design:** o enunciado ilustra `GET /revenue` com um corpo JSON de *request* (`{ "date": ..., "sector": ... }`), o que é atípico para o verbo `GET`. A implementação expõe os mesmos campos como **query string** (`GET /revenue?date=2025-01-01&sector=A`), por aderência às convenções REST/HTTP. Documentar essa escolha no `README.md` e, se o avaliador enviar de fato um corpo no `GET`, avaliar suporte adicional via model binding customizado.

### 8.3 Cross-cutting

- Swagger/OpenAPI ligado.
- `ExceptionHandlingMiddleware` retornando `ProblemDetails` (RFC 7807).
- Validação automática via `ValidationBehavior` (MediatR pipeline).
- Logging estruturado (Serilog), sem dados sensíveis (placas podem ser mascaradas parcialmente em log, se necessário).

---

## 9. Camada de Testes

### 9.1 Pacotes comuns

| Pacote | Uso |
|--------|-----|
| `xunit` | Framework de testes |
| `xunit.runner.visualstudio` | Runner |
| `FluentAssertions` | Assertions legíveis |
| `Bogus` | Geração de dados fake |
| `NSubstitute` | Mocks em `Application.Tests` |
| `Microsoft.NET.Test.Sdk` | SDK |

### 9.2 `ParkingManagement.Domain.Tests`

Testes de unidade puros do domínio (sem mocks, sem infra).

```
ParkingManagement.Domain.Tests/
├── Parking/
│   ├── ParkingSessionTests.cs
│   └── Builders/
│       └── ParkingSessionFaker.cs
├── Garage/
│   ├── SpotTests.cs
```

Cobertura mínima exigida:

| Cenário | Teste |
|---------|-------|
| Entrada válida com garagem abaixo de 25% de lotação | `IniciarEntrada_DeveAplicarDesconto10Porcento_QuandoLotacaoAbaixoDe25` |
| Entrada com lotação entre 50% e 75% | `IniciarEntrada_DeveAplicarAcrescimo10Porcento_QuandoLotacaoAte75` |
| Entrada com lotação entre 75% e 100% | `IniciarEntrada_DeveAplicarAcrescimo25Porcento_QuandoLotacaoAte100` |
| Bloqueio de entrada com garagem cheia | `IniciarEntrada_DeveLancar_QuandoLotacao100Porcento` |
| Estacionar veículo | `RegistrarEstacionamento_DeveAssociarVagaESetor` |
| Bloqueio de estacionar sessão já finalizada | `RegistrarEstacionamento_DeveLancar_QuandoSessaoJaFinalizada` |
| Saída antes de 30 minutos — gratuita | `RegistrarSaida_DeveCobrarZero_QuandoPermanenciaMenorQue30Min` |
| Saída após 30 minutos — cobrança com arredondamento para cima | `RegistrarSaida_DeveArredondarHoraCheiaParaCima` |
| Cálculo aplica o multiplicador travado na entrada | `RegistrarSaida_DeveUsarMultiplicadorDaEntrada_NaoODaSaida` |
| Bloqueio de saída em sessão não estacionada | `RegistrarSaida_DeveLancar_QuandoSessaoNaoEstacionada` |

### 9.3 `ParkingManagement.Application.Tests`

Testes de Handlers e Validators usando `NSubstitute`, mockando `ISectorRepository`, `ISpotRepository`, `IParkingSessionRepository` e `IGarageSimulatorClient`.

```
ParkingManagement.Application.Tests/
├── Features/
│   ├── Garage/
│   │   └── SyncGarageHandlerTests.cs
│   └── Parking/
│       ├── RegisterEntryHandlerTests.cs
│       ├── RegisterEntryValidatorTests.cs
│       ├── RegisterParkedHandlerTests.cs
│       ├── RegisterExitHandlerTests.cs
│       └── GetRevenueHandlerTests.cs
└── Common/
    └── Fakers/
```

### 9.4 Padrão AAA + FluentAssertions + Bogus

```csharp
[Fact]
public void IniciarEntrada_DeveLancar_QuandoLotacao100Porcento()
{
    // Arrange
    var placa = LicensePlate.Criar("ZUL0001");
    var entryTime = DateTime.UtcNow;
    var occupancyPercentage = 100m;

    // Act
    Action act = () => ParkingSession.IniciarEntrada(placa, entryTime, occupancyPercentage);

    // Assert
    act.Should().Throw<DomainException>()
       .WithMessage(ParkingSessionErrors.GaragemCheia);
}
```

### 9.5 `ParkingManagement.Integration.Tests`

- `WebApplicationFactory<Program>` para subir a API em memória.
- `Testcontainers.MsSql` para subir SQL Server real.
- DbUp aplicado antes da bateria.
- `IGarageSimulatorClient` substituído por um *fake* (`WireMock.Net` ou stub in-memory) simulando o `GET /garage` do simulador real, já que ele não está disponível no ambiente de CI.
- Cobre fluxo end-to-end: sync da garagem → `POST /webhook` (ENTRY) → `POST /webhook` (PARKED) → `POST /webhook` (EXIT) → `GET /revenue` reflete o valor cobrado.

---

## 10. Tratamento de erros

| Camada | Mecanismo |
|--------|-----------|
| Domínio | `DomainException` lançada nos guards |
| Application | Validators (FluentValidation) + Result pattern |
| API | `ExceptionHandlingMiddleware` mapeia para `ProblemDetails` HTTP adequado |

| Exceção | HTTP |
|---------|------|
| `ValidationException` (FluentValidation) | 400 |
| `event_type` desconhecido no webhook | 400 |
| `DomainException` (ex.: garagem cheia, sessão inexistente) | 422 |
| `NotFoundException` (setor/vaga não encontrados) | 404 |
| Outras | 500 (logada, mensagem genérica ao cliente) |

---

## 11. Segurança e qualidade

- Validação de entrada em todas as Requests via FluentValidation.
- Sanitização implícita do EF Core (queries parametrizadas).
- HTTPS obrigatório.
- Headers de segurança (`X-Content-Type-Options`, `X-Frame-Options`).
- Logging sem dados sensíveis (placas parcialmente mascaradas, se aplicável).
- Retry/backoff nas chamadas ao simulador (`AddStandardResilienceHandler`).
- Concorrência: uso de transação/isolamento adequado ao verificar capacidade da garagem no `ENTRY`, para evitar condição de corrida em múltiplas entradas simultâneas próximas ao limite (100%).
- Análise estática: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` + nullable enabled.

---

## 12. Premissas adotadas e pontos de ambiguidade do enunciado

> O enunciado (`Teste .NET.docx`) tem pontos que exigem interpretação. Cada premissa abaixo deve ser levada ao `README.md` do repositório, seguindo a mesma prática do projeto de referência (que documentou a substituição de SQL Server por PostgreSQL).

| Ponto | Ambiguidade | Premissa adotada | Justificativa |
|-------|-------------|-------------------|----------------|
| Lotação para preço dinâmico | O enunciado não diz se a % de lotação é global (garagem toda) ou por setor, e o setor só é conhecido no evento `PARKED`, não no `ENTRY` | Lotação calculada **globalmente** (ocupação total da garagem) no momento do `ENTRY`; o `BasePrice` do setor só é aplicado depois, no `EXIT`, junto com o multiplicador travado na entrada | No `ENTRY` ainda não se sabe em qual setor o veículo vai estacionar (isso só chega no `PARKED`), logo a única lotação calculável nesse momento é a global |
| Bloqueio de entrada a 100% | Não fica claro se o bloqueio de capacidade é por setor ou da garagem inteira | Bloqueio pela capacidade **total** da garagem (soma de `MaxCapacity` de todos os setores) | Consistente com a premissa acima e com "as garagens têm um único grupo de cancelas" (ponto único de entrada) |
| Correlação de eventos | Os eventos não trazem um `session_id`; apenas `license_plate` | Assume-se **no máximo uma sessão ativa por placa** por vez; `PARKED`/`EXIT` localizam a sessão em aberto mais recente daquela placa | Única chave disponível nos 3 payloads é `license_plate` |
| Vaga correspondente ao `PARKED` | O evento traz `lat`/`lng`, não o `id` da vaga | Match **exato** de coordenadas contra as vagas sincronizadas via `GET /garage` | O simulador deve reportar coordenadas idênticas às vagas cadastradas; nearest-match fica como melhoria futura |
| `GET /revenue` com corpo em requisição `GET` | Exemplo do enunciado mostra corpo JSON num `GET`, o que foge da convenção HTTP | Implementado como query string (`?date=&sector=`) | Aderência às convenções REST; documentado no README |
| Base URL do simulador | O enunciado não fornece a URL/imagem Docker do simulador | Configurável via `Simulator:BaseUrl` em `appsettings`/variável de ambiente, a preencher quando disponibilizado pela Estapar | Evita hardcode de endereço não confirmado |
| Banco de dados | Enunciado sugere SQL Server | Mantido SQL Server (sem substituição, ao contrário do `gestao-faturas`) | Sem motivo para desviar da sugestão original neste desafio |

---

## 13. Roadmap de execução (ordem sugerida)

1. **Setup da Solution**
   - Completar `ParkingManagement.slnx` já criado com os 5 projetos `src/` + 3 projetos `tests/`.
   - Configurar referências entre projetos e `Directory.Build.props`.

2. **Domínio**
   - `Entity`, `AggregateRoot`, `ValueObject`, `DomainException`.
   - `Sector`, `Spot`, `ParkingSession`, VOs (`LicensePlate`, `GeoCoordinate`, `PricingSnapshot`).
   - Testes unitários completos (RN-3 a RN-6, RN-9, RN-10).

3. **Migrations (DbUp)**
   - Scripts SQL: schema, `sectors`, `spots`, `parking_sessions`, índices.
   - Runner DbUp; testar localmente contra SQL Server.

4. **Infrastructure**
   - `ParkingDbContext` + Configurations.
   - Repositórios (`SectorRepository`, `SpotRepository`, `ParkingSessionRepository`).
   - `GarageSimulatorClient` (typed HttpClient + resiliência).
   - `AddInfrastructure`.

5. **Application**
   - `ApplicationErrorMessages`, `Result`/`Error`.
   - MediatR + `ValidationBehavior` + `LoggingBehavior`.
   - Features: `SyncGarage`, `RegisterEntry`, `RegisterParked`, `RegisterExit`, `GetRevenue`.
   - Validators e testes com mocks.

6. **API**
   - `WebhookController`, `RevenueController`, `GarageSyncStartupService`.
   - `ExceptionHandlingMiddleware`, Swagger.
   - Testar com REST Client / Postman e, se possível, contra o simulador real.

7. **Integration tests**
   - `WebApplicationFactory` + `Testcontainers.MsSql` + fake do simulador.
   - Fluxo E2E completo (sync → entry → parked → exit → revenue).

8. **Documentação**
   - `README.md` com: tecnologias, como rodar (Docker Compose), como rodar migrations, como rodar testes, premissas (seção 12), decisões técnicas, melhorias futuras.

9. **Empacotamento / Repositório**
   - `.gitignore`, `docker-compose.yml` (SQL Server + migrations + API), Dockerfiles multi-stage.
   - Tag de versão final.

---

## 14. Política de documentação contínua (README.md no GitHub)

> **Regra obrigatória do projeto:** a cada entrega concluída (camada, feature, caso de uso, conjunto de testes ou item do roadmap da seção 13), o `README.md` do repositório **deve ser atualizado** descrevendo o que foi entregue — mesma política do `gestao-faturas`.

### 14.1 Quando documentar

- Setup inicial da Solution e estrutura de projetos.
- Implementação de cada camada (Domain, Application, Infrastructure, Migrations, API).
- Cada caso de uso entregue (SyncGarage, RegisterEntry, RegisterParked, RegisterExit, GetRevenue).
- Cada bateria de testes (Domain, Application, Integration).
- Cada script de migration novo.
- Configurações de infraestrutura (Docker, SQL Server, variáveis de ambiente, URL do simulador).
- Premissas e decisões técnicas relevantes (seção 12) ou mudanças delas.

### 14.2 O que registrar a cada atualização

- **O que foi feito**, **Como executar/testar** (comandos, endpoints, payloads de exemplo), **Dependências adicionadas**, **Decisões técnicas**, **Status** no checklist da seção 15.

### 14.3 Estrutura mínima sugerida do `README.md`

```
# Parking Management — Gestão de Estacionamento

## Tecnologias
## Arquitetura
## Como executar
   - Pré-requisitos
   - Banco de dados (SQL Server + DbUp)
   - Configuração do simulador (Simulator:BaseUrl)
   - API
## Como rodar os testes
## Endpoints da API
## Estrutura da Solution
## Histórico de entregas      ← ★ atualizado a cada desenvolvimento concluído
## Premissas adotadas          ← seção 12 deste PLAN.md
## Decisões técnicas
## Melhorias futuras
```

### 14.4 Fluxo recomendado de commits

1. Implementar a entrega → 2. Cobrir com testes → 3. Atualizar `README.md` → 4. Commit (`docs(readme): adiciona <feature>` separado ou junto) → 5. Push.

> **Importante:** entregas não refletidas no `README.md` do GitHub são consideradas incompletas.

---

## 15. Checklist final de aderência ao desafio

- [ ] Sincronização inicial da garagem via `GET /garage`
- [ ] Webhook `POST /webhook` aceitando `ENTRY`, `PARKED`, `EXIT`
- [ ] Marcar vaga como ocupada na entrada estacionada (`PARKED`)
- [ ] Marcar vaga como disponível na saída (`EXIT`)
- [ ] Primeiros 30 minutos grátis
- [ ] Tarifa por hora cheia, arredondada para cima, usando `basePrice` do setor
- [ ] Bloqueio de novas entradas com garagem cheia
- [ ] Preço dinâmico por lotação na entrada (10% desconto / normal / +10% / +25%)
- [ ] `GET /revenue` retornando receita total por setor e data
- [ ] Testes automatizados cobrindo todos os cenários de regras de negócio
- [ ] README completo, com premissas documentadas
- [ ] Scripts de criação do banco (DbUp)
- [ ] Validação de entrada em todos os endpoints
- [ ] Tratamento de erros padronizado (`ProblemDetails`)
- [ ] Swagger/OpenAPI documentando os endpoints
- [ ] Docker Compose subindo banco + migrations + API com um único comando

---

> **Premissa principal de arquitetura:** este plano replica a organização em camadas, os padrões (Clean Architecture, DDD, CQRS via MediatR, Result pattern, DbUp, estratégia de testes em 3 níveis) e a política de documentação contínua do projeto `gestao-faturas`. As regras de negócio, entidades e endpoints são específicos deste desafio (`Teste .NET.docx`) e estão detalhados nas seções 3, 4, 7 e 8, com as ambiguidades do enunciado explicitadas na seção 12.
