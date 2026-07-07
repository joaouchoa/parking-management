# Documentação Técnica — Camada de Domínio (`ParkingManagement.Domain`)

> **Objetivo deste documento:** explicar, em profundidade, cada decisão conceitual e técnica tomada na construção da camada de domínio do sistema de Gestão de Estacionamento. Serve como referência para qualquer desenvolvedor que precise entender o "porquê" por trás do código, não apenas o "o quê".

---

## 1. A Filosofia por trás do Domínio

### 1.1 O que é "Domínio"?

Em sistemas com **Clean Architecture + DDD (Domain-Driven Design)**, o **Domínio** é o coração da aplicação. Ele contém apenas as **regras de negócio puras** do desafio da Estapar — controle de vagas, cálculo de tarifa dinâmica, cobrança na saída — sem saber nada sobre banco de dados, HTTP, webhook ou qualquer tecnologia externa.

> Pense no domínio como as regras escritas no papel pelo operador de um estacionamento — elas existem independentemente de qualquer sistema.

A camada de domínio **não tem dependência de nenhum outro projeto** do sistema. Ela é autossuficiente. Isso é visível no `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <!-- nenhuma ProjectReference aqui -->
  </ItemGroup>
</Project>
```

### 1.2 Domínio Rico vs. Domínio Anêmico

| Abordagem | Descrição | Problema |
|-----------|-----------|----------|
| **Anêmico** | Entidade é só um "saco de dados" com getters e setters públicos. A lógica fica espalhada em services. | Qualquer lugar do código pode alterar o estado da entidade de forma indevida. |
| **Rico** ✅ | Entidade encapsula seus dados e expõe apenas métodos que fazem sentido de negócio. | Nenhum — é a abordagem correta para DDD. |

**Este projeto usa Domínio Rico.** `ParkingSession` não tem `set` público em nenhuma propriedade. Só os próprios métodos (`IniciarEntrada`, `RegistrarEstacionamento`, `RegistrarSaida`) podem mudar seu estado. Isso garante que uma sessão nunca chegue, por exemplo, a ter `AmountCharged` preenchido sem ter passado por `Estacionado` antes.

---

## 2. Estrutura de Arquivos e o Papel de Cada Um

```
ParkingManagement.Domain/
├── Common/                              ← Blocos de construção genéricos (reutilizáveis)
│   ├── Entity.cs
│   ├── AggregateRoot.cs
│   ├── ValueObject.cs
│   ├── DomainException.cs
│   ├── IDomainEvent.cs
│   └── ValueObjects/
│       └── GeoCoordinate.cs             ← VO compartilhado entre Garage e Parking
│
├── Garage/                              ← Contexto "configuração da garagem"
│   ├── Sector.cs                        ← Entity — setor sincronizado do simulador
│   ├── Spot.cs                          ← Entity — vaga física
│   ├── SpotStatus.cs                    ← Enum (Livre, Ocupada)
│   ├── Repositories/
│   │   ├── ISectorRepository.cs
│   │   └── ISpotRepository.cs
│   └── Errors/
│       └── GarageErrors.cs
│
└── Parking/                             ← Contexto "ciclo de vida do veículo"
    ├── ParkingSession.cs                ← Aggregate Root
    ├── ParkingSessionStatus.cs          ← Enum (Entrou, Estacionado, Finalizado)
    ├── ValueObjects/
    │   ├── LicensePlate.cs
    │   └── PricingSnapshot.cs
    ├── Events/
    │   ├── VehicleEnteredEvent.cs
    │   ├── VehicleParkedEvent.cs
    │   └── VehicleExitedEvent.cs
    ├── Repositories/
    │   └── IParkingSessionRepository.cs
    └── Errors/
        └── ParkingSessionErrors.cs
```

**Por que dois contextos (`Garage/` e `Parking/`) em vez de um só?** `Garage` representa dados de **configuração**, sincronizados de um sistema externo (o simulador) — setores e vagas raramente mudam de estado por conta própria. `Parking` representa o **comportamento de negócio** central do desafio — o ciclo de vida de um veículo. Separar os dois deixa claro que `ParkingSession` é o agregado rico do sistema, enquanto `Sector`/`Spot` são coadjuvantes com invariantes simples.

---

## 3. Os Blocos de Construção (`Common/`)

Estes conceitos são idênticos, em espírito, a qualquer projeto DDD — são as peças que toda entidade e agregado do sistema reaproveita.

### 3.1 `Entity.cs` — O que é uma Entidade?

```csharp
public abstract class Entity
{
    public Guid Id { get; protected init; }

    protected Entity() => Id = Guid.NewGuid();
    protected Entity(Guid id) => Id = id;

    public override bool Equals(object? obj) { ... }
    public override int GetHashCode() => Id.GetHashCode();
}
```

**Conceito:** Uma **Entidade** é um objeto com **identidade única** — dois objetos com os mesmos dados mas IDs diferentes são objetos diferentes.

**Por que `protected init` em vez de `private set`?** `init` permite que o `Id` seja atribuído apenas na construção (pelo próprio construtor ou por uma subclasse), nunca depois. É uma garantia ainda mais forte que `private set`: nem o próprio objeto pode mudar seu `Id` após criado.

**Por que sobrescrever `Equals`/`GetHashCode`?** Duas entidades com o mesmo `Id` devem ser consideradas iguais, independentemente do estado interno — é a semântica de identidade do DDD, diferente da igualdade por valor dos Value Objects.

### 3.2 `AggregateRoot.cs` — O que é uma Raiz de Agregado?

```csharp
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

**No nosso sistema:** `ParkingSession` é a única raiz de agregado com essa capacidade. `Sector` e `Spot` herdam apenas de `Entity` — eles não acumulam eventos porque não representam uma transição de negócio complexa, apenas dados de configuração e um estado binário (livre/ocupada).

**Por que `ParkingSession` acumula eventos mesmo sem nenhum handler ainda os consumir?** Para deixar o design **extensível** sem custo: se amanhã for necessário notificar um sistema de billing quando um veículo sai (`VehicleExitedEvent`), a infraestrutura de captura já existe — só falta publicar os eventos após `SaveChangesAsync`.

### 3.3 `ValueObject.cs` — O que é um Value Object?

```csharp
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other) { ... }
    public override int GetHashCode() { ... }
    public static bool operator ==(...) { ... }
}
```

| | Entidade | Value Object |
|---|---|---|
| Identidade | Tem `Id` único | Não tem `Id` |
| Igualdade | Comparada pelo `Id` | Comparada pelos valores |
| Mutabilidade | Pode mudar (com cuidado) | **Imutável** |
| Exemplo | `ParkingSession`, `Spot` | `LicensePlate`, `GeoCoordinate`, `PricingSnapshot` |

**Por que `IEquatable<ValueObject>` explícito e não só sobrescrever `Equals(object?)`?** Implementar a interface genérica evita boxing em comparações tipadas e deixa explícito, para o compilador e para quem lê o código, que o tipo tem uma noção de igualdade estrutural bem definida.

### 3.4 `DomainException.cs` — Exceções de Negócio

```csharp
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
```

Quando uma **regra de negócio é violada** — por exemplo, tentar registrar entrada com a garagem 100% cheia — o domínio lança `DomainException`. A API mapeia isso para **HTTP 422** no `ExceptionHandlingMiddleware` (ver documentação da camada de API).

### 3.5 `IDomainEvent.cs`

```csharp
public interface IDomainEvent
{
    DateTime OccurredOn { get; }
}
```

**Diferença em relação ao `gestao-faturas`:** aqui a interface já exige `OccurredOn`. Isso porque, num sistema de estacionamento, a **ordem temporal dos eventos** (entrada, estacionamento, saída) é intrinsecamente relevante para auditoria e billing — faz sentido que todo evento já carregue seu timestamp por contrato, em vez de deixar essa responsabilidade a cada implementação.

### 3.6 `GeoCoordinate.cs` — Value Object Compartilhado

```csharp
public sealed class GeoCoordinate : ValueObject
{
    public double Latitude { get; }
    public double Longitude { get; }

    public static GeoCoordinate Criar(double latitude, double longitude) => new(latitude, longitude);
}
```

**Por que fica em `Common/ValueObjects` e não dentro de `Garage/` ou `Parking/`?** Porque é usado nos dois contextos: `Spot.Coordinate` (a vaga tem uma posição fixa) e `ParkingSession.ParkedCoordinate` (a posição informada pelo evento `PARKED`). Colocá-lo em `Common` evita que `Garage` dependa de `Parking` (ou vice-versa) só para reaproveitar um VO de coordenadas — os dois contextos dependem apenas de `Common`, nunca um do outro.

---

## 4. O Contexto `Garage/` — Configuração da Garagem

### 4.1 `Sector.cs` — Entity de Configuração

```csharp
public sealed class Sector : Entity
{
    public string Code { get; private set; } = null!;
    public decimal BasePrice { get; private set; }
    public int MaxCapacity { get; private set; }

    public static Sector Criar(string code, decimal basePrice, int maxCapacity) { ... }
    public void AtualizarConfiguracao(decimal basePrice, int maxCapacity) { ... }
}
```

**Por que `Sector` não é um Aggregate Root?** Porque não tem invariantes complexas nem coleções internas para proteger — é essencialmente um espelho local de um recurso externo (`GET /garage` do simulador). A única regra é "preço base e capacidade devem ser positivos", verificada tanto na criação quanto na atualização.

**`AtualizarConfiguracao` — por que existe um método de atualização em vez de recriar o `Sector`?** Porque a sincronização (`SyncGarage`, ver documentação de Application) roda periodicamente/no startup: se o setor já existe, seus dados devem ser **atualizados in-place** (preservando o `Id` interno, que outras tabelas referenciam), nunca substituídos por um novo registro.

### 4.2 `Spot.cs` — Entity com Estado

```csharp
public sealed class Spot : Entity
{
    public long ExternalId { get; private set; }
    public string SectorCode { get; private set; } = null!;
    public GeoCoordinate Coordinate { get; private set; } = null!;
    public SpotStatus Status { get; private set; } = SpotStatus.Livre;

    public void Ocupar() { ... }
    public void Liberar() { ... }
}
```

**`ExternalId` vs `Id`:** `Id` é o `Guid` interno (identidade do nosso sistema). `ExternalId` é o `long` que o simulador usa para identificar a vaga (`"id": 1` no JSON de `GET /garage`). Manter os dois separados evita acoplar nossa chave primária a um identificador de um sistema externo que não controlamos.

**`Ocupar()` / `Liberar()` — guards simétricos:**

```csharp
public void Ocupar()
{
    if (Status == SpotStatus.Ocupada)
        throw new DomainException(GarageErrors.VagaJaOcupada);
    Status = SpotStatus.Ocupada;
}
```

Cada método só permite a transição a partir do estado oposto. Isso é o que impede, por exemplo, que um bug no handler de `PARKED` tente ocupar duas vezes a mesma vaga sem que ninguém perceba — o domínio recusa a operação com uma mensagem clara em vez de silenciosamente sobrescrever o estado.

### 4.3 Os Repositórios (`ISectorRepository`, `ISpotRepository`)

```csharp
public interface ISpotRepository
{
    Task<Spot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Spot?> GetByExternalIdAsync(long externalId, CancellationToken cancellationToken = default);
    Task<Spot?> FindByCoordinateAsync(GeoCoordinate coordinate, CancellationToken cancellationToken = default);
    Task AddAsync(Spot spot, CancellationToken cancellationToken = default);
    Task<int> CountOccupiedAsync(CancellationToken cancellationToken = default);
    Task<int> CountOccupiedBySectorAsync(string sectorCode, CancellationToken cancellationToken = default);
    void Update(Spot spot);
}
```

**`FindByCoordinateAsync` é o método mais importante desta interface.** É ele que resolve o maior desafio de correlação do domínio: o evento `PARKED` chega com `lat`/`lng`, não com um `spotId`. O domínio define o contrato de "encontre a vaga por coordenada"; a Infrastructure decide **como** (match exato — ver documentação de Infrastructure).

**`CountOccupiedAsync` / `CountOccupiedBySectorAsync`** existem porque o cálculo de lotação (RN-4, ver seção 5.4) precisa de um agregado (contagem), não de uma entidade específica — típico exemplo de método de repositório que não é um CRUD simples.

---

## 5. O Contexto `Parking/` — O Coração do Domínio

### 5.1 `LicensePlate.cs` — Value Object com Validação de Formato

```csharp
public sealed partial class LicensePlate : ValueObject
{
    public string Value { get; }

    public static LicensePlate Criar(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(ParkingSessionErrors.PlacaObrigatoria);

        var normalized = value.Trim().ToUpperInvariant();

        if (!PlateRegex().IsMatch(normalized))
            throw new DomainException(ParkingSessionErrors.PlacaFormatoInvalido);

        return new LicensePlate(normalized);
    }

    [GeneratedRegex("^[A-Z0-9]{5,8}$")]
    private static partial Regex PlateRegex();
}
```

**Por que normalizar (`Trim().ToUpperInvariant()`) antes de validar?** O simulador e webhooks externos podem enviar placas com espaços acidentais ou minúsculas. Normalizar **dentro do VO** garante que `LicensePlate.Criar("zul0001")` e `LicensePlate.Criar("ZUL0001")` produzam exatamente o mesmo valor — essencial para a correlação de eventos por placa (RN-10), que depende de comparação de igualdade exata no banco.

**Por que uma regex genérica `[A-Z0-9]{5,8}` em vez do formato exato de placas brasileiras (`ABC1234` ou Mercosul `ABC1D23`)?** O enunciado do desafio usa placas fictícias no formato `ZUL0001` (4 letras + 4 dígitos), que não corresponde a nenhum padrão real brasileiro. Uma regex mais permissiva evita rejeitar dados legítimos do simulador enquanto ainda impede lixo óbvio (vazio, símbolos, tamanho absurdo).

**Por que `[GeneratedRegex]` em vez de `new Regex(...)`?** É o gerador de código-fonte do .NET (`System.Text.RegularExpressions.Generator`) — a regex é compilada em tempo de build para IL nativo, sem custo de compilação em runtime na primeira execução. É a evolução do antigo `RegexOptions.Compiled`.

### 5.2 `PricingSnapshot.cs` — O Value Object Mais Importante do Domínio

```csharp
public sealed class PricingSnapshot : ValueObject
{
    public decimal OccupancyPercentageAtEntry { get; }
    public decimal Multiplier { get; }

    public static PricingSnapshot CalcularPara(decimal occupancyPercentage)
    {
        var multiplier = occupancyPercentage switch
        {
            < 25m => 0.90m,
            <= 50m => 1.00m,
            <= 75m => 1.10m,
            <= 100m => 1.25m,
            _ => 1.25m
        };

        return new PricingSnapshot(occupancyPercentage, multiplier);
    }
}
```

**O problema que este VO resolve:** o preço dinâmico do desafio (RN-4) é calculado **no momento da entrada**, com base na lotação da garagem naquele instante. Mas o valor efetivamente cobrado só é calculado **na saída** — potencialmente horas depois, quando a lotação já mudou completamente. Sem um mecanismo de "congelamento", o sistema cobraria o cliente com o multiplicador da **saída**, não da **entrada** — o que contraria o enunciado ("Preço dinâmico **na hora da entrada**").

`PricingSnapshot` resolve isso sendo **imutável e calculado uma única vez**, no momento em que `ParkingSession.IniciarEntrada` é chamado. Ele fica "congelado" dentro da sessão até a saída, quando `RegistrarSaida` o lê para compor o valor final — nunca recalcula a lotação naquele momento.

**Por que os limites são `< 25`, `<= 50`, `<= 75`, `<= 100` (e não `< 50`, `< 75`)?** Reflete literalmente o enunciado: *"Lotação < 25%: desconto", "Lotação até 50%: normal", "Lotação até 75%: acréscimo 10%", "Lotação até 100%: acréscimo 25%"*. O uso de `<=` nos limites intermediários (exatamente 50% e exatamente 75%) segue a leitura mais natural de "até X%" como um intervalo fechado.

### 5.3 Os Eventos de Domínio

```csharp
public sealed record VehicleEnteredEvent(Guid SessionId, string LicensePlate, DateTime EntryTime) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

Três eventos espelham exatamente os três eventos externos que o webhook recebe: `VehicleEnteredEvent`, `VehicleParkedEvent`, `VehicleExitedEvent`. Isso não é coincidência — o domínio reage ao mesmo vocabulário de negócio que o enunciado define (`ENTRY`, `PARKED`, `EXIT`), mantendo a **linguagem ubíqua** (Ubiquitous Language do DDD) consistente do webhook até o coração do domínio.

### 5.4 `ParkingSession.cs` — A Raiz do Agregado

```csharp
public sealed class ParkingSession : AggregateRoot
{
    private const int MinutosGratis = 30;

    public LicensePlate LicensePlate { get; private set; } = null!;
    public DateTime EntryTime { get; private set; }
    public PricingSnapshot PricingSnapshot { get; private set; } = null!;

    public DateTime? ParkedAt { get; private set; }
    public GeoCoordinate? ParkedCoordinate { get; private set; }
    public Guid? SpotId { get; private set; }
    public string? SectorCode { get; private set; }

    public DateTime? ExitTime { get; private set; }
    public decimal? AmountCharged { get; private set; }

    public ParkingSessionStatus Status { get; private set; }
}
```

**Por que tantas propriedades nullable (`ParkedAt?`, `SpotId?`, `ExitTime?`...)?** Porque `ParkingSession` representa as **três fases da vida de um veículo** dentro de um único agregado — `Entrou`, `Estacionado`, `Finalizado`. Cada fase preenche progressivamente mais campos. Um objeto no estado `Entrou` legitimamente não tem `SpotId` ainda — não é um bug, é o modelo refletindo fielmente a realidade: o sistema só descobre em qual vaga o carro está quando o evento `PARKED` chega.

Uma alternativa seria modelar três classes separadas (`EntradaRegistrada`, `VeiculoEstacionado`, `SessaoFinalizada`) e fazer transições substituindo o objeto inteiro — mas isso complicaria a persistência (mesma linha lógica em três tabelas ou um esquema de herança no EF Core) sem ganho real de segurança, já que os *guards* de cada método já impedem o uso de campos fora de ordem.

#### `IniciarEntrada` — A Fábrica (Factory Method)

```csharp
public static ParkingSession IniciarEntrada(LicensePlate licensePlate, DateTime entryTime, decimal occupancyPercentage)
{
    if (occupancyPercentage is < 0 or > 100)
        throw new DomainException(ParkingSessionErrors.OcupacaoPercentualInvalida);

    if (occupancyPercentage >= 100m)
        throw new DomainException(ParkingSessionErrors.GaragemCheia);

    var session = new ParkingSession
    {
        LicensePlate = licensePlate,
        EntryTime = entryTime,
        PricingSnapshot = PricingSnapshot.CalcularPara(occupancyPercentage),
        Status = ParkingSessionStatus.Entrou
    };

    session.RaiseDomainEvent(new VehicleEnteredEvent(session.Id, licensePlate.Value, entryTime));

    return session;
}
```

**Por que a validação de lotação (RN-3/RN-8: bloquear entrada com garagem cheia) mora aqui, e não na Application?** Porque é uma regra de negócio pura — "não existe uma sessão de estacionamento válida quando a garagem está lotada" — que deve valer **sempre**, não importa quem chame `IniciarEntrada`. Se essa checagem estivesse só no Handler da Application, um teste unitário do domínio poderia criar sessões inválidas sem perceber. Colocando o guard na fábrica, `ParkingSession` **nunca existe** em memória com uma lotação de 100% — o objeto simplesmente não é criado.

**Por que o handler (`RegisterEntryHandler`) calcula `occupancyPercentage` e passa como parâmetro, em vez de `ParkingSession` consultar o repositório sozinha?** Porque o domínio não deve ter acesso a repositórios ou dependências de infraestrutura — isso quebraria a regra fundamental da Clean Architecture (camadas internas não dependem de externas). A Application consulta `ISectorRepository`/`ISpotRepository` para obter os números brutos (capacidade total, vagas ocupadas) e entrega apenas o **resultado do cálculo** (`occupancyPercentage`) ao domínio. O domínio decide o que fazer com esse número; não decide como obtê-lo.

#### `RegistrarEstacionamento` — Transição `Entrou → Estacionado`

```csharp
public void RegistrarEstacionamento(Guid spotId, string sectorCode, GeoCoordinate coordinate, DateTime parkedAt)
{
    if (Status != ParkingSessionStatus.Entrou)
        throw new DomainException(ParkingSessionErrors.SessaoJaEstacionada);

    SpotId = spotId;
    SectorCode = sectorCode;
    ParkedCoordinate = coordinate;
    ParkedAt = parkedAt;
    Status = ParkingSessionStatus.Estacionado;

    RaiseDomainEvent(new VehicleParkedEvent(Id, spotId, sectorCode));
}
```

**Este é o método onde o `SectorCode` é definido pela primeira vez.** Até aqui (estado `Entrou`), a sessão não sabia em qual setor o veículo ficaria — só descobre agora, através da vaga que a Application resolveu por coordenada. É este `SectorCode`, gravado neste exato momento, que será usado depois em `RegistrarSaida` para buscar o `BasePrice` correto do setor.

**Por que o guard verifica `!= Entrou` (e não, por exemplo, `== Finalizado`)?** Porque `RegistrarEstacionamento` só faz sentido vindo do estado `Entrou`. Se a sessão já está `Estacionado` (evento `PARKED` duplicado) ou já `Finalizado`, ambos são erros — o guard único cobre os dois casos com uma mensagem (`SessaoJaEstacionada`) que, embora nomeada para o caso mais comum, é tecnicamente correta para ambos: a operação já não é mais válida.

#### `RegistrarSaida` — Transição `Estacionado → Finalizado` e o Cálculo da Tarifa

```csharp
public void RegistrarSaida(DateTime exitTime, decimal sectorBasePrice)
{
    if (Status != ParkingSessionStatus.Estacionado)
        throw new DomainException(ParkingSessionErrors.SessaoNaoEstacionada);

    if (sectorBasePrice <= 0)
        throw new DomainException(ParkingSessionErrors.BasePriceInvalido);

    if (exitTime < EntryTime)
        throw new DomainException(ParkingSessionErrors.ExitTimeAnteriorAEntrada);

    ExitTime = exitTime;
    AmountCharged = CalcularValorCobrado(exitTime, sectorBasePrice);
    Status = ParkingSessionStatus.Finalizado;

    RaiseDomainEvent(new VehicleExitedEvent(Id, AmountCharged.Value));
}

private decimal CalcularValorCobrado(DateTime exitTime, decimal sectorBasePrice)
{
    var minutosDecorridos = (exitTime - EntryTime).TotalMinutes;

    if (minutosDecorridos <= MinutosGratis)
        return 0m;

    var minutosCobraveis = minutosDecorridos - MinutosGratis;
    var horasCobradas = (int)Math.Ceiling(minutosCobraveis / 60.0);

    return horasCobradas * sectorBasePrice * PricingSnapshot.Multiplier;
}
```

**Passo a passo do cálculo (RN-6), com exemplo real usado nos testes:** entrada às 08:00, saída às 09:31 (91 minutos depois), `sectorBasePrice = 10`, lotação na entrada = 50% (`Multiplier = 1.00`).

1. `minutosDecorridos = 91`
2. `91 > 30` → não é gratuito
3. `minutosCobraveis = 91 - 30 = 61`
4. `horasCobradas = ceil(61 / 60) = ceil(1.0166...) = 2`
5. `AmountCharged = 2 × 10 × 1.00 = 20`

**Por que `Math.Ceiling` e não `Math.Round`?** O enunciado é explícito: *"cobrar tarifa fixa por hora, arredondando para cima"*. `Ceiling` sempre arredonda para cima, mesmo que `minutosCobraveis` seja `61` (pouco mais que uma hora) — o cliente paga 2 horas completas, nunca uma fração. `Round` arredondaria `61` para `1` hora (mais próximo), o que violaria a regra.

**Por que `PricingSnapshot.Multiplier` é lido aqui, e não recalculado?** Este é o ponto crucial que resolve a "premissa da lotação" documentada no `PLAN.md`: o multiplicador usado é **sempre** o que foi travado na entrada (`session.PricingSnapshot`), nunca a lotação atual no momento da saída. Um teste específico (`RegistrarSaida_DeveUsarMultiplicadorDaEntrada_NaoODaSaida`) existe exatamente para proteger essa garantia.

**Por que `sectorBasePrice` é passado como parâmetro em vez de a sessão já guardá-lo desde a entrada?** Porque o `BasePrice` do setor só é conhecido depois do evento `PARKED` (é a vaga que revela o setor). Guardar o preço no momento da entrada seria impossível — o setor ainda não existe na sessão nesse ponto. A Application busca o `Sector` pelo `SectorCode` (já gravado por `RegistrarEstacionamento`) e passa o `BasePrice` no momento da saída.

**Por que validar `exitTime < EntryTime`?** É uma invariante de sanidade temporal — dados vindos de um webhook externo não são confiáveis por padrão. Sem essa checagem, um evento `EXIT` malformado (ou fora de ordem) produziria `minutosDecorridos` negativo, e `Math.Ceiling` de um número negativo geraria um valor de cobrança negativo, sem sentido de negócio.

### 5.5 Mapeamento Completo das Regras de Negócio

| RN | Regra do enunciado | Onde é implementada |
|----|---------------------|----------------------|
| RN-3 | Bloquear entrada com garagem cheia | `ParkingSession.IniciarEntrada` (guard) |
| RN-4 | Preço dinâmico calculado na entrada | `PricingSnapshot.CalcularPara` |
| RN-5 | `PARKED` associa vaga e setor à sessão | `ParkingSession.RegistrarEstacionamento` |
| RN-6 | 30 min grátis + tarifa por hora arredondada para cima | `ParkingSession.CalcularValorCobrado` |
| RN-8 | Reforço do bloqueio a 100% de lotação | Idem RN-3 — mesmo guard, reavaliado a cada `ENTRY` |
| — | Vaga não pode ser ocupada/liberada duas vezes | `Spot.Ocupar` / `Spot.Liberar` (guards simétricos) |
| — | Placa deve ter formato válido | `LicensePlate.Criar` |

---

## 6. O Repositório (`IParkingSessionRepository`)

```csharp
public interface IParkingSessionRepository
{
    Task<ParkingSession?> GetActiveByLicensePlateAsync(string licensePlate, CancellationToken cancellationToken = default);
    Task<ParkingSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ParkingSession session, CancellationToken cancellationToken = default);
    void Update(ParkingSession session);

    Task<decimal> GetRevenueAsync(string sectorCode, DateOnly date, CancellationToken cancellationToken = default);
}
```

**`GetActiveByLicensePlateAsync` é o método mais importante desta interface — e a resposta à maior ambiguidade do enunciado.** Os eventos `ENTRY`, `PARKED` e `EXIT` não trazem um identificador de sessão comum — só a placa. Este método assume que **existe no máximo uma sessão ativa (não finalizada) por placa** em um dado momento, e é ele quem localiza a sessão correta para os eventos `PARKED`/`EXIT` correlacionarem com o `ENTRY` que os precedeu.

**`GetRevenueAsync` fica no repositório do agregado, e não em um repositório de "relatórios"?** Porque, seguindo DDD, consultas de leitura que agregam dados de um único tipo de agregado (`ParkingSession`) continuam sendo responsabilidade do seu repositório — não há necessidade de um `IReportRepository` separado para uma soma simples. Se o sistema crescesse com relatórios mais complexos (múltiplos agregados, joins pesados), aí sim se justificaria uma camada de leitura dedicada (CQRS com modelos de leitura próprios).

---

## 7. Diagrama Conceitual

```
┌───────────────────────────────────────────────────────────────────┐
│                     PARKINGMANAGEMENT.DOMAIN                      │
│                                                                   │
│  ┌────────────────────┐        ┌─────────────────────────────┐   │
│  │   Garage/           │        │   Parking/                  │   │
│  │                     │        │                              │   │
│  │  Sector (Entity)    │        │  ParkingSession              │   │
│  │  ─────────────────  │        │  (AggregateRoot)             │   │
│  │  Code, BasePrice,   │        │  ──────────────────────────  │   │
│  │  MaxCapacity        │        │  LicensePlate (VO)           │   │
│  │                     │        │  EntryTime                   │   │
│  │  Spot (Entity)      │◄───────┤  PricingSnapshot (VO)        │   │
│  │  ─────────────────  │ vaga é │  ParkedAt / ParkedCoordinate │   │
│  │  ExternalId,        │ referen│  SpotId / SectorCode         │   │
│  │  SectorCode,        │ ciada  │  ExitTime / AmountCharged    │   │
│  │  Coordinate (VO),   │ por Id │  Status                      │   │
│  │  Status             │        │                              │   │
│  │                     │        │  + IniciarEntrada(...)       │   │
│  │  + Ocupar()         │        │  + RegistrarEstacionamento() │   │
│  │  + Liberar()        │        │  + RegistrarSaida(...)       │   │
│  │                     │        │  - CalcularValorCobrado()    │   │
│  └─────────────────────┘        └───────────────┬──────────────┘   │
│                                                  │ eventos          │
│                                     VehicleEntered/Parked/Exited   │
│                                                                   │
│  Common/: Entity, AggregateRoot, ValueObject, DomainException,   │
│           IDomainEvent, GeoCoordinate (VO compartilhado)         │
└───────────────────────────────────────────────────────────────────┘
```

---

## 8. Resumo das Decisões Técnicas

| Decisão | Alternativa | Por que escolhemos assim |
|---------|-------------|--------------------------|
| Um único agregado rico (`ParkingSession`) para as 3 fases | Três classes/tabelas separadas por fase | Guards já garantem ordem correta das transições; evita complexidade extra de mapeamento/herança sem ganho real de segurança |
| `PricingSnapshot` travado na entrada | Recalcular lotação na saída | É a única forma de honrar "preço dinâmico na hora da entrada" do enunciado quando a saída ocorre bem depois |
| `SectorCode` só definido em `RegistrarEstacionamento` | Exigir setor já na entrada | O setor só é conhecido de fato quando a coordenada do evento `PARKED` é resolvida contra as vagas sincronizadas |
| `GeoCoordinate` em `Common/ValueObjects` | Duplicar o VO em `Garage` e `Parking` | Evita acoplamento cruzado entre os dois contextos; ambos dependem só de `Common` |
| `Math.Ceiling` para arredondamento de horas | `Math.Round` | Enunciado exige explicitamente arredondamento para cima, nunca para o mais próximo |
| Correlação de eventos por placa (sessão ativa única) | Exigir um `session_id` externo | Os payloads do desafio só trazem `license_plate` — é o único identificador comum disponível |
| `Sector`/`Spot` como `Entity` simples, não `AggregateRoot` | Torná-los agregados com eventos próprios | São dados de configuração espelhados de um sistema externo, sem regras de negócio complexas que justifiquem eventos |
| Guards simétricos em `Spot.Ocupar`/`Liberar` | Deixar a Infrastructure controlar o estado | Garante que o estado da vaga nunca fique inconsistente mesmo com bugs de orquestração na Application |
