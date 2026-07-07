# Documentação Técnica — Camada de Application (`ParkingManagement.Application`)

> **Objetivo deste documento:** explicar em profundidade a arquitetura da camada de Application, seus padrões e decisões técnicas. Para não repetir os mesmos conceitos em cada caso de uso, usaremos o **`RegisterExit`** como caso de estudo completo — ele é o mais representativo por envolver duas buscas em repositórios diferentes (sessão e setor), duas possibilidades de "não encontrado", uma chamada ao domínio que pode lançar `DomainException`, uma segunda operação de escrita (liberar a vaga) e persistência via Unit of Work. Os demais casos de uso seguem o mesmo padrão, com variações pontuais.

---

## 1. O Papel da Application na Arquitetura

A camada de Application é o **orquestrador** do sistema. Ela não contém regras de negócio (isso é responsabilidade do Domínio) e não sabe nada sobre EF Core, SQL Server ou HTTP (isso é responsabilidade da Infrastructure e da Api). Ela apenas coordena o fluxo:

```
Receber intenção → Validar entrada → Consultar repositórios → Chamar o Domínio → Persistir → Retornar resposta
```

Cada **caso de uso** do sistema (sincronizar garagem, registrar entrada, registrar estacionamento, registrar saída, consultar receita) é representado por um **Handler** do MediatR.

---

## 2. Estrutura do Projeto

```
ParkingManagement.Application/
├── Common/
│   ├── Behaviors/
│   │   ├── ValidationBehavior.cs      ← Intercepta e valida antes do handler
│   │   └── LoggingBehavior.cs         ← Loga início e fim de cada request
│   ├── Mediator/
│   │   ├── ICommand.cs / IQuery.cs
│   │   └── ICommandHandler.cs / IQueryHandler.cs
│   ├── Results/
│   │   ├── Result.cs                  ← Encapsula sucesso ou falha
│   │   └── Error.cs                   ← Código + mensagem + tipo de erro
│   ├── Persistence/
│   │   └── IUnitOfWork.cs             ← Contrato de "salvar tudo de uma vez"
│   ├── Integrations/
│   │   └── IGarageSimulatorClient.cs  ← Contrato do cliente HTTP do simulador
│   └── Errors/
│       └── ApplicationErrorMessages.cs ← Mensagens centralizadas
│
├── Features/
│   ├── Garage/
│   │   ├── GarageUpsert.cs            ← Upsert de setores/vagas, compartilhado por SyncGarage e SeedGarage
│   │   └── Commands/
│   │       ├── SyncGarage/            ← Sincroniza setores/vagas a partir do simulador (GET /garage)
│   │       └── SeedGarage/            ← Popula setores/vagas direto no corpo da requisição (sem simulador)
│   └── Parking/
│       ├── Commands/
│       │   ├── RegisterEntry/         ← Evento ENTRY
│       │   ├── RegisterParked/        ← Evento PARKED
│       │   └── RegisterExit/          ← ★ Caso de estudo deste documento
│       └── Queries/
│           └── GetRevenue/            ← GET /revenue
│
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs ← AddApplication()
```

**Por que `Features/Garage` e `Features/Parking` espelham exatamente `Domain/Garage` e `Domain/Parking`?** Para que a navegação entre as camadas seja previsível — quem procura o caso de uso que orquestra o agregado `ParkingSession` sabe que vai olhar em `Features/Parking`, do mesmo jeito que o agregado está em `Domain/Parking`.

**Por que não existe `Features/Parking/Commands/RegisterParked`... com Query também?** Porque neste sistema não há um "GetParkingSessionById" exposto — o único caso de uque de leitura do desafio é `GetRevenue`. A ausência de mais Queries não é uma omissão, é reflexo direto do escopo do enunciado (apenas `GET /revenue` e o webhook).

---

## 3. Pacotes NuGet

| Pacote | Versão | Papel |
|--------|--------|-------|
| `MediatR` | 12.4.1 | Barramento de mediação — desacopla quem envia o request de quem o processa |
| `FluentValidation` | 11.11.0 | Validação declarativa com regras encadeadas |
| `FluentValidation.DependencyInjectionExtensions` | 11.11.0 | Registra todos os validators no container DI automaticamente |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 8.0.2 | `IServiceCollection` para o extension method `AddApplication` |
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.2 | Interface `ILogger<T>` para o `LoggingBehavior` |

---

## 4. Os Blocos de Construção do `Common/`

### 4.1 `ICommand`/`IQuery` e `ICommandHandler`/`IQueryHandler`

```csharp
public interface ICommand<out TResponse> : IRequest<TResponse> { }
public interface IQuery<out TResponse>   : IRequest<TResponse> { }

public interface ICommandHandler<in TCommand, TResponse>
    : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse> { }
```

Interfaces marcadoras sobre o `IRequest<T>`/`IRequestHandler<,>` do MediatR — a diferença é puramente semântica (Command muda estado, Query só lê), mas a constraint `where TCommand : ICommand<TResponse>` garante, em tempo de compilação, que um handler de comando não seja acidentalmente registrado para uma query e vice-versa.

**Por que `out TResponse` (covariante)?** Permite que `ICommand<Result<RegisterEntryResponse>>` seja tratado como `IRequest<Result<RegisterEntryResponse>>` sem conversões explícitas — necessário para o MediatR resolver o handler correto via reflection sem restrições desnecessárias de variância.

### 4.2 `Result` e `Error` — O Result Pattern

```csharp
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

public sealed class Result<TValue> : Result
{
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Não é possível acessar o valor de um resultado de falha.");
}

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error None => new(string.Empty, string.Empty, ErrorType.None);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
}
```

**O problema que o Result pattern resolve:** "sessão ativa não encontrada" (ex: um `PARKED` chega para uma placa que nunca teve `ENTRY`) não é uma situação *excepcional* — é um cenário esperado quando o webhook recebe eventos fora de ordem ou duplicados. Lançar exceção para isso seria caro e semanticamente errado.

**Por que `Result` base e `Result<TValue>` derivado, em vez de só `Result<TValue>` sempre?** Alguns comandos (nenhum neste projeto atualmente, mas o padrão fica pronto) podem não precisar devolver valor algum — só sucesso/falha. Ter `Result` sem tipo genérico cobre esse caso sem forçar um `Result<Unit>` artificial.

**`ErrorType` explícito no `Error`, diferente do `gestao-faturas` (que usava `Error.Code` como string "NotFound"/"Conflict"):** Aqui o tipo do erro é um `enum` (`None`, `Validation`, `NotFound`, `Conflict`, `Failure`), não uma string mágica comparada por igualdade. Isso move o mapeamento HTTP (feito no controller — ver documentação da API) para um `switch` exaustivo sobre um enum, que o compilador pode avisar se um novo `ErrorType` for adicionado e esquecido em algum `switch`.

**Divisão de responsabilidades entre Result e Exceptions neste projeto:**

| Situação | Mecanismo | Quem trata | HTTP |
|----------|-----------|-----------|------|
| "Sessão ativa não encontrada" | `Result.Failure(Error.NotFound(...))` | Controller verifica `IsFailure` | 404 |
| "Garagem cheia" / "sessão já finalizada" | `DomainException` (domínio lança) | `ExceptionHandlingMiddleware` | 422 |
| "Placa vazia" / "data obrigatória" | `ValidationException` (FluentValidation) | `ExceptionHandlingMiddleware` | 400 |
| `event_type` desconhecido no webhook | Tratado no próprio Controller (nem chega ao MediatR) | Controller | 400 |
| Erro inesperado (bug, falha do simulador) | `Exception` genérica | `ExceptionHandlingMiddleware` | 500 |

### 4.3 `IUnitOfWork` — A Peça que Faltava (e que um teste de integração revelou)

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

**Por que esta interface existe na Application e não é só "chamar o `DbContext` diretamente"?** Porque a Application não pode depender do EF Core (Infrastructure) — precisa de uma **abstração**. `IUnitOfWork` é essa abstração: "persista tudo o que foi rastreado até agora, de uma vez". A Infrastructure a implementa no próprio `ParkingDbContext` (ver documentação de Infrastructure).

**Nota de desenvolvimento:** esta interface não fazia parte do desenho inicial dos handlers. Durante a validação end-to-end com o teste de integração (`ParkingFlowTests`, ver documentação de Testes), o fluxo completo (`ENTRY → PARKED → EXIT → GET /revenue`) falhava com "garagem no limite de capacidade" mesmo com a garagem vazia — a causa raiz era que os repositórios (`AddAsync`, `Update`) apenas marcavam entidades no *Change Tracker* do EF Core, mas **nenhum handler chamava `SaveChangesAsync`**. Os dados nunca eram persistidos de fato. A introdução de `IUnitOfWork`, injetado em todo handler que muda estado, corrigiu o problema. Esse é um exemplo concreto de por que a pirâmide de testes deste projeto inclui um nível de integração real contra banco de dados (ver documentação de Testes, seção sobre o que cada nível de teste cobre).

### 4.4 `IGarageSimulatorClient` — O Contrato para o Mundo Externo

```csharp
public interface IGarageSimulatorClient
{
    Task<GarageConfigurationDto> GetGarageConfigurationAsync(CancellationToken cancellationToken = default);
}

public sealed record GarageConfigurationDto(
    IReadOnlyCollection<GarageSectorDto> Garage,
    IReadOnlyCollection<GarageSpotDto> Spots);

public sealed record GarageSectorDto(string Sector, decimal BasePrice, int MaxCapacity);
public sealed record GarageSpotDto(long Id, string Sector, double Lat, double Lng);
```

**Por que este contrato (e os DTOs) fica na Application e não na Infrastructure?** Mesma lógica de `IUnitOfWork` e dos repositórios do domínio: a Application **define o que precisa** de uma integração externa; a Infrastructure **implementa como** (nesse caso, com um `HttpClient` tipado — ver documentação de Infrastructure). Isso permite, por exemplo, que os testes de Application substituam esse contrato por um mock com `NSubstitute`, sem nunca precisar de rede real.

**Por que os DTOs (`GarageSectorDto`, `GarageSpotDto`) são records simples aqui, e não os tipos de domínio `Sector`/`Spot`?** Porque o formato de `GET /garage` é um **contrato externo** (do simulador da Estapar), não um conceito do nosso domínio. Se o formato do JSON mudar (ex: renomear `"sector"` para `"sectorCode"`), só este DTO muda — `Sector`/`Spot` do domínio permanecem intocados. É a mesma razão de existir DTOs de Request/Response separados das entidades.

### 4.5 `ApplicationErrorMessages` — Central de Mensagens

```csharp
public static class ApplicationErrorMessages
{
    public static class Parking
    {
        public const string SessaoAtivaNaoEncontrada = "Nenhuma sessão ativa encontrada para esta placa.";
        public const string VagaNaoEncontradaPorCoordenada = "Nenhuma vaga corresponde às coordenadas informadas.";
        public const string SetorDaSessaoNaoEncontrado = "Setor associado à sessão não foi encontrado.";
        // ...
    }
    public static class Garage { /* ... */ }
    public static class Revenue { /* ... */ }
}
```

Todas as mensagens de erro **da camada de Application** (usadas em Validators e nos `Result.Failure` dos Handlers) residem aqui — nunca como string literal espalhada pelo código. Note que isso é **distinto** de `ParkingSessionErrors`/`GarageErrors` do Domínio (ver documentação de Domínio): mensagens de regra de negócio (ex: "garagem cheia") pertencem ao domínio; mensagens de orquestração da Application (ex: "sessão não encontrada", que é uma condição só detectável consultando o repositório) pertencem aqui.

---

## 5. O Pipeline do MediatR

```
ISender.Send(request)
        │
        ▼
┌───────────────────────┐
│   LoggingBehavior     │  ← 1° executa: loga "Processando RegisterExitRequest"
└──────────┬────────────┘
           ▼
┌───────────────────────┐
│  ValidationBehavior   │  ← 2° executa: valida com FluentValidation
└──────────┬────────────┘
           │ se inválido → lança ValidationException → para aqui
           ▼
┌───────────────────────┐
│  RegisterExitHandler  │  ← 3° executa: a lógica real (seção 6)
└──────────┬────────────┘
           ▼
        resultado
```

### 5.1 `ValidationBehavior`

```csharp
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = validators
            .Select(validator => validator.Validate(context))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

**Sintaxe de construtor primário (`ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)`):** é o recurso de *primary constructors* do C# 12 — o parâmetro `validators` já vira implicitamente um campo privado da classe, sem a necessidade de declarar `private readonly` e atribuir manualmente no corpo do construtor. Usado consistentemente em todos os Handlers e Behaviors deste projeto.

**`RequestHandlerDelegate<TResponse> next` sem `CancellationToken` no `Invoke`:** na versão 12.4.1 do MediatR usada neste projeto, o delegate do pipeline não recebe mais o `CancellationToken` como parâmetro (`next()`, e não `next(cancellationToken)`) — o token já está fechado (closure) internamente pelo próprio MediatR. Isso é uma diferença de API em relação a versões anteriores do MediatR e vale registrar porque é uma fonte comum de erro de compilação ao migrar exemplos de versões antigas.

### 5.2 `LoggingBehavior`

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("Processando {RequestName}", requestName);
        var response = await next();
        logger.LogInformation("Concluído {RequestName}", requestName);
        return response;
    }
}
```

Simples, mas essencial: todo evento do webhook (`RegisterEntryRequest`, `RegisterParkedRequest`, `RegisterExitRequest`) e toda consulta de receita geram duas linhas de log, dando visibilidade de todo o tráfego que passa pelo sistema — importante num sistema orientado a webhook, onde não há um "usuário" na tela clicando botões para correlacionar com os logs.

---

## 6. Caso de Estudo: `RegisterExit`

Este caso de uso concentra os cenários mais interessantes: busca em dois repositórios diferentes, dois pontos de possível "não encontrado", delegação ao domínio (que pode lançar `DomainException`), uma segunda escrita (liberar a vaga) e persistência via `IUnitOfWork`.

### 6.1 O Fluxo Completo

```
POST /webhook   { "license_plate": "ZUL0001", "exit_time": "...", "event_type": "EXIT" }
                │
                ▼
    WebhookController.Receive(envelope)
    → identifica event_type == "EXIT"
    → monta RegisterExitRequest(envelope.LicensePlate, envelope.ExitTime)
    → sender.Send(request)
                │
                ▼
    ┌─────────────────────────────────────────────────────┐
    │  PIPELINE MEDIATR                                   │
    │  1. LoggingBehavior       → loga "Processando..."   │
    │  2. ValidationBehavior    → RegisterExitValidator:  │
    │       LicensePlate not empty?  ✓                    │
    │       ExitTime != default?      ✓                   │
    │     → se falhar: ValidationException (400)          │
    │  3. RegisterExitHandler   → lógica real (ver 6.3)   │
    │  4. LoggingBehavior       → loga "Concluído..."     │
    └─────────────────────────────────────────────────────┘
                │
                ▼
    Controller recebe Result<RegisterExitResponse>
    → IsSuccess? → 200 OK
    → IsFailure (NotFound)? → 404
    → DomainException (sessão não estacionada, base price inválido...)? → 422
```

### 6.2 `RegisterExitRequest`/`Response` — Os DTOs

```csharp
public sealed record RegisterExitRequest(
    string LicensePlate,
    DateTime ExitTime
) : ICommand<Result<RegisterExitResponse>>;

public sealed record RegisterExitResponse(
    Guid SessionId,
    DateTime ExitTime,
    decimal AmountCharged
);
```

**Por que `RegisterExitRequest` não recebe `SectorCode` ou `SpotId`?** Porque o evento `EXIT` do desafio só traz `license_plate` e `exit_time` (ver seção "Simulador" do `Teste .NET.docx`). O setor e a vaga já foram gravados na sessão durante `RegisterParked` — o handler os recupera a partir da sessão encontrada, não da requisição.

### 6.3 `RegisterExitValidator` — Validação de Input

```csharp
public sealed class RegisterExitValidator : AbstractValidator<RegisterExitRequest>
{
    public RegisterExitValidator()
    {
        RuleFor(x => x.LicensePlate)
            .NotEmpty().WithMessage(ApplicationErrorMessages.Parking.LicensePlateObrigatoria);

        RuleFor(x => x.ExitTime)
            .NotEqual(default(DateTime)).WithMessage(ApplicationErrorMessages.Parking.ExitTimeObrigatorio);
    }
}
```

**Divisão de responsabilidade entre Validator e Domínio:**

| Regra | Onde fica | Por quê |
|-------|-----------|---------|
| `LicensePlate` não vazia | Validator | Formato de input — nem chega a consultar o repositório com placa vazia |
| `ExitTime` diferente do `default(DateTime)` | Validator | Detecta o caso comum de erro de desserialização (campo ausente no JSON vira `DateTime.MinValue`) antes de chegar ao domínio |
| Formato da placa (`[A-Z0-9]{5,8}`) | **Só o Domínio** (`LicensePlate.Criar`) | É uma invariante de negócio sobre o que é uma placa válida, não apenas "campo preenchido" |
| Sessão estar em `Estacionado` antes de sair | **Só o Domínio** (`ParkingSession.RegistrarSaida`) | Depende do estado atual da sessão no banco — o validator não acessa repositório |
| `ExitTime >= EntryTime` | **Só o Domínio** | Depende de outro campo da entidade (`EntryTime`), carregado do banco — não é validável a partir do request isolado |

### 6.4 `RegisterExitHandler` — O Orquestrador

```csharp
public sealed class RegisterExitHandler(
    IParkingSessionRepository sessionRepository,
    ISectorRepository sectorRepository,
    ISpotRepository spotRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RegisterExitRequest, Result<RegisterExitResponse>>
{
    public async Task<Result<RegisterExitResponse>> Handle(RegisterExitRequest request, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetActiveByLicensePlateAsync(request.LicensePlate, cancellationToken);
        if (session is null)
        {
            return Result.Failure<RegisterExitResponse>(
                Error.NotFound("Parking.SessaoNaoEncontrada", ApplicationErrorMessages.Parking.SessaoAtivaNaoEncontrada));
        }

        var sector = await sectorRepository.GetByCodeAsync(session.SectorCode!, cancellationToken);
        if (sector is null)
        {
            return Result.Failure<RegisterExitResponse>(
                Error.NotFound("Parking.SetorNaoEncontrado", ApplicationErrorMessages.Parking.SetorDaSessaoNaoEncontrado));
        }

        session.RegistrarSaida(request.ExitTime, sector.BasePrice);
        sessionRepository.Update(session);

        await ReleaseSpotAsync(session.SpotId!.Value, spotRepository, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RegisterExitResponse(session.Id, session.ExitTime!.Value, session.AmountCharged!.Value));
    }

    private static async Task ReleaseSpotAsync(Guid spotId, ISpotRepository spotRepository, CancellationToken cancellationToken)
    {
        var spot = await spotRepository.GetByIdAsync(spotId, cancellationToken);
        if (spot is null)
            return;

        spot.Liberar();
        spotRepository.Update(spot);
    }
}
```

**Passo a passo:**

**① `GetActiveByLicensePlateAsync`** — localiza a sessão em aberto para a placa. Este é o ponto de correlação entre o evento `EXIT` (que só traz a placa) e a sessão criada pelo `ENTRY` anterior. Se não existir sessão ativa, é um cenário **esperado** (placa desconhecida, evento fora de ordem) → `Result.Failure` com `Error.NotFound`, não uma exceção.

**② `GetByCodeAsync(session.SectorCode!)`** — busca o `Sector` para obter o `BasePrice` necessário ao cálculo. O `!` (null-forgiving) é seguro aqui porque, se a sessão chegou até este ponto, ela obrigatoriamente passou por `RegistrarEstacionamento` antes (é a única forma de a sessão estar com `Status != Entrou`, e `GetActiveByLicensePlateAsync` só retorna sessões não finalizadas — logo, `Estacionado`). Um segundo `is null` é verificado mesmo assim, porque o setor pode ter sido removido/renomeado entre o `PARKED` e o `EXIT` — um cenário raro, mas tratado explicitamente em vez de deixar uma `NullReferenceException` estourar.

**③ `session.RegistrarSaida(...)`** — delega ao domínio. Se a sessão não estiver `Estacionado` (ex: um segundo `EXIT` duplicado), o domínio lança `DomainException`. **Não há `try/catch` aqui** — a exceção sobe até o `ExceptionHandlingMiddleware` da API, que a converte em HTTP 422 (ver documentação da API).

**④ `ReleaseSpotAsync`** — busca a vaga pelo `Id` interno (gravado na sessão em `RegistrarEstacionamento`) e chama `Liberar()`. Note que esta é uma **segunda mutação de estado** dentro do mesmo handler — a vaga (`Spot`) e a sessão (`ParkingSession`) são dois agregados diferentes, cada um mutado por sua própria API de domínio, mas ambos persistidos **juntos** no passo seguinte.

**⑤ `unitOfWork.SaveChangesAsync(cancellationToken)`** — persiste as duas mutações (`session.Update` e `spot.Update`) atomicamente, numa única transação implícita do EF Core. É o momento em que os `UPDATE`s realmente acontecem no SQL Server.

**Por que `ReleaseSpotAsync` é um método privado estático, e não inline no `Handle`?** Para isolar a lógica de "encontrar e liberar a vaga" com um nome que documenta sua intenção, mantendo o método `Handle` legível como uma sequência linear de passos de alto nível.

### 6.5 Como os Outros Casos de Uso Seguem o Mesmo Padrão

| Caso de Uso | Diferencial |
|-------------|-------------|
| `SyncGarage` | Sem busca por "não encontrado" — faz *upsert* (cria ou atualiza) de `Sector`/`Spot` a partir do `IGarageSimulatorClient`. Nunca retorna `Failure` de negócio (falhas de rede propagam como exceção) |
| `SeedGarage` | Mesmíssimo *upsert* de `SyncGarage`, através do método compartilhado `GarageUpsert.ApplyAsync` (ver seção 6.6) — a única diferença é que a configuração vem do **corpo da requisição**, não de `IGarageSimulatorClient`. Existe para testar o sistema sem depender do simulador externo estar disponível |
| `RegisterEntry` | Não busca nenhuma entidade existente — calcula a lotação atual (`ISectorRepository.GetTotalCapacityAsync` + `ISpotRepository.CountOccupiedAsync`) e delega a criação da sessão à fábrica do domínio. Pode lançar `DomainException` (garagem cheia) sem nunca retornar `Result.Failure` |
| `RegisterParked` | Busca a sessão ativa (`NotFound` se ausente) **e** a vaga por coordenada (`NotFound` se ausente) — dois pontos de falha esperada, como `RegisterExit`, mas nenhum acesso a um terceiro repositório (`Sector`) |
| `GetRevenue` | É uma **Query**, não um Command — não muta nada, não usa `IUnitOfWork`. Busca o setor (`NotFound` se não existir) e delega a soma ao repositório (`GetRevenueAsync`) |

### 6.6 `GarageUpsert` — Upsert Compartilhado entre `SyncGarage` e `SeedGarage`

```csharp
public static class GarageUpsert
{
    public static async Task<(int SectorsUpserted, int SpotsUpserted)> ApplyAsync(
        GarageConfigurationDto configuration,
        ISectorRepository sectorRepository,
        ISpotRepository spotRepository,
        CancellationToken cancellationToken)
    {
        // upsert de Sector por Code, upsert de Spot por ExternalId — idêntico ao que SyncGarageHandler fazia inline
    }
}
```

**Por que extrair esse método em vez de deixar a lógica duplicada em `SyncGarageHandler` e `SeedGarageHandler`?** Os dois handlers fazem exatamente a mesma operação — "dada uma `GarageConfigurationDto`, criar ou atualizar `Sector`/`Spot` no banco". A única diferença entre eles é **de onde vem** essa configuração: `SyncGarageHandler` a obtém de `IGarageSimulatorClient.GetGarageConfigurationAsync()`; `SeedGarageHandler` a recebe já pronta no `SeedGarageRequest`. Sem essa extração, uma correção de bug no upsert (ex: um ajuste na lógica de *matching* por `ExternalId`) precisaria ser replicada manualmente nos dois handlers — um risco real de divergência silenciosa entre "sincronizar" e "semear" a garagem.

**Por que `GarageUpsert` é uma classe estática, e não um serviço registrado no DI (`IGarageUpsertService`)?** Porque não tem estado nem depende de nada além dos parâmetros recebidos — os dois repositórios já são passados explicitamente pelos handlers (que os têm injetados). Introduzir uma interface e registrá-la no container seria uma indireção sem benefício real para uma função pura de orquestração como essa.

---

## 7. `AddApplication()` — Registro de Dependências

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

**`RegisterServicesFromAssembly`** — o MediatR varre o assembly da Application e registra automaticamente todos os handlers (`SyncGarageHandler`, `RegisterEntryHandler`, `RegisterParkedHandler`, `RegisterExitHandler`, `GetRevenueHandler`). Adicionar um novo caso de uso não exige tocar neste arquivo.

**`AddValidatorsFromAssembly`** — mesma auto-descoberta para todos os `AbstractValidator<T>`. Se nenhum validator existir para um dado request (nenhum caso neste projeto — todos os commands/queries têm validator), o `ValidationBehavior` recebe uma lista vazia e passa direto.

**Ordem de registro dos behaviors:** `ValidationBehavior` antes de `LoggingBehavior` no código-fonte, mas isso **não determina a ordem de execução** — quem determina é a ordem de `AddTransient` no pipeline do `IPipelineBehavior<,>`, que aqui coloca `ValidationBehavior` primeiro na lista de registro. Na prática, para este projeto, a ordem exata entre os dois é menos crítica do que no `gestao-faturas`, já que o `LoggingBehavior` aqui não distingue sucesso de falha — ele loga em ambos os casos de qualquer forma.

---

## 8. Resumo das Decisões Técnicas

| Decisão | Alternativa | Por que escolhemos assim |
|---------|-------------|--------------------------|
| MediatR como barramento | Injetar handlers diretamente | Desacoplamento total; o `WebhookController` não precisa conhecer 3 handlers diferentes, apenas despachar pelo `event_type` |
| `IUnitOfWork` explícito na Application | Repositórios chamarem `SaveChanges` internamente | Handlers controlam explicitamente quando persistir; permite compor múltiplas mutações (sessão + vaga) numa única transação |
| `IGarageSimulatorClient` definido na Application | Definir o contrato na Infrastructure | A Application dita o que precisa da integração externa; a Infrastructure decide como implementar (HttpClient + resiliência) |
| `ErrorType` como enum no `Error` | Código de erro como string livre (`"NotFound"`) | Mapeamento HTTP no controller feito por `switch` exaustivo, mais seguro contra erros de digitação |
| `Result` para "não encontrado", `DomainException` para regra de negócio | Tudo com exceções, ou tudo com Result | Cenários esperados (placa sem sessão ativa) não pagam custo de exceção; regras de negócio verdadeiras (garagem cheia) usam o mecanismo já existente no domínio |
| `RegisterExitHandler` libera a vaga além de fechar a sessão | Lógica de liberação de vaga em outro caso de uso | `EXIT` é o único evento em que a vaga deveria voltar a ficar livre — mantém a orquestração coesa num único handler |
| Validators só checam formato/presença | Validators replicarem regras de domínio | Evita duplicar lógica que já existe (e é testada) no domínio; Validators focam no que só é visível na borda HTTP |
| `GarageUpsert` extraído como método estático compartilhado | Duplicar o loop de upsert em `SyncGarageHandler` e `SeedGarageHandler` | Os dois fazem a mesma operação sobre `Sector`/`Spot`; só a origem da `GarageConfigurationDto` muda (simulador externo vs. corpo da requisição) |
| `SeedGarage` como caso de uso separado de `SyncGarage` | Fazer `SyncGarage` aceitar um corpo opcional que, se presente, pula a chamada ao simulador | Manter os dois contratos (`ICommand`) explícitos e com nomes que documentam a intenção — "sincronizar com o simulador" e "semear manualmente" são operações conceitualmente diferentes, mesmo reaproveitando o mesmo upsert |
