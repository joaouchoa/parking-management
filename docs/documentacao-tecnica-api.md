# Documentação Técnica — Camada de API (`ParkingManagement.Api`)

> **Objetivo deste documento:** explicar em profundidade cada decisão conceitual e técnica tomada na construção da camada de API do sistema de Gestão de Estacionamento. Esta camada é a fronteira do sistema com o mundo externo — e, neste projeto, "o mundo externo" tem duas direções: o **simulador da Estapar** (que chama nosso `POST /webhook`) e um cliente humano/ferramenta consultando `GET /revenue`.

---

## 1. O Papel da API na Arquitetura

```
Simulador (externo)
     │  POST /webhook  { event_type: ENTRY|PARKED|EXIT }
     ▼
ExceptionHandlingMiddleware   ← captura qualquer exceção não tratada
     │
     ▼
WebhookController              ← identifica o evento e despacha o command certo
     │
     ▼
ISender.Send(command)          ← entra no pipeline MediatR
     │
     ▼
Application + Domain + Infrastructure
```

Diferente do `gestao-faturas` (uma API convencional, request/response iniciado sempre pelo cliente), esta API tem uma característica particular: ela é, ao mesmo tempo, **cliente** (consome `GET /garage` do simulador no startup) e **servidor de webhook** (recebe eventos empurrados pelo simulador). Essa dualidade aparece diretamente na estrutura do projeto.

O que a API **não faz:**
- Regras de negócio (Domínio)
- Validação de campos (Application — `ValidationBehavior`)
- Acesso a banco de dados ou HTTP externo (Infrastructure)

---

## 2. Estrutura do Projeto

```
ParkingManagement.Api/
├── Controllers/
│   ├── WebhookController.cs           ← POST /webhook (ENTRY, PARKED, EXIT)
│   ├── RevenueController.cs           ← GET /revenue
│   └── ControllerExtensions.cs        ← ToProblem(): Error → IActionResult
├── Contracts/
│   └── Webhook/
│       └── VehicleEventEnvelope.cs    ← DTO único que cobre os 3 formatos de evento
├── HostedServices/
│   └── GarageSyncStartupService.cs    ← Sincroniza a garagem ANTES da API aceitar tráfego
├── Middlewares/
│   └── ExceptionHandlingMiddleware.cs
├── Program.cs
└── appsettings.json
```

**Por que dois controllers em vez de um só?** No `gestao-faturas`, um único `FaturasController` fazia sentido porque só existe um agregado (`Fatura`). Aqui, embora `ParkingSession` também seja o único agregado rico, os dois controllers representam **duas responsabilidades HTTP conceitualmente distintas**: `WebhookController` é um **receptor de eventos assíncronos** de um sistema externo (semântica de "notifique-me quando algo acontecer"), enquanto `RevenueController` é uma **consulta convencional** (semântica de "me diga o estado atual"). Separá-los deixa essa diferença de papel explícita na própria estrutura de pastas.

---

## 3. `Program.cs` — Composição da Aplicação

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHostedService<GarageSyncStartupService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
```

### 3.1 `AddHostedService<GarageSyncStartupService>()` — A Diferença Estrutural Mais Importante

Esta linha não existe em nenhum equivalente no `gestao-faturas`. Ela registra um **hosted service** — um componente que o ASP.NET Core inicializa junto com a aplicação, **antes** de a API começar a aceitar requisições HTTP (ver seção 6 para os detalhes de `GarageSyncStartupService`). É a implementação direta do requisito do enunciado: *"Ao iniciar a solução, busque e armazene os dados da garagem/vagas a partir de um endpoint GET /garage."*

### 3.2 Ordem dos Middlewares

```
Requisição entra
      │
      ▼
ExceptionHandlingMiddleware   ← 1°: envolve tudo abaixo num try/catch
      │
      ▼
UseHttpsRedirection
      │
      ▼
MapControllers                ← roteia para WebhookController ou RevenueController
```

Mesma lógica do projeto de referência: `ExceptionHandlingMiddleware` é o primeiro middleware customizado registrado para garantir que qualquer exceção lançada em qualquer ponto do pipeline (incluindo dentro do MediatR, dentro do domínio) seja capturada.

### 3.3 `public partial class Program;` — Visibilidade para Testes

```csharp
public partial class Program;
```

Com top-level statements (usados neste `Program.cs`), o compilador gera uma classe `Program` **`internal`** por padrão. O projeto `ParkingManagement.Integration.Tests` referencia `Program` como parâmetro genérico de `WebApplicationFactory<Program>` a partir de **outro assembly** — sem essa declaração, o build falharia com `error CS0122: 'Program' is inaccessible due to its protection level`. O modificador `partial` funde essa declaração explícita com a classe gerada implicitamente pelos top-level statements, tornando-a pública. (Sintaxe C# 12: `public partial class Program;` com `;` no lugar de `{ }` — uma classe parcial vazia não precisa de corpo.)

---

## 4. `Contracts/Webhook/VehicleEventEnvelope.cs` — Um DTO para Três Formatos

```csharp
public sealed record VehicleEventEnvelope(
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("license_plate")] string LicensePlate,
    [property: JsonPropertyName("entry_time")] DateTime? EntryTime,
    [property: JsonPropertyName("exit_time")] DateTime? ExitTime,
    [property: JsonPropertyName("lat")] double? Lat,
    [property: JsonPropertyName("lng")] double? Lng
);

public static class VehicleEventTypes
{
    public const string Entry = "ENTRY";
    public const string Parked = "PARKED";
    public const string Exit = "EXIT";
}
```

**Por que um único DTO com campos nullable, em vez de três DTOs (`EntryEventDto`, `ParkedEventDto`, `ExitEventDto`) mapeados individualmente?** O enunciado define um **único endpoint** (`POST /webhook`) que aceita três formatos de payload diferentes, discriminados pelo campo `event_type`. O `[ApiController]` faz o model binding **antes** de o controller saber qual `event_type` está chegando — não é possível decidir qual DTO usar no binding automático do ASP.NET Core sem um discriminador já lido. A solução mais simples e direta é um envelope único com todos os campos possíveis como nullable, e o controller lê `EventType` para decidir o que fazer com os demais campos (ver seção 5.1).

Uma alternativa considerada e descartada foi criar três `record` distintos (`EntryEventDto`, `ParkedEventDto`, `ExitEventDto`) e converter manualmente — mas, como o binding já precisa ocorrer contra um único tipo antes de sabermos o `event_type`, esses três DTOs ficariam sem uso real (dead code), então foram removidos em favor do envelope único.

**Por que `[property: JsonPropertyName("...")]` em vez de configurar uma `JsonNamingPolicy` global?** Os campos do payload do desafio são `snake_case` (`license_plate`, `entry_time`), enquanto o restante da aplicação usa `PascalCase`/`camelCase` (`System.Text.Json` já converte para `camelCase` por padrão nas respostas). Anotar campo a campo com `JsonPropertyName` é mais explícito e não afeta a serialização de nenhum outro DTO do sistema — uma política de nomenclatura global mudaria o comportamento de toda resposta JSON da API, incluindo `GetRevenueResponse`, que deve continuar em `camelCase` (`amount`, `currency`, `timestamp`) conforme o exemplo do enunciado.

---

## 5. `WebhookController` — O Endpoint Mais Importante do Sistema

```csharp
[ApiController]
[Route("webhook")]
public sealed class WebhookController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] VehicleEventEnvelope envelope, CancellationToken cancellationToken)
    {
        return envelope.EventType.ToUpperInvariant() switch
        {
            VehicleEventTypes.Entry => await HandleEntryAsync(envelope, cancellationToken),
            VehicleEventTypes.Parked => await HandleParkedAsync(envelope, cancellationToken),
            VehicleEventTypes.Exit => await HandleExitAsync(envelope, cancellationToken),
            _ => Problem(
                title: "event_type desconhecido",
                detail: $"O evento '{envelope.EventType}' não é suportado. Use ENTRY, PARKED ou EXIT.",
                statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private async Task<IActionResult> HandleEntryAsync(VehicleEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var request = new RegisterEntryRequest(envelope.LicensePlate, envelope.EntryTime ?? default);
        var result = await sender.Send(request, cancellationToken);
        return result.IsSuccess ? Ok() : this.ToProblem(result.Error);
    }
    // HandleParkedAsync e HandleExitAsync seguem o mesmo padrão
}
```

### 5.1 O `switch` de Despacho por `event_type`

**Por que um único endpoint despachando internamente, em vez de três rotas (`POST /webhook/entry`, `/webhook/parked`, `/webhook/exit`)?** Porque o enunciado do desafio define explicitamente um único endpoint `POST /webhook` que recebe os três tipos de evento no mesmo caminho, discriminados pelo corpo (`event_type`). A API precisa aderir a esse contrato — não é uma escolha de design livre, é um requisito externo (o simulador só conhece essa rota).

**`envelope.EventType.ToUpperInvariant()`** — normaliza o discriminador antes de comparar, tornando o dispatch tolerante a variações de caixa (`"entry"`, `"Entry"`, `"ENTRY"` funcionam identicamente) sem exigir que o simulador siga uma convenção rígida de maiúsculas.

**O `_ => Problem(...)` no `switch`** — trata qualquer `event_type` fora dos três esperados como erro do cliente (400), **sem sequer acionar o MediatR**. Essa é uma validação de "roteamento de evento", não uma validação de dados de um comando específico — por isso fica no controller, e não em um `FluentValidation` da Application (que só valida requests já identificados).

### 5.2 Cada Handler Privado Segue o Mesmo Micro-Padrão

```csharp
private async Task<IActionResult> HandleParkedAsync(VehicleEventEnvelope envelope, CancellationToken cancellationToken)
{
    var request = new RegisterParkedRequest(envelope.LicensePlate, envelope.Lat ?? default, envelope.Lng ?? default);
    var result = await sender.Send(request, cancellationToken);
    return result.IsSuccess ? Ok() : this.ToProblem(result.Error);
}
```

1. Monta o Request específico da Application a partir dos campos relevantes do envelope (ignorando os campos que não pertencem àquele tipo de evento — ex: `EntryHandler` não olha `Lat`/`Lng`).
2. Envia via `ISender.Send`.
3. Traduz o `Result` em `IActionResult`: sucesso vira `200 OK` sem corpo (o enunciado só exige "HTTP 200 em caso de sucesso" para os três eventos — nenhum corpo de resposta é especificado), falha vira `this.ToProblem(result.Error)`.

**Por que `envelope.Lat ?? default` (equivalente a `0.0`) em vez de retornar erro se `Lat` vier nulo?** Porque a **validação de presença** desses campos já é responsabilidade do `RegisterParkedValidator` da Application (que roda no pipeline do MediatR, ver documentação de Application) — o controller não deve duplicar essa checagem. Se o `Lat` realmente for necessário e ausente, o fluxo mais correto seria o validator rejeitar; hoje o validator do projeto foca em `LicensePlate` (ver documentação de Application, seção 6.3, para o padrão idêntico em `RegisterExit`), então o `?? default` é uma salvaguarda sintática para satisfazer o tipo não-nulo do Request sem lançar exceção de null aqui no controller.

### 5.3 `ControllerExtensions.ToProblem` — Tradução do Result Pattern

```csharp
public static class ControllerExtensions
{
    public static IActionResult ToProblem(this ControllerBase controller, Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return controller.Problem(title: error.Code, detail: error.Message, statusCode: statusCode);
    }
}
```

**Por que um *extension method* compartilhado, e não um método privado em cada controller?** `WebhookController` e `RevenueController` precisam da mesma tradução `Error → IActionResult`. Um extension method sobre `ControllerBase` evita duplicar esse `switch` em dois lugares — qualquer novo `ErrorType` adicionado no futuro é tratado uma única vez.

**Por que o `switch` tem um `_ => 422` como padrão, em vez de `500`?** Porque `Error` (ver documentação de Application) só é construído explicitamente pelos Handlers para representar falhas de negócio já conhecidas (`NotFound`, `Validation`, `Conflict`, `Failure`) — nunca para bugs inesperados. Um `ErrorType.Failure` sem mapeamento específico ainda é, por definição, um erro de negócio tratado, então `422 Unprocessable Entity` é semanticamente mais apropriado que `500`, que fica reservado para exceções realmente não tratadas (capturadas pelo middleware, seção 7).

---

## 6. `RevenueController` — A Consulta

```csharp
[ApiController]
[Route("revenue")]
public sealed class RevenueController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string sector,
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetRevenueRequest(sector, date), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error);
    }
}
```

**Decisão de design documentada no `PLAN.md`: `[FromQuery]` em vez de corpo JSON no `GET`.** O enunciado ilustra `GET /revenue` com um exemplo de "Request" em formato JSON (`{ "date": "2025-01-01", "sector": "A" }`), o que é atípico para o verbo HTTP `GET` — HTTP desaconselha corpo em requisições `GET` (não há garantia de que proxies e intermediários o preservem). A implementação expõe os mesmos dois campos como **query string** (`GET /revenue?sector=A&date=2025-01-01`), aderindo às convenções REST. Essa é uma divergência **deliberada e documentada** em relação ao exemplo literal do enunciado — se o avaliador automatizado exigir corpo no `GET`, é o primeiro ponto a revisar.

**`DateOnly` como tipo do parâmetro `date`** — desde o .NET 6, o ASP.NET Core faz o binding de query string diretamente para `DateOnly` (formato `yyyy-MM-dd`), sem necessidade de conversão manual a partir de `string`/`DateTime`. Isso também documenta, pela própria assinatura do endpoint, que a hora do dia é irrelevante para este parâmetro — só a data importa, coerente com a regra de negócio (receita **por dia**).

---

## 7. `ExceptionHandlingMiddleware` — Tratamento Centralizado de Erros

```csharp
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Falha de validação",
                string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)));
        }
        catch (DomainException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity, "Regra de negócio violada", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro não tratado ao processar a requisição {Path}", context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "Erro interno",
                "Ocorreu um erro inesperado. Tente novamente mais tarde.");
        }
    }
}
```

### 7.1 Mapeamento de Exceções para HTTP

| Exceção | HTTP | Motivo |
|---------|------|--------|
| `ValidationException` (FluentValidation) | **400 Bad Request** | Dados de entrada inválidos — problema do remetente (simulador ou cliente de `/revenue`) |
| `DomainException` | **422 Unprocessable Entity** | Dados sintaticamente válidos, mas violam uma regra de negócio (ex: `ParkingSession.RegistrarSaida` chamada numa sessão que não está `Estacionado`) |
| `Exception` genérica | **500 Internal Server Error** | Erro inesperado — logado com `LogError`, mensagem genérica ao cliente |

**Por que `DomainException` não é logada como erro (`LogError`), mas a exceção genérica sim?** Uma `DomainException` representa uma tentativa de operação inválida — um evento de webhook fora de ordem, uma segunda saída para a mesma placa. Isso é **esperado** em um sistema que recebe eventos de uma fonte externa fora do nosso controle, e logar cada ocorrência como "erro" poluiria os logs de produção com ruído que não indica um bug real. O log de erro fica reservado para situações genuinamente inesperadas (falha de conexão com o banco, bug não previsto).

### 7.2 `ProblemDetails` Simplificado (sem `ValidationProblemDetails`)

```csharp
var problemDetails = new ProblemDetails
{
    Status = statusCode,
    Title = title,
    Detail = detail
};

await context.Response.WriteAsJsonAsync(problemDetails);
```

**Diferença em relação ao `gestao-faturas`:** o projeto de referência usa `ValidationProblemDetails` (com um dicionário `errors` por campo) especificamente para erros 400 de validação. Aqui, os erros de validação (`ValidationException.Errors`) são concatenados numa única string (`string.Join(" ", ...)`) dentro de um `ProblemDetails` comum. Essa simplificação é adequada ao escopo do desafio — os DTOs de entrada (`RegisterEntryRequest`, etc.) têm poucos campos e validações simples, e o consumidor principal da API é um simulador automatizado, não um formulário de frontend que se beneficiaria de um mapeamento erro-por-campo estruturado.

---

## 8. `HostedServices/GarageSyncStartupService.cs` — Sincronização no Startup

```csharp
public sealed class GarageSyncStartupService(
    IServiceProvider serviceProvider,
    ILogger<GarageSyncStartupService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        try
        {
            var result = await sender.Send(new SyncGarageRequest(), cancellationToken);

            if (result.IsFailure)
            {
                LogSyncIndisponivel(result.Error.Message);
                return;
            }

            logger.LogInformation(
                "Garagem sincronizada: {Sectors} setores e {Spots} vagas.",
                result.Value.SectorsSynced, result.Value.SpotsSynced);
        }
        catch (Exception ex)
        {
            LogSyncIndisponivel(ex.Message);
        }
    }

    private void LogSyncIndisponivel(string motivo) =>
        logger.LogWarning(
            "Não foi possível sincronizar a configuração da garagem com o simulador ({Motivo}). " +
            "A API vai subir mesmo assim, mas /webhook e /revenue vão falhar até a sincronização " +
            "ser refeita manualmente via POST /garage/sync.",
            motivo);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Este é o componente que implementa literalmente o primeiro requisito funcional do enunciado: *"Ao iniciar a solução, busque e armazene os dados da garagem/vagas a partir de um endpoint GET /garage."*

**Por que `IHostedService` (interface "crua") em vez de herdar de `BackgroundService`?** `BackgroundService` é pensado para tarefas de **longa duração em segundo plano** (ex: um worker que fica processando uma fila indefinidamente) — seu `ExecuteAsync` não bloqueia o startup do host. `IHostedService.StartAsync`, por outro lado, **é aguardado (`await`) pelo host antes de a aplicação terminar de subir**. Como a sincronização da garagem precisa **necessariamente** terminar antes de o webhook começar a aceitar eventos (não faria sentido receber um `ENTRY` sem nenhum setor/vaga cadastrado), `IHostedService` é a escolha correta — garante ordem, não apenas "dispara e esquece".

**Por que `serviceProvider.CreateScope()` em vez de injetar `ISender` diretamente no construtor?** `GarageSyncStartupService` é registrado como serviço singleton pelo `AddHostedService` (o padrão do ASP.NET Core para hosted services). `ISender`/`MediatR` e os repositórios por trás dele (via `ParkingDbContext`) são `Scoped` — não podem ser injetados diretamente num singleton (o container lançaria uma exceção de validação de escopo, ou pior, manteria uma única instância de `DbContext` viva pela vida inteira da aplicação). Criar um `scope` explícito resolve as dependências `Scoped` corretamente, com descarte automático ao final do `using`.

### 8.1 De fail-fast para não-fatal — uma mudança de política deliberada

Na primeira versão deste serviço, uma falha na sincronização chamava `IHostApplicationLifetime.StopApplication()` — a API encerrava a si mesma no startup. Era uma política de **fail-fast** consciente: sem a configuração da garagem, não é possível validar capacidade nem calcular preços corretamente, então "melhor falhar alto e cedo do que aceitar tráfego incorreto".

**Essa política foi revertida assim que o cenário real do desafio ficou claro: o enunciado (`Teste .NET.docx`) não vem acompanhado de nenhum endereço, imagem ou instrução de acesso ao simulador real da Estapar — nem o candidato, nem quem avalia a entrega necessariamente têm o simulador disponível.** Com o fail-fast antigo, rodar a API sem o simulador de pé (o cenário mais provável para quem for avaliar o teste) derrubava a aplicação **antes mesmo do Swagger abrir** — nada era inspecionável, mesmo com o código inteiro correto.

A troca (seção 8, código acima): qualquer falha ao sincronizar — seja um `Result.Failure` de negócio, seja uma exceção de rede (`try/catch` em volta de toda a chamada, já que uma falha de conexão com um sistema totalmente fora do ar propagaria como exceção não tratada, não como `Result.Failure`) — vira um `LogWarning` e a `StartAsync` simplesmente retorna. A API sobe normalmente, o Swagger fica navegável, e os endpoints que dependem de dados sincronizados (`/webhook`, `/revenue`) falham de forma explícita (404 "setor não encontrado") até alguém rodar a sincronização manualmente — o que nos leva à seção 8.2.

**Por que capturar `Exception` genérica (normalmente um code smell) é aceitável aqui?** Porque a intenção é explícita e única: nenhuma falha de um sistema externo, de qualquer natureza, deve derrubar o processo inteiro no startup. Não é um "engolir erros silenciosamente" — o erro é logado como `Warning` com o motivo, e o restante do sistema permanece diagnosticável via `POST /garage/sync`.

---

## 8.2 `Controllers/GarageController.cs` — Sincronização Manual e Seed de Teste

```csharp
[ApiController]
[Route("garage")]
public sealed class GarageController(ISender sender) : ControllerBase
{
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SyncGarageRequest(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error);
    }

    [HttpPost("seed")]
    public async Task<IActionResult> Seed(SeedGarageRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error);
    }
}
```

Este controller tem dois endpoints com propósitos deliberadamente diferentes — vale explicar cada um e, principalmente, por que os dois existem lado a lado.

### 8.2.1 `POST /garage/sync` — Repetir a Sincronização Real

**O que este endpoint resolve, na prática:** sem ele, a única forma de tentar sincronizar a garagem era reiniciar a API inteira (para o `GarageSyncStartupService` rodar de novo). Isso é particularmente incômodo justamente no cenário sem simulador real: você sobe um stub local (ver `docs/guia-testes-manuais.md`) *depois* da API já estar de pé, e precisaria reiniciá-la só para a sincronização ser tentada de novo. Com `POST /garage/sync`, dá para acionar a sincronização a qualquer momento pelo próprio Swagger, sem derrubar nada.

**Por que não criei um novo Command na Application?** Não precisei — `SyncGarageRequest`/`SyncGarageHandler` já existiam desde o início (é o mesmo caso de uso que o `GarageSyncStartupService` chama). Este controller é apenas uma **segunda porta de entrada HTTP** para um caso de uso que já existia, exatamente como `RevenueController` é uma porta de entrada fina sobre `GetRevenueRequest`. Nenhuma lógica de negócio nova foi criada — só uma forma manual de acionar o que já existia.

**Este endpoint ainda depende do simulador externo (`IGarageSimulatorClient`)** — se `Simulator:BaseUrl` não estiver respondendo, `POST /garage/sync` falha exatamente como o `GarageSyncStartupService` falharia no boot (mesmo `try/catch` interno do handler, mesma propagação de erro).

### 8.2.2 `POST /garage/seed` — Popular a Garagem Sem Depender de Nada Externo

**O problema que este endpoint resolve:** o ambiente de avaliação/desenvolvimento deste desafio não tem o simulador real da Estapar disponível. Testar o fluxo completo (`ENTRY` → `PARKED` → `EXIT` → `GET /revenue`) manualmente exigia, até este endpoint existir, subir um stub HTTP à parte (um script PowerShell rodando `GET /garage` num terceiro terminal — ver `docs/guia-testes-manuais.md`) só para que `POST /garage/sync` (ou o `GarageSyncStartupService`) tivesse algo para consumir.

`POST /garage/seed` recebe a mesma forma de payload que `GET /garage` do simulador retornaria (`{ "garage": [...], "spots": [...] }` — o `SeedGarageRequest` reaproveita literalmente `GarageSectorDto`/`GarageSpotDto` da Application) **direto no corpo da requisição**, e aplica o mesmo *upsert* de `SyncGarage` (via `GarageUpsert.ApplyAsync`, ver `docs/documentacao-tecnica-application.md`, seção 6.6) — sem nenhuma chamada HTTP de saída.

**Por que reaproveitar `GarageSectorDto`/`GarageSpotDto` em vez de criar um novo formato de request?** Porque esses DTOs já espelham exatamente o contrato de `GET /garage` documentado no enunciado do desafio (`Teste .NET.docx`) — reaproveitá-los significa que o mesmo corpo JSON que você usaria para testar um stub de simulador funciona, sem alteração, como corpo de `POST /garage/seed`. Um novo formato só introduziria uma segunda forma de representar a mesma coisa, sem ganho.

**Este endpoint não faz parte dos requisitos do desafio (RN-1 continua sendo `GET /garage` + sincronização automática no boot).** Ele é uma facilidade de teste, análoga a um *test seam*: existe para reduzir o atrito de testar manualmente sem o simulador, não para substituir a sincronização automática, que continua sendo a implementação "oficial" de RN-1.

### 8.2.3 Por Que os Dois Endpoints Convivem no Mesmo Controller

`GarageController` concentra hoje **todas as operações que escrevem na configuração da garagem** (`Sector`/`Spot`), venha o dado de onde vier — do simulador externo (`sync`) ou de um corpo de requisição manual (`seed`). Os eventos do webhook (`ENTRY`/`PARKED`/`EXIT`) continuam em `WebhookController`, porque leem/mutam `ParkingSession`, um agregado diferente. Se no futuro surgir uma terceira forma de popular a garagem (ex: importar um CSV), este é o controller onde ela entraria.

---

## 9. `appsettings.json`

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=localhost,1433;Database=ParkingManagement;User Id=sa;Password=Your_password123;TrustServerCertificate=True;"
  },
  "Simulator": {
    "BaseUrl": "http://localhost:3000"
  }
}
```

**A seção `Simulator:BaseUrl` não existe em nenhum equivalente do `gestao-faturas`** — é a URL do serviço externo que expõe `GET /garage` e para onde o simulador enviará eventos de volta ao nosso `POST /webhook`. Como o enunciado não fornece a imagem/URL definitiva do simulador da Estapar, este valor é um placeholder documentado no `PLAN.md` (seção de premissas), a ser ajustado assim que o ambiente real do simulador for conhecido — nunca hardcoded no código-fonte, sempre vindo de configuração sobrescritível por variável de ambiente (`Simulator__BaseUrl` no `docker-compose`, por exemplo).

---

## 10. Tabela Completa de Endpoints

| Método | Rota | Command/Query | Sucesso | Erro Negócio | Não Encontrado |
|--------|------|----------------|---------|---------------|-----------------|
| `POST` | `/webhook` (`event_type: ENTRY`) | `RegisterEntryRequest` | 200 | 400 (validação) / 422 (garagem cheia) | — |
| `POST` | `/webhook` (`event_type: PARKED`) | `RegisterParkedRequest` | 200 | 400 (validação) | 404 (sessão ou vaga) |
| `POST` | `/webhook` (`event_type: EXIT`) | `RegisterExitRequest` | 200 | 400/422 | 404 (sessão ou setor) |
| `POST` | `/webhook` (`event_type` desconhecido) | — | — | 400 | — |
| `GET` | `/revenue` | `GetRevenueRequest` | 200 | 400 (validação) | 404 (setor) |
| `POST` | `/garage/sync` | `SyncGarageRequest` | 200 | 500 (simulador inacessível) | — |
| `POST` | `/garage/seed` | `SeedGarageRequest` | 200 | 400 (validação) | — |

---

## 11. Diagrama de Dependências

```
┌───────────────────────────────────────────────────────────────────┐
│                         ParkingManagement.Api                      │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐      │
│  │  Program.cs                                                │      │
│  │  AddApplication() / AddInfrastructure()                   │      │
│  │  AddHostedService<GarageSyncStartupService>()             │      │
│  │  UseMiddleware<ExceptionHandlingMiddleware>()              │      │
│  │  UseSwagger()/UseSwaggerUI() [Development] · MapControllers│      │
│  └──────────────────────────────────────────────────────────┘      │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐      │
│  │  HostedServices/GarageSyncStartupService                  │      │
│  │  Roda ANTES da API aceitar tráfego                        │      │
│  │  Falha → LogWarning, API sobe mesmo assim (não-fatal)     │      │
│  └──────────────────────────────────────────────────────────┘      │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐      │
│  │  ExceptionHandlingMiddleware                               │      │
│  │  ValidationException → 400 · DomainException → 422         │      │
│  │  Exception → 500 + LogError                                │      │
│  └──────────────────────────────────────────────────────────┘      │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐      │
│  │  WebhookController   RevenueController   GarageController │      │
│  │  POST /webhook       GET /revenue        POST /garage/sync│      │
│  │  → ENTRY/PARKED/EXIT → GetRevenueRequest → SyncGarageRequest│     │
│  └──────────────────────────────────────────────────────────┘      │
│                                                                     │
│  Depende de:                                                        │
│  ├── ParkingManagement.Application (commands, queries, ISender)    │
│  ├── ParkingManagement.Infrastructure (AddInfrastructure)          │
│  └── Swashbuckle.AspNetCore 6.6.2                                  │
└───────────────────────────────────────────────────────────────────┘
```

---

## 12. Resumo das Decisões Técnicas

| Decisão | Alternativa | Por que escolhemos assim |
|---------|-------------|--------------------------|
| Dois controllers (`Webhook`, `Revenue`) | Um único controller | Papéis HTTP conceitualmente distintos — receptor de eventos vs. consulta convencional |
| Um `VehicleEventEnvelope` único e nullable | Três DTOs mapeados individualmente | O `event_type` só é conhecido após o model binding; DTOs específicos ficariam sem uso real |
| `[JsonPropertyName]` por campo | `JsonNamingPolicy` global `snake_case` | Isola o formato externo do simulador sem afetar a serialização de outras respostas da API (`camelCase`) |
| `GarageSyncStartupService` como `IHostedService` puro | `BackgroundService` | Precisa ser **aguardado** antes do host terminar de subir — `BackgroundService` não bloqueia o startup |
| Falha na sincronização apenas loga warning e segue | Fail-fast (`lifetime.StopApplication()`) | Sem o simulador real disponibilizado pelo enunciado, fail-fast derrubava a API antes do Swagger abrir — pior para quem for avaliar sem ter o simulador do que subir parcialmente funcional |
| `POST /garage/sync` reexpõe `SyncGarageRequest` já existente | Só reiniciar a API para tentar sincronizar de novo | Permite repetir a sincronização a qualquer momento (ex: depois de subir um stub local) sem derrubar a aplicação |
| `POST /garage/seed` como endpoint separado, sem chamar o simulador | Exigir sempre um stub externo (real ou PowerShell) respondendo `GET /garage` para qualquer teste manual | Sem o simulador da Estapar disponível no ambiente do desafio, testar o fluxo completo end-to-end exigia manter um terceiro processo (stub) rodando; `/garage/seed` elimina essa dependência para testes manuais, sem mudar como RN-1 é atendida |
| `GET /revenue` com `[FromQuery]` | Corpo JSON no `GET` (como no exemplo do enunciado) | Aderência às convenções HTTP/REST; decisão documentada e reversível se o avaliador exigir o formato literal |
| `ProblemDetails` simples (sem `errors` por campo) | `ValidationProblemDetails` (como no `gestao-faturas`) | Payloads de entrada simples; simulador automatizado não se beneficia de um mapeamento erro-por-campo estruturado |
| `422` como fallback de `ToProblem` | `500` como fallback | `Error` só é construído para falhas de negócio já conhecidas pelos Handlers, nunca para bugs |
| `public partial class Program;` | Projeto de testes referenciar a Api de outra forma | Padrão idiomático do ASP.NET Core para `WebApplicationFactory<Program>` em outro assembly |
