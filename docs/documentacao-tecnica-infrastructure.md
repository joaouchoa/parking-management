# Documentação Técnica — Camada de Infrastructure (`ParkingManagement.Infrastructure`)

> **Objetivo deste documento:** explicar em profundidade cada decisão conceitual e técnica tomada na construção da camada de Infrastructure do sistema de Gestão de Estacionamento. Esta camada é a ponte entre o mundo puro do Domínio e os dois mundos concretos que o desafio exige: o banco de dados (SQL Server) e o sistema externo (o simulador da Estapar).

---

## 1. O Papel da Infrastructure na Arquitetura

```
┌─────────────────────────────────────────────┐
│              ParkingManagement.Api           │  ← Entrada (HTTP + Webhook)
├─────────────────────────────────────────────┤
│           ParkingManagement.Application      │  ← Casos de uso
├─────────────────────────────────────────────┤
│             ParkingManagement.Domain         │  ← Regras de negócio
├─────────────────────────────────────────────┤
│          ParkingManagement.Infrastructure    │  ← Detalhes técnicos ★
│   (EF Core, SQL Server, Repositórios,        │
│    Cliente HTTP do Simulador)                │
└─────────────────────────────────────────────┘
```

Diferente do `gestao-faturas`, esta Infrastructure tem **duas responsabilidades de integração externa**, não apenas uma:

1. **Persistência** — implementa os repositórios do Domínio (`ISectorRepository`, `ISpotRepository`, `IParkingSessionRepository`) e o `IUnitOfWork` da Application usando EF Core + SQL Server.
2. **Integração de saída** — implementa `IGarageSimulatorClient` (contrato da Application) usando um `HttpClient` tipado para consultar `GET /garage` no simulador.

Se amanhã o simulador mudar de protocolo (ex: gRPC em vez de REST), só a Infrastructure muda — a Application continua chamando `IGarageSimulatorClient.GetGarageConfigurationAsync()` sem saber o que existe por trás.

---

## 2. Estrutura do Projeto

```
ParkingManagement.Infrastructure/
├── Persistence/
│   ├── ParkingDbContext.cs                    ← Contexto do EF Core + implementação de IUnitOfWork
│   └── Configurations/
│       ├── SectorConfiguration.cs
│       ├── SpotConfiguration.cs
│       └── ParkingSessionConfiguration.cs
│
├── Repositories/
│   ├── SectorRepository.cs
│   ├── SpotRepository.cs
│   └── ParkingSessionRepository.cs
│
├── ExternalServices/
│   └── GarageSimulatorClient.cs               ← Implementa IGarageSimulatorClient
│
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs         ← AddInfrastructure()
```

**Por que `ExternalServices/` é uma pasta de primeira classe, ao lado de `Persistence/` e `Repositories/`?** Porque, neste projeto, "acessar dados externos" não significa só banco de dados — significa também falar HTTP com outro serviço. Separar deixa explícito que há duas fronteiras técnicas diferentes sendo cruzadas aqui, cada uma com suas próprias preocupações (transações e SQL de um lado; timeouts e retries de rede do outro).

---

## 3. Pacotes NuGet

| Pacote | Versão | Papel |
|--------|--------|-------|
| `Microsoft.EntityFrameworkCore` | 8.0.10 | Núcleo do EF Core |
| `Microsoft.EntityFrameworkCore.Relational` | 8.0.10 | Suporte a bancos relacionais |
| `Microsoft.EntityFrameworkCore.SqlServer` | 8.0.10 | Provider SQL Server |
| `Microsoft.Extensions.Http` | 8.0.1 | `AddHttpClient` / typed clients |
| `Microsoft.Extensions.Http.Resilience` | 8.10.0 | Políticas de retry/timeout/circuit breaker sobre `HttpClient` (baseado em Polly) |
| `Microsoft.Extensions.Configuration.Abstractions` | 8.0.0 | `IConfiguration` usado em `AddInfrastructure` |

**Por que SQL Server e não PostgreSQL (diferente do `gestao-faturas`)?** O enunciado deste desafio (`Teste .NET.docx`) já sugere explicitamente `"Entity Framework Core (com SQL Server)"` como stack, sem a ressalva de "outro banco relacional, desde que documentado" que existia no desafio de Faturas. Não havia motivo para desviar da sugestão original aqui — ver `PLAN.md`, seção de premissas.

**Por que `Microsoft.Extensions.Http.Resilience` e não Polly "puro"?** É o pacote oficial da Microsoft que envolve o Polly com uma API declarativa (`AddStandardResilienceHandler()`) já configurada com boas práticas (retry com backoff exponencial, timeout, circuit breaker) para `HttpClient`. Evita reinventar políticas de resiliência na mão para uma chamada HTTP simples.

---

## 4. `ParkingDbContext` — O Centro do EF Core (e do Unit of Work)

```csharp
public sealed class ParkingDbContext(DbContextOptions<ParkingDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Sector> Sectors => Set<Sector>();
    public DbSet<Spot> Spots => Set<Spot>();
    public DbSet<ParkingSession> ParkingSessions => Set<ParkingSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ParkingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

### 4.1 `ParkingDbContext` implementa `IUnitOfWork` diretamente

**Esta é a decisão mais importante deste arquivo, e vale explicar o "porquê" com detalhe.** `IUnitOfWork` (definido na Application — ver documentação de Application, seção 4.3) exige apenas:

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

O próprio `DbContext` do EF Core **já expõe** um método `SaveChangesAsync(CancellationToken)` com assinatura idêntica. Ao declarar `ParkingDbContext : DbContext, IUnitOfWork`, o C# reconhece automaticamente que o método herdado de `DbContext` **satisfaz** a interface — nenhuma implementação extra é necessária. Não existe uma classe `UnitOfWork.cs` separada porque seria um wrapper vazio, delegando para o mesmo método que o `DbContext` já tem.

**Por que isso importa na prática?** Foi exatamente a ausência deste mecanismo, numa versão anterior do código, que causava o bug descrito na documentação de Application (seção 4.3): os handlers chamavam `repository.AddAsync(...)` (que só marca entidades no *Change Tracker*) mas nunca `SaveChangesAsync`. Ao registrar `ParkingDbContext` também como `IUnitOfWork` no container de DI (seção 8), os handlers passaram a ter um ponto único e explícito para "persistir agora".

### 4.2 `ApplyConfigurationsFromAssembly`

Idêntico em espírito ao `gestao-faturas`: em vez de configurar o mapeamento inteiro dentro de `OnModelCreating` (que ficaria enorme com 3 entidades), cada entidade tem sua própria classe `IEntityTypeConfiguration<T>`, descoberta automaticamente por reflection.

---

## 5. As Configurações do EF Core

### 5.1 `SectorConfiguration` — A Mais Simples

```csharp
public sealed class SectorConfiguration : IEntityTypeConfiguration<Sector>
{
    public void Configure(EntityTypeBuilder<Sector> builder)
    {
        builder.ToTable("Sectors", "parking");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Code).HasMaxLength(10).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();
        builder.Property(s => s.BasePrice).HasColumnType("decimal(18,2)");
        builder.Property(s => s.MaxCapacity).IsRequired();
    }
}
```

**`ToTable("Sectors", "parking")` — por que um schema `parking` explícito, diferente do `public` implícito do `gestao-faturas` no PostgreSQL?** SQL Server usa `dbo` como schema padrão. Criar um schema próprio (`parking`) documenta explicitamente que estas tabelas pertencem ao domínio da aplicação — útil se, no futuro, outro sistema compartilhar o mesmo banco físico. Esse schema é criado no primeiro script de migration (`0001_create_schema.sql`, ver documentação de Migrations) e precisa **bater exatamente** com o que o EF Core espera aqui, já que o EF Core não gera schema neste projeto — apenas lê/escreve nas tabelas que o DbUp já criou.

**`HasIndex(s => s.Code).IsUnique()`** — impede dois setores com o mesmo código (`"A"` duplicado, por exemplo) em nível de banco, não só de aplicação. Reflete o índice único criado em `0005_indexes.sql`.

### 5.2 `SpotConfiguration` — Mapeando um Value Object com `OwnsOne`

```csharp
public sealed class SpotConfiguration : IEntityTypeConfiguration<Spot>
{
    public void Configure(EntityTypeBuilder<Spot> builder)
    {
        builder.ToTable("Spots", "parking");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.ExternalId).IsUnique();
        builder.Property(s => s.SectorCode).HasMaxLength(10).IsRequired();

        builder.OwnsOne(s => s.Coordinate, coordinate =>
        {
            coordinate.Property(c => c.Latitude).HasColumnName("Latitude");
            coordinate.Property(c => c.Longitude).HasColumnName("Longitude");
        });

        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
    }
}
```

**`OwnsOne(s => s.Coordinate, ...)` — a técnica central para mapear o VO `GeoCoordinate`.** `GeoCoordinate` (ver documentação de Domínio, seção 3.6) é uma classe C# imutável sem identidade própria — exatamente a definição de um **Owned Type** do EF Core. `OwnsOne` instrui o EF Core a:

1. **Não** tratar `GeoCoordinate` como uma entidade própria com sua tabela e chave estrangeira.
2. Mapear suas duas propriedades (`Latitude`, `Longitude`) como **colunas da própria tabela `Spots`** (`Latitude`, `Longitude`), sem nenhuma tabela extra.
3. Reconstruir automaticamente um `GeoCoordinate` ao ler uma linha do banco — sem precisar de conversores manuais como o `HasConversion` que o `gestao-faturas` usava para `NumeroFatura`.

**Por que `OwnsOne` aqui e não `HasConversion` (como o `NumeroFatura` do projeto de referência)?** `HasConversion` é ideal para VOs com **um único valor primitivo** (uma string, um decimal). `GeoCoordinate` tem **dois** valores (`Latitude` e `Longitude`) — `OwnsOne` é a ferramenta certa do EF Core para VOs compostos por múltiplos campos, mapeando cada campo do VO para sua própria coluna sem exigir um conversor customizado que serialize/desserialize os dois valores manualmente.

**`HasConversion<string>()` no enum `Status`** — diferente do `StatusFatura` do projeto de referência (que ficava como `INTEGER`), aqui optamos por armazenar o enum como `VARCHAR` (`"Livre"`, `"Ocupada"`). A vantagem é que uma consulta manual no banco (`SELECT * FROM parking.Spots`) já é autoexplicativa, sem precisar decorar que `0 = Livre` e `1 = Ocupada`. O custo é um pouco mais de espaço em disco — irrelevante na escala deste desafio.

### 5.3 `ParkingSessionConfiguration` — O Mapeamento Mais Rico

```csharp
public sealed class ParkingSessionConfiguration : IEntityTypeConfiguration<ParkingSession>
{
    public void Configure(EntityTypeBuilder<ParkingSession> builder)
    {
        builder.ToTable("ParkingSessions", "parking");
        builder.HasKey(s => s.Id);

        builder.OwnsOne(s => s.LicensePlate, licensePlate =>
        {
            licensePlate.Property(l => l.Value).HasColumnName("LicensePlate").HasMaxLength(8).IsRequired();
            licensePlate.HasIndex(l => l.Value);
        });

        builder.OwnsOne(s => s.PricingSnapshot, pricing =>
        {
            pricing.Property(p => p.OccupancyPercentageAtEntry).HasColumnName("OccupancyPercentageAtEntry").HasColumnType("decimal(5,2)");
            pricing.Property(p => p.Multiplier).HasColumnName("PriceMultiplier").HasColumnType("decimal(5,2)");
        });

        builder.OwnsOne(s => s.ParkedCoordinate, coordinate =>
        {
            coordinate.Property(c => c.Latitude).HasColumnName("ParkedLatitude");
            coordinate.Property(c => c.Longitude).HasColumnName("ParkedLongitude");
        });

        builder.Property(s => s.SectorCode).HasMaxLength(10);
        builder.Property(s => s.AmountCharged).HasColumnType("decimal(18,2)");
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(s => new { s.SectorCode, s.ExitTime });
        builder.HasIndex(s => s.Status);

        builder.Ignore(s => s.DomainEvents);
    }
}
```

**`OwnsOne` aplicado três vezes na mesma entidade (`LicensePlate`, `PricingSnapshot`, `ParkedCoordinate`)** — mostra como o padrão de VOs mapeados como colunas próprias se repete consistentemente. Nenhum desses três VOs ganha uma tabela separada; todos viram colunas de `ParkingSessions`.

**`licensePlate.HasIndex(l => l.Value)`** — índice sobre uma propriedade de um *owned type*. É essencial para a performance de `GetActiveByLicensePlateAsync` (ver seção 6.3), que filtra por `LicensePlate.Value` a cada evento de webhook recebido — sem índice, essa busca degradaria para uma varredura completa da tabela conforme o volume de sessões cresce.

**`builder.Ignore(s => s.DomainEvents)`** — a propriedade `DomainEvents` (herdada de `AggregateRoot`) não deve virar coluna nem tabela. Ela existe apenas em memória, durante o processamento de uma operação, para eventualmente ser publicada após `SaveChangesAsync` (mecanismo ainda não conectado a um *dispatcher* de eventos neste projeto — ver "Melhorias futuras" no `PLAN.md`). Sem o `Ignore` explícito, o EF Core tentaria mapear a coleção como um relacionamento, o que falharia por `IDomainEvent` não ser uma entidade mapeada.

**`HasIndex(s => new { s.SectorCode, s.ExitTime })`** — índice composto que acelera diretamente a query de receita (RN-7: `GetRevenueAsync`, seção 6.3), que filtra por ambos os campos simultaneamente.

---

## 6. Os Repositórios — Implementando os Contratos do Domínio

### 6.1 `SectorRepository`

```csharp
public sealed class SectorRepository(ParkingDbContext dbContext) : ISectorRepository
{
    public Task<Sector?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.Sectors.FirstOrDefaultAsync(s => s.Code == code, cancellationToken);

    public Task<int> GetTotalCapacityAsync(CancellationToken cancellationToken = default) =>
        dbContext.Sectors.SumAsync(s => s.MaxCapacity, cancellationToken);
}
```

**`GetTotalCapacityAsync` — um `SUM` no banco, não uma soma em memória.** `SumAsync` traduz para `SELECT SUM(MaxCapacity) FROM parking.Sectors` — o cálculo acontece no SQL Server, sem trazer todas as linhas para a aplicação. Esse valor alimenta diretamente o cálculo de lotação global usado por `RegisterEntryHandler` (ver documentação de Application).

### 6.2 `SpotRepository` — `FindByCoordinateAsync`, o Ponto Mais Delicado

```csharp
public sealed class SpotRepository(ParkingDbContext dbContext) : ISpotRepository
{
    public Task<Spot?> FindByCoordinateAsync(GeoCoordinate coordinate, CancellationToken cancellationToken = default) =>
        dbContext.Spots.FirstOrDefaultAsync(
            s => s.Coordinate.Latitude == coordinate.Latitude && s.Coordinate.Longitude == coordinate.Longitude,
            cancellationToken);

    public Task<int> CountOccupiedAsync(CancellationToken cancellationToken = default) =>
        dbContext.Spots.CountAsync(s => s.Status == SpotStatus.Ocupada, cancellationToken);
}
```

**`FindByCoordinateAsync` implementa a decisão de design documentada no `PLAN.md`: match exato de coordenadas.** O evento `PARKED` traz `lat`/`lng` como `double` — a query compara ambos os componentes contra os valores sincronizados de `GET /garage`. Isso funciona porque o simulador do desafio reporta, no evento `PARKED`, exatamente as mesmas coordenadas cadastradas para a vaga (não coordenadas de GPS "reais" com ruído de precisão). Uma implementação mais robusta para coordenadas com imprecisão de sensor usaria uma distância mínima (haversine) em vez de igualdade exata — documentado como possível melhoria futura, não necessária para o escopo atual.

**`CountOccupiedAsync` traduz para `SELECT COUNT(*) FROM parking.Spots WHERE Status = 'Ocupada'`** — outro agregado calculado no banco, evitando trazer todas as vagas para contar em memória.

### 6.3 `ParkingSessionRepository` — Correlação por Placa e Consulta de Receita

```csharp
public sealed class ParkingSessionRepository(ParkingDbContext dbContext) : IParkingSessionRepository
{
    public Task<ParkingSession?> GetActiveByLicensePlateAsync(string licensePlate, CancellationToken cancellationToken = default) =>
        dbContext.ParkingSessions
            .Where(s => s.LicensePlate.Value == licensePlate && s.Status != ParkingSessionStatus.Finalizado)
            .OrderByDescending(s => s.EntryTime)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<decimal> GetRevenueAsync(string sectorCode, DateOnly date, CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);

        return dbContext.ParkingSessions
            .Where(s => s.SectorCode == sectorCode
                        && s.Status == ParkingSessionStatus.Finalizado
                        && s.ExitTime >= start
                        && s.ExitTime < end)
            .SumAsync(s => s.AmountCharged ?? 0m, cancellationToken);
    }
}
```

**`Where(... && s.Status != ParkingSessionStatus.Finalizado).OrderByDescending(s => s.EntryTime).FirstOrDefaultAsync()`** — esta é a query que resolve, na prática, a premissa "no máximo uma sessão ativa por placa" documentada no domínio. `OrderByDescending(EntryTime)` garante que, mesmo que existisse mais de uma sessão não finalizada para a mesma placa (um cenário que a regra de negócio não deveria permitir, mas que uma falha de integração poderia produzir), o repositório sempre retorna a **mais recente** — o comportamento mais seguro diante de dados inconsistentes.

**`GetRevenueAsync` — por que converter `DateOnly` para um intervalo `[start, end)` em vez de comparar `ExitTime.Date == date`?** Comparar por intervalo (`>= start && < end`) permite que o SQL Server utilize o índice `(SectorCode, ExitTime)` (ver seção 5.3) de forma eficiente — uma comparação com uma função (`CAST(ExitTime AS DATE) = @date` ou equivalente LINQ `.Date`) frequentemente impede o uso de índices, forçando uma varredura completa. Escrever a condição como um intervalo sargable (*Search ARGument ABLE*) é uma prática padrão de otimização de queries.

**`s.AmountCharged ?? 0m` dentro do `SumAsync`** — `AmountCharged` é `decimal?` (só é preenchido quando a sessão é finalizada). Embora o filtro `Status == Finalizado` já garanta, na prática, que só sessões com valor preenchido entrem na soma, o `??` é uma defesa adicional: `SumAsync` sobre uma coluna nullable sem tratamento explícito pode gerar comportamento surpreendente (ou erro de tradução, dependendo do provider) se alguma linha tiver `NULL`.

---

## 7. `GarageSimulatorClient` — Falando HTTP com o Mundo Externo

```csharp
public sealed class GarageSimulatorClient(HttpClient httpClient) : IGarageSimulatorClient
{
    public async Task<GarageConfigurationDto> GetGarageConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<GarageConfigurationDto>("/garage", cancellationToken);

        return response ?? throw new InvalidOperationException(
            "O simulador retornou uma resposta vazia para GET /garage.");
    }
}
```

**Por que `GetFromJsonAsync<T>` e não `GetAsync` + desserialização manual?** É o método de extensão do `System.Net.Http.Json` que combina a requisição HTTP e a desserialização JSON num único passo, usando `System.Text.Json` por baixo — reduz código boilerplate para um caso tão direto quanto este (GET simples, sem headers customizados).

**Por que lançar `InvalidOperationException` se a resposta for `null` em vez de retornar `null` e deixar o chamador tratar?** Porque uma resposta vazia de `GET /garage` é um cenário **inesperado e não recuperável** para o `SyncGarageHandler` (ver documentação de Application) — não há como sincronizar setores/vagas sem dados. Diferente de "sessão não encontrada" (esperado, tratado com `Result`), aqui a ausência de dados é uma falha do sistema externo, corretamente modelada como exceção que propaga até o `GarageSyncStartupService` da API (ver documentação de API), que interrompe a inicialização da aplicação.

**`GarageSimulatorClient` não trata retries/timeouts diretamente** — essa responsabilidade fica inteiramente na configuração do `HttpClient` tipado, registrada em `AddInfrastructure` (seção 8) via `AddStandardResilienceHandler()`. Isso mantém o `GarageSimulatorClient` simples e focado apenas em "fazer a chamada e desserializar" — a resiliência é uma preocupação transversal, configurada centralmente.

---

## 8. `AddInfrastructure` — Registro de Dependências

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ParkingDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("SqlServer")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ParkingDbContext>());

        services.AddScoped<ISectorRepository, SectorRepository>();
        services.AddScoped<ISpotRepository, SpotRepository>();
        services.AddScoped<IParkingSessionRepository, ParkingSessionRepository>();

        services.AddHttpClient<IGarageSimulatorClient, GarageSimulatorClient>(client =>
            {
                var baseUrl = configuration["Simulator:BaseUrl"]
                    ?? throw new InvalidOperationException("Configuração 'Simulator:BaseUrl' não foi definida.");
                client.BaseAddress = new Uri(baseUrl);
            })
            .AddStandardResilienceHandler();

        return services;
    }
}
```

### 8.1 `services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ParkingDbContext>())`

**Esta linha merece atenção especial.** Em vez de `AddScoped<IUnitOfWork, ParkingDbContext>()` (que criaria uma **segunda instância** de `ParkingDbContext` só para satisfazer o `IUnitOfWork`), a *factory* explícita (`sp => sp.GetRequiredService<ParkingDbContext>()`) garante que o `IUnitOfWork` resolvido pelo container seja **exatamente a mesma instância** de `ParkingDbContext` já registrada (e usada pelos repositórios) na mesma requisição/escopo. Isso é essencial: se fossem instâncias diferentes, `sessionRepository.Update(session)` marcaria uma mudança num `DbContext`, e `unitOfWork.SaveChangesAsync()` chamaria `SaveChanges` em **outro** `DbContext` vazio — nada seria persistido, reproduzindo exatamente o bug que motivou a criação do `IUnitOfWork` (ver documentação de Application, seção 4.3).

### 8.2 `AddHttpClient<IGarageSimulatorClient, GarageSimulatorClient>` — Typed Client

O padrão de **Typed Client** do `IHttpClientFactory`: em vez de injetar um `HttpClient` genérico em qualquer lugar (risco de esgotamento de sockets se mal gerenciado — o problema clássico do `new HttpClient()` a cada chamada), o container gerencia o ciclo de vida do `HttpClient` internamente e o entrega já configurado (com `BaseAddress` e resiliência) sempre que `IGarageSimulatorClient` é injetado.

**`AddStandardResilienceHandler()`** adiciona, numa única chamada, um conjunto de políticas Polly recomendadas pela Microsoft: retry com backoff exponencial e jitter, timeout por tentativa, timeout total, e circuit breaker. Isso é particularmente relevante no `docker-compose` deste projeto: a API pode subir **antes** do simulador estar pronto para responder — as políticas de retry dão à API algumas tentativas automáticas antes de desistir e falhar o `GarageSyncStartupService`.

### 8.3 Falha Rápida na Ausência de Configuração

```csharp
var baseUrl = configuration["Simulator:BaseUrl"]
    ?? throw new InvalidOperationException("Configuração 'Simulator:BaseUrl' não foi definida.");
```

Se `Simulator:BaseUrl` não estiver configurado (`appsettings.json`, variável de ambiente, etc.), a aplicação falha **imediatamente no startup**, com uma mensagem clara, em vez de permitir que o sistema suba "quebrado" e falhe de forma confusa na primeira tentativa de sincronização.

---

## 9. Diagrama de Dependências

```
┌─────────────────────────────────────────────────────────────────┐
│                ParkingManagement.Infrastructure                  │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  DependencyInjection/ServiceCollectionExtensions          │    │
│  │  AddInfrastructure(configuration)                        │    │
│  │       ├── AddDbContext<ParkingDbContext>(UseSqlServer)   │    │
│  │       ├── AddScoped<IUnitOfWork>(→ ParkingDbContext)     │    │
│  │       ├── AddScoped<ISectorRepository, SectorRepository>│    │
│  │       ├── AddScoped<ISpotRepository, SpotRepository>    │    │
│  │       ├── AddScoped<IParkingSessionRepository, ...>     │    │
│  │       └── AddHttpClient<IGarageSimulatorClient, ...>    │    │
│  │             .AddStandardResilienceHandler()              │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  Persistence/ParkingDbContext : DbContext, IUnitOfWork    │    │
│  │       └── ApplyConfigurationsFromAssembly                │    │
│  │               ├── SectorConfiguration                    │    │
│  │               ├── SpotConfiguration (OwnsOne Coordinate) │    │
│  │               └── ParkingSessionConfiguration            │    │
│  │                     (OwnsOne × 3, índices, Ignore events)│    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  Repositories/                                            │    │
│  │  SectorRepository · SpotRepository · ParkingSessionRepo  │    │
│  │  Implementam contratos definidos no Domain               │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  ExternalServices/GarageSimulatorClient                   │    │
│  │  Implementa IGarageSimulatorClient (definido na App)     │    │
│  │  HttpClient tipado + AddStandardResilienceHandler         │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  Depende de:                                                      │
│  ├── ParkingManagement.Domain (repositórios, entidades, VOs)     │
│  ├── ParkingManagement.Application (IUnitOfWork, IGarageSimulatorClient) │
│  └── Microsoft.EntityFrameworkCore.SqlServer, Http.Resilience    │
└─────────────────────────────────────────────────────────────────┘
```

---

## 10. Resumo das Decisões Técnicas

| Decisão | Alternativa | Por que escolhemos assim |
|---------|-------------|--------------------------|
| SQL Server + `Microsoft.EntityFrameworkCore.SqlServer` | PostgreSQL + Npgsql (como no `gestao-faturas`) | O enunciado deste desafio já sugere SQL Server explicitamente, sem ressalva para outro banco |
| `ParkingDbContext` implementa `IUnitOfWork` diretamente | Classe `UnitOfWork` separada delegando ao DbContext | `DbContext.SaveChangesAsync` já satisfaz a interface por assinatura idêntica; uma classe extra seria um wrapper vazio |
| `AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ParkingDbContext>())` | `AddScoped<IUnitOfWork, ParkingDbContext>()` | Garante a mesma instância de `DbContext` entre repositórios e Unit of Work no mesmo escopo — evita "salvar no contexto errado" |
| `OwnsOne` para `GeoCoordinate`, `LicensePlate`, `PricingSnapshot` | `HasConversion` (como `NumeroFatura` no projeto de referência) | VOs com mais de um campo primitivo mapeiam melhor como Owned Types do que com conversores manuais de string única |
| Schema `parking` explícito | Schema `dbo` padrão | Isola as tabelas da aplicação, documentando a ownership caso o banco seja compartilhado no futuro |
| Enum armazenado como `string` (`Status`) | `INTEGER` (como `StatusFatura` no projeto de referência) | Legibilidade em consultas manuais ao banco durante desenvolvimento e depuração |
| Match exato de coordenadas em `FindByCoordinateAsync` | Cálculo de distância mínima (haversine) | O simulador do desafio reporta coordenadas idênticas às cadastradas; complexidade de geolocalização real não se justifica no escopo atual |
| `GetRevenueAsync` com intervalo `[start, end)` | Comparação `ExitTime.Date == date` | Mantém a query *sargable*, permitindo uso eficiente do índice `(SectorCode, ExitTime)` |
| `Typed HttpClient` + `AddStandardResilienceHandler` | `HttpClient` manual com Polly customizado | API declarativa oficial da Microsoft já cobre retry/timeout/circuit breaker sem código extra |
| Falha rápida se `Simulator:BaseUrl` ausente | Deixar `BaseAddress` nulo e falhar na primeira chamada | Erro de configuração aparece imediatamente no startup, não em produção durante a primeira sincronização |

