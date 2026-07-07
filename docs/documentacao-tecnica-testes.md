# Documentação Técnica — Camada de Testes (`ParkingManagement.Domain.Tests`, `ParkingManagement.Application.Tests` e `ParkingManagement.Integration.Tests`)

> **Objetivo deste documento:** explicar em profundidade a estratégia de testes adotada no projeto, detalhando cada decisão de design, ferramenta e padrão utilizado — incluindo um bug real de persistência que os testes de integração revelaram durante o desenvolvimento, e como isso reforça o valor de cada nível da pirâmide de testes.

---

## 1. A Filosofia de Testes deste Projeto

### 1.1 Por que testar?

Num sistema orientado a webhook — onde eventos chegam de um sistema externo, fora do nosso controle, e potencialmente fora de ordem — testes automatizados são a única forma confiável de garantir que regras como "não permitir entrada com a garagem cheia" ou "usar o multiplicador de preço da entrada, nunca o da saída" continuam corretas à medida que o código evolui.

| Função | Descrição |
|--------|-----------|
| **Verificação de regras de negócio** | Garantem que o domínio se comporta como o enunciado da Estapar determina |
| **Documentação viva** | Um teste bem nomeado descreve um comportamento do sistema sem ambiguidade |
| **Rede de segurança para refatoração** | Permitem alterar a implementação com confiança de que o contrato de negócio não foi quebrado |

### 1.2 A Pirâmide de Testes Aplicada a Este Projeto

```
           ╱▔▔▔▔▔▔▔▔╲          ← Integração / E2E (2 testes — API real + SQL Server real)
          ╱▔▔▔▔▔▔▔▔▔▔▔▔╲       (poucos, lentos, caros)
         ╱▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔╲    ← Handlers/Validators (38 testes — mocks)
        ╱▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔╲ ← Domínio (56 testes — sem mocks, sem infra)
       ▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔ (muitos, rápidos, baratos)
```

- **`ParkingManagement.Domain.Tests`** — base da pirâmide. Testes unitários puros do domínio: `ParkingSession` e `Spot`. Sem mocks, sem banco de dados.
- **`ParkingManagement.Application.Tests`** — camada intermediária. Testa os Handlers usando mocks dos repositórios e do `IUnitOfWork`/`IGarageSimulatorClient` com NSubstitute.
- **`ParkingManagement.Integration.Tests`** — topo da pirâmide. Sobe a API real em memória (`WebApplicationFactory`) contra um SQL Server real em container (`Testcontainers.MsSql`), com o cliente do simulador substituído por um fake local. Valida o fluxo HTTP end-to-end completo: sincronização da garagem → `ENTRY` → `PARKED` → `EXIT` → `GET /revenue`.

> **Por que este projeto precisou, na prática, do nível de Integration Tests para ficar correto?** Durante o desenvolvimento, o fluxo completo passava em todos os testes de Domain e Application, mas falhava ao ser exercitado de ponta a ponta: a `WebhookController` retornava `422 Unprocessable Entity` ("garagem no limite de capacidade") logo no primeiro evento `ENTRY`, mesmo com a garagem recém-sincronizada e vazia. A causa raiz: nenhum Handler chamava `IUnitOfWork.SaveChangesAsync()` — os repositórios apenas marcavam entidades no *Change Tracker* do EF Core, mas nada era persistido de fato. `GetTotalCapacityAsync()` retornava `0` (nenhum setor persistido), e o Handler de entrada interpretava isso como garagem cheia. Nenhum teste de Domain ou Application detectaria esse bug, porque ambos usam mocks/objetos em memória — só um teste que sobe a pilha completa contra um banco real expõe uma falha de persistência real. Essa é a justificativa mais concreta possível para manter os três níveis da pirâmide.

---

## 2. Estrutura de Projetos

```
tests/
├── ParkingManagement.Domain.Tests/
│   ├── Parking/
│   │   ├── Builders/
│   │   │   └── ParkingSessionFaker.cs
│   │   └── ParkingSessionTests.cs
│   ├── Garage/
│   │   └── SpotTests.cs
│   └── ParkingManagement.Domain.Tests.csproj
│
├── ParkingManagement.Application.Tests/
│   ├── Features/
│   │   ├── Garage/
│   │   │   └── SyncGarageHandlerTests.cs
│   │   └── Parking/
│   │       ├── RegisterEntryHandlerTests.cs
│   │       ├── RegisterParkedHandlerTests.cs
│   │       ├── RegisterExitHandlerTests.cs
│   │       └── GetRevenueHandlerTests.cs
│   └── ParkingManagement.Application.Tests.csproj
│
└── ParkingManagement.Integration.Tests/
    ├── Fakes/
    │   └── FakeGarageSimulatorClient.cs         ← Substitui o simulador real nos testes
    ├── ParkingApiFactory.cs                     ← WebApplicationFactory + Testcontainers + DbUp
    ├── IntegrationTestCollection.cs              ← xUnit collection fixture
    ├── Parking/
    │   └── ParkingFlowTests.cs                   ← Fluxo end-to-end completo
    └── ParkingManagement.Integration.Tests.csproj
```

---

## 3. Pacotes NuGet e seus Papéis

### 3.1 `ParkingManagement.Domain.Tests`

| Pacote | Papel |
|--------|-------|
| `xunit` / `xunit.runner.visualstudio` | Framework de testes e runner |
| `Microsoft.NET.Test.Sdk` | SDK base para `dotnet test` |
| `FluentAssertions` | Assertions expressivas: `.Should().Be(...)`, `.Should().Throw<T>()` |
| `Bogus` | Geração de placas e coordenadas fake realistas |

### 3.2 Pacotes adicionais de `ParkingManagement.Application.Tests`

| Pacote | Papel |
|--------|-------|
| `NSubstitute` | Mocking de `ISectorRepository`, `ISpotRepository`, `IParkingSessionRepository`, `IUnitOfWork`, `IGarageSimulatorClient` |

### 3.3 Pacotes adicionais de `ParkingManagement.Integration.Tests`

| Pacote | Papel |
|--------|-------|
| `Microsoft.AspNetCore.Mvc.Testing` | Sobe a API ASP.NET Core em memória via `WebApplicationFactory<Program>` |
| `Testcontainers.MsSql` | Sobe um container Docker com **SQL Server real** durante a execução dos testes |
| `dbup-sqlserver` | Aplica os scripts SQL de migração contra o banco do container antes dos testes rodarem |

> **Por que Testcontainers.MsSql e não um banco em memória (InMemory provider do EF Core)?** O provider `InMemory` do EF Core não valida constraints, tipos de coluna, nem o comportamento real de índices e *owned types* (`OwnsOne`) — um teste que passa contra `InMemory` pode falhar contra o SQL Server real por diferenças de comportamento (ex: comparação de `decimal`, tradução de queries `Sum`/`Count`). Testcontainers garante que os testes de integração validam exatamente o mesmo motor de banco usado em produção.

---

## 4. Padrões e Ferramentas em Profundidade

### 4.1 O Padrão AAA (Arrange, Act, Assert)

Todos os testes seguem rigorosamente o padrão AAA, com comentários explícitos marcando cada fase:

```csharp
[Fact]
public void IniciarEntrada_DeveLancar_QuandoLotacao100Porcento()
{
    // Arrange
    var licensePlate = LicensePlate.Criar("ZUL0005");

    // Act
    Action act = () => ParkingSession.IniciarEntrada(licensePlate, DateTime.UtcNow, 100m);

    // Assert
    act.Should().Throw<DomainException>()
        .WithMessage(ParkingSessionErrors.GaragemCheia);
}
```

### 4.2 FluentAssertions — Assertions Legíveis

```csharp
result.IsSuccess.Should().BeTrue();
result.Value.AmountCharged.Should().Be(20m);
act.Should().Throw<DomainException>().WithMessage(ParkingSessionErrors.SessaoNaoEstacionada);
```

**`.WithMessage(...)`** verifica não só o tipo da exceção, mas a mensagem exata — garante que é *exatamente aquele guard* que disparou, não qualquer `DomainException` genérica que por acaso o mesmo cenário poderia produzir.

### 4.3 NSubstitute — Mocking de Interfaces

```csharp
private readonly IParkingSessionRepository _sessionRepository = Substitute.For<IParkingSessionRepository>();
private readonly ISectorRepository _sectorRepository = Substitute.For<ISectorRepository>();
private readonly ISpotRepository _spotRepository = Substitute.For<ISpotRepository>();
private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

_sectorRepository.GetTotalCapacityAsync(Arg.Any<CancellationToken>()).Returns(100);
_spotRepository.CountOccupiedAsync(Arg.Any<CancellationToken>()).Returns(40);

await _sessionRepository.Received(1).AddAsync(Arg.Any<ParkingSession>(), Arg.Any<CancellationToken>());
```

**Por que mockar `IUnitOfWork` também, e não só os repositórios?** Porque, desde a correção do bug de persistência (seção 1.2), todo Handler que muta estado depende de `IUnitOfWork.SaveChangesAsync`. Os testes de Application verificam a **orquestração** do Handler — incluindo o fato de que ele de fato tenta persistir — sem precisar de um banco real. Um mock de `IUnitOfWork` que simplesmente não faz nada é suficiente para isso; o teste de integração é quem garante que a implementação real (`ParkingDbContext`) realmente persiste.

### 4.4 Bogus — Geração de Dados Realistas

```csharp
public static class ParkingSessionFaker
{
    private static readonly Faker Faker = new();

    public static ParkingSession CriarEntrada(decimal occupancyPercentage = 40m, DateTime? entryTime = null)
    {
        var licensePlate = LicensePlate.Criar(Faker.Random.Replace("???####").ToUpperInvariant());
        return ParkingSession.IniciarEntrada(licensePlate, entryTime ?? DateTime.UtcNow, occupancyPercentage);
    }

    public static ParkingSession CriarEstacionada(decimal occupancyPercentage = 40m, DateTime? entryTime = null)
    {
        var session = CriarEntrada(occupancyPercentage, entryTime);
        var coordinate = GeoCoordinate.Criar(Faker.Address.Latitude(), Faker.Address.Longitude());
        session.RegistrarEstacionamento(Guid.NewGuid(), "A", coordinate, session.EntryTime.AddMinutes(2));
        return session;
    }
}
```

**`Faker.Random.Replace("???####")`** — gera uma string aleatória seguindo o padrão de máscara do Bogus (`?` = letra aleatória, `#` = dígito aleatório), produzindo algo como `"XYZ1234"`. Combinado com `.ToUpperInvariant()`, sempre gera placas válidas segundo a regex de `LicensePlate` (`[A-Z0-9]{5,8}`), sem precisar hardcodar placas fixas em cada teste.

**`CriarEstacionada` reaproveita `CriarEntrada` e avança o agregado mais um passo** — evita duplicar a criação de uma sessão válida em todo teste que precisa de uma sessão já `Estacionado`. É o mesmo papel que `FaturaFaker` cumpria no projeto de referência, adaptado para um agregado com fases sequenciais em vez de coleção de itens.

---

## 5. `ParkingManagement.Domain.Tests` em Profundidade

### 5.1 `ParkingSessionTests.cs` — Cenários de Preço Dinâmico e Cobrança

Os quatro primeiros testes validam, individualmente, cada faixa do multiplicador de preço dinâmico (RN-4):

```csharp
[Fact]
public void IniciarEntrada_DeveAplicarDesconto10Porcento_QuandoLotacaoAbaixoDe25()
{
    var session = ParkingSession.IniciarEntrada(LicensePlate.Criar("ZUL0001"), DateTime.UtcNow, 20m);
    session.PricingSnapshot.Multiplier.Should().Be(0.90m);
}
```

| Teste | Lotação testada | Multiplicador esperado |
|-------|------------------|--------------------------|
| `IniciarEntrada_DeveAplicarDesconto10Porcento_QuandoLotacaoAbaixoDe25` | 20% | 0.90 |
| `IniciarEntrada_DeveAplicarPrecoNormal_QuandoLotacaoAte50` | 50% | 1.00 |
| `IniciarEntrada_DeveAplicarAcrescimo10Porcento_QuandoLotacaoAte75` | 75% | 1.10 |
| `IniciarEntrada_DeveAplicarAcrescimo25Porcento_QuandoLotacaoAte100` | 99% | 1.25 |

**Por que testar exatamente `50` e `75` (os limites), e não só valores "no meio" de cada faixa?** Porque os limites são onde bugs de `<` vs `<=` se escondem. Testar `50m` confirma que a faixa "até 50%" é **inclusiva** (`<= 50m` no código), não `< 50m` — um erro de operador ali produziria silenciosamente o multiplicador errado exatamente nos valores de fronteira, os mais prováveis de ocorrerem na prática (uma garagem que atinge exatamente metade da capacidade).

```csharp
[Fact]
public void IniciarEntrada_DeveLancar_QuandoLotacao100Porcento()
{
    Action act = () => ParkingSession.IniciarEntrada(LicensePlate.Criar("ZUL0005"), DateTime.UtcNow, 100m);
    act.Should().Throw<DomainException>().WithMessage(ParkingSessionErrors.GaragemCheia);
}
```

Cobre RN-3/RN-8: bloqueio de entrada com garagem 100% cheia.

**O cenário mais importante do arquivo — a premissa da lotação travada na entrada:**

```csharp
[Fact]
public void RegistrarSaida_DeveUsarMultiplicadorDaEntrada_NaoODaSaida()
{
    // Arrange
    var entryTime = DateTime.UtcNow;
    var session = ParkingSessionFaker.CriarEstacionada(occupancyPercentage: 20m, entryTime: entryTime);

    // Act — 30 min grátis + 30 min cobráveis => 1 hora, base 10, multiplicador 0.90 travado na entrada
    session.RegistrarSaida(entryTime.AddMinutes(61), sectorBasePrice: 10m);

    // Assert
    session.AmountCharged.Should().Be(9m);
}
```

Este teste é a materialização direta, em código, da premissa documentada no `PLAN.md`: *"Lotação calculada globalmente no momento do ENTRY; o BasePrice do setor só é aplicado depois, no EXIT, junto com o multiplicador travado na entrada."* Se alguém, por engano, alterasse `CalcularValorCobrado` para recalcular a lotação atual em vez de ler `PricingSnapshot.Multiplier`, este teste falharia imediatamente — mesmo que a lotação "atual" simulada fosse coincidentemente igual, o teste está desenhado para que a diferença importe caso a implementação mude.

**O teste de arredondamento — validação numérica exata do RN-6:**

```csharp
[Fact]
public void RegistrarSaida_DeveArredondarHoraCheiaParaCima()
{
    var entryTime = DateTime.UtcNow;
    var session = ParkingSessionFaker.CriarEstacionada(occupancyPercentage: 50m, entryTime: entryTime);

    // 30 min grátis + 61 min cobráveis => arredonda para 2 horas
    session.RegistrarSaida(entryTime.AddMinutes(91), sectorBasePrice: 10m);

    session.AmountCharged.Should().Be(20m);
}
```

**Por que 91 minutos, e não um número "redondo" como 90 ou 120?** 91 minutos produz `61` minutos cobráveis após os 30 grátis — que é **mais de uma hora, mas menos de duas**. Esse é exatamente o caso de fronteira que prova que o arredondamento é para cima (`Ceiling`) e não para o mais próximo (`Round`): `61 / 60 = 1.0166...`, que `Ceiling` arredonda para `2`, mas `Round` arredondaria para `1`. Escolher um valor "redondo" como 120 minutos (exatamente 2 horas) não distinguiria entre as duas estratégias de arredondamento — o teste falharia em detectar uma regressão de `Ceiling` para `Round`.

### 5.2 `SpotTests.cs` — Guards Simétricos de Ocupar/Liberar

```csharp
[Fact]
public void Ocupar_DeveLancar_QuandoJaOcupada()
{
    var spot = Spot.Criar(1, "A", GeoCoordinate.Criar(-23.561684, -46.655981));
    spot.Ocupar();

    Action act = () => spot.Ocupar();

    act.Should().Throw<DomainException>().WithMessage(GarageErrors.VagaJaOcupada);
}
```

Os quatro testes deste arquivo cobrem os dois caminhos felizes (`Ocupar`/`Liberar` a partir do estado correto) e os dois guards (tentar repetir a mesma transição). Simples, mas essenciais: é este par de guards que impede, na prática, que um bug de correlação de coordenadas (`FindByCoordinateAsync`, ver documentação de Infrastructure) ocupe a mesma vaga duas vezes sem que ninguém perceba.

---

## 6. `ParkingManagement.Application.Tests` em Profundidade

### 6.1 A Estratégia de Isolamento

```
Teste → Handler → repositórios e IUnitOfWork (mocks controlados pelo teste)
```

Cada teste de Handler: cria os mocks necessários com NSubstitute, configura o comportamento esperado, instancia o Handler passando os mocks, executa e verifica o resultado — e, quando relevante, verifica que os métodos certos do repositório foram chamados.

### 6.2 `RegisterEntryHandlerTests` — Sucesso e Bloqueio de Capacidade

```csharp
[Fact]
public async Task Handle_DeveRegistrarEntrada_QuandoGaragemNaoEstaCheia()
{
    _sectorRepository.GetTotalCapacityAsync(Arg.Any<CancellationToken>()).Returns(100);
    _spotRepository.CountOccupiedAsync(Arg.Any<CancellationToken>()).Returns(40);

    var handler = CreateHandler();
    var result = await handler.Handle(new RegisterEntryRequest("ZUL0001", DateTime.UtcNow), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    await _sessionRepository.Received(1).AddAsync(Arg.Any<ParkingSession>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Handle_DeveLancarDomainException_QuandoGaragemCheia()
{
    _sectorRepository.GetTotalCapacityAsync(Arg.Any<CancellationToken>()).Returns(100);
    _spotRepository.CountOccupiedAsync(Arg.Any<CancellationToken>()).Returns(100);

    var handler = CreateHandler();
    Func<Task> act = () => handler.Handle(new RegisterEntryRequest("ZUL0002", DateTime.UtcNow), CancellationToken.None);

    await act.Should().ThrowAsync<DomainException>();
}
```

**Por que verificar `Received(1).AddAsync(...)` no teste de sucesso?** Mesma razão do projeto de referência: um Handler que retorna sucesso mas não chama `AddAsync` passaria na assertion de retorno, mas a sessão nunca seria persistida. Este teste protege contra esse tipo específico de regressão silenciosa.

**Por que o teste de garagem cheia usa `Func<Task>` e `ThrowAsync`, em vez de `Action`/`Throw` (usado nos testes de Domain)?** Porque `Handler.Handle` é assíncrono (`Task<Result<...>>`) — capturar a chamada como `Func<Task>` e usar `ThrowAsync<T>()` é a forma correta do FluentAssertions de testar exceções lançadas dentro de métodos `async`. Usar `Action`/`Throw` aqui simplesmente não compilaria contra um método assíncrono.

### 6.3 `RegisterParkedHandlerTests` — Correlação por Coordenada

```csharp
[Fact]
public async Task Handle_DeveAssociarVagaESetor_QuandoSessaoEVagaExistem()
{
    var session = ParkingSession.IniciarEntrada(LicensePlate.Criar("ZUL0001"), DateTime.UtcNow, 30m);
    var spot = Spot.Criar(1, "A", GeoCoordinate.Criar(-23.561684, -46.655981));

    _sessionRepository.GetActiveByLicensePlateAsync("ZUL0001", Arg.Any<CancellationToken>()).Returns(session);
    _spotRepository.FindByCoordinateAsync(Arg.Any<GeoCoordinate>(), Arg.Any<CancellationToken>()).Returns(spot);

    var handler = CreateHandler();
    var result = await handler.Handle(new RegisterParkedRequest("ZUL0001", -23.561684, -46.655981), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.SectorCode.Should().Be("A");
    spot.Status.Should().Be(SpotStatus.Ocupada);
}
```

**Por que verificar `spot.Status.Should().Be(SpotStatus.Ocupada)` além do resultado do Handler?** Porque o Handler tem **duas responsabilidades de mutação**: registrar o estacionamento na sessão **e** ocupar a vaga. Um teste que só verificasse `result.IsSuccess` não capturaria uma regressão em que alguém removesse acidentalmente a chamada a `spot.Ocupar()` — o Handler continuaria retornando sucesso, mas a vaga ficaria incorretamente marcada como livre.

```csharp
[Fact]
public async Task Handle_DeveRetornarNotFound_QuandoSessaoAtivaNaoExiste()
{
    _sessionRepository.GetActiveByLicensePlateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns((ParkingSession?)null);

    var handler = CreateHandler();
    var result = await handler.Handle(new RegisterParkedRequest("ZUL0002", -23.561684, -46.655981), CancellationToken.None);

    result.IsFailure.Should().BeTrue();
    result.Error.Message.Should().Be(ApplicationErrorMessages.Parking.SessaoAtivaNaoEncontrada);
}
```

**Por que `.Returns((ParkingSession?)null)` com cast explícito?** O tipo de retorno é `Task<ParkingSession?>` — nullable. Sem o cast, o compilador não consegue inferir de forma não-ambígua para qual sobrecarga de `Returns` o `null` literal se aplica; o cast explícito resolve a ambiguidade e documenta a intenção (simular "não encontrado").

### 6.4 `RegisterExitHandlerTests` — O Cenário Mais Completo

```csharp
[Fact]
public async Task Handle_DeveCalcularValorELiberarVaga_QuandoSessaoEstacionada()
{
    var entryTime = DateTime.UtcNow;
    var spotId = Guid.NewGuid();
    var session = CriarSessaoEstacionada(spotId, entryTime);
    var sector = Sector.Criar("A", 10m, 100);
    var spot = Spot.Criar(1, "A", GeoCoordinate.Criar(-23.561684, -46.655981));
    spot.Ocupar();

    _sessionRepository.GetActiveByLicensePlateAsync("ZUL0001", Arg.Any<CancellationToken>()).Returns(session);
    _sectorRepository.GetByCodeAsync("A", Arg.Any<CancellationToken>()).Returns(sector);
    _spotRepository.GetByIdAsync(spotId, Arg.Any<CancellationToken>()).Returns(spot);

    var handler = CreateHandler();
    var result = await handler.Handle(new RegisterExitRequest("ZUL0001", entryTime.AddMinutes(91)), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.AmountCharged.Should().Be(20m);
    spot.Status.Should().Be(SpotStatus.Livre);
}
```

**Este teste orquestra três mocks diferentes (`sessionRepository`, `sectorRepository`, `spotRepository`) porque o Handler real também depende dos três** (ver documentação de Application, seção 6.4, para a leitura linha a linha do `RegisterExitHandler`). A asserção `spot.Status.Should().Be(SpotStatus.Livre)` confirma que a vaga foi de fato liberada — não apenas que a sessão foi finalizada — protegendo contra uma regressão em que `ReleaseSpotAsync` deixasse de ser chamado.

**Por que este teste reaproveita os mesmos 91 minutos / `sectorBasePrice = 10` do teste de Domínio (`RegistrarSaida_DeveArredondarHoraCheiaParaCima`)?** Não é coincidência — o teste de Application confia que o **cálculo em si** já está correto (testado exaustivamente no Domain.Tests) e foca em verificar que o **Handler orquestra corretamente** os dados até chegar nesse cálculo (busca a sessão certa, o setor certo, libera a vaga certa). Reusar os mesmos números facilita conferir visualmente que o valor `20m` bate com o que já se sabe estar correto.

### 6.5 `GetRevenueHandlerTests` — Query sem Mutação

```csharp
[Fact]
public async Task Handle_DeveRetornarReceita_QuandoSetorExiste()
{
    var sector = Sector.Criar("A", 10m, 100);
    var date = DateOnly.FromDateTime(DateTime.UtcNow);

    _sectorRepository.GetByCodeAsync("A", Arg.Any<CancellationToken>()).Returns(sector);
    _sessionRepository.GetRevenueAsync("A", date, Arg.Any<CancellationToken>()).Returns(150.00m);

    var handler = CreateHandler();
    var result = await handler.Handle(new GetRevenueRequest("A", date), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.Amount.Should().Be(150.00m);
    result.Value.Currency.Should().Be("BRL");
}
```

**Por que `GetRevenueHandlerTests` não recebe (nem precisa de) um mock de `IUnitOfWork`?** Porque `GetRevenueHandler` implementa `IQueryHandler`, não `ICommandHandler` — é uma operação de leitura pura, que nunca chama `SaveChangesAsync`. A ausência do mock no construtor do teste já documenta, por si só, que este caso de uso não muta estado algum.

### 6.6 `SyncGarageHandlerTests` — *Upsert* sem Duplicação

```csharp
[Fact]
public async Task Handle_DeveAtualizarSetorExistente_SemDuplicar()
{
    var sectorExistente = Sector.Criar("A", 8m, 50);
    var configuration = new GarageConfigurationDto(
        Garage: [new GarageSectorDto("A", 10m, 100)],
        Spots: []);

    _simulatorClient.GetGarageConfigurationAsync(Arg.Any<CancellationToken>()).Returns(configuration);
    _sectorRepository.GetByCodeAsync("A", Arg.Any<CancellationToken>()).Returns(sectorExistente);

    var handler = CreateHandler();
    var result = await handler.Handle(new SyncGarageRequest(), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    sectorExistente.BasePrice.Should().Be(10m);
    sectorExistente.MaxCapacity.Should().Be(100);
    await _sectorRepository.DidNotReceive().AddAsync(Arg.Any<Sector>(), Arg.Any<CancellationToken>());
}
```

**`_sectorRepository.DidNotReceive().AddAsync(...)`** — é a contraparte negativa de `Received(1)`: confirma explicitamente que, quando o setor já existe, o Handler **não** tenta inseri-lo de novo (o que geraria uma violação da constraint única `IX_Sectors_Code`, ver documentação de Migrations). Testar tanto o caminho de criação (`SyncGarageHandlerTests` também tem um teste `Handle_DeveCriarSetoresEVagasNovos_QuandoNaoExistemAinda`) quanto o de atualização garante que a lógica de *upsert* está correta nos dois ramos do `if (existing is null)`.

---

## 7. `ParkingManagement.Integration.Tests` em Profundidade

### 7.1 A Estratégia: API Real + Banco Real + Simulador Fake

```
Teste HTTP
   │
   ▼
HttpClient ──▶ ASP.NET Core (em memória, via WebApplicationFactory<Program>)
                    │
                    ▼
              GarageSyncStartupService (roda no startup do host de teste)
                    │
                    ▼
              ExceptionHandlingMiddleware → WebhookController / RevenueController
                    │
                    ▼
              MediatR Pipeline (Logging → Validation → Handler)
                    │
              ┌─────┴──────────────────┐
              ▼                        ▼
     Repositórios reais         FakeGarageSimulatorClient
     (EF Core + SQL Server)     (substitui o simulador externo real)
              │
              ▼
     SQL Server real (container Docker via Testcontainers)
```

**Por que o simulador é substituído por um fake, mas o banco é real?** O SQL Server é uma peça da **nossa** infraestrutura — testá-la de verdade é viável e barato com Testcontainers. O simulador da Estapar é um sistema **externo e de terceiros**, não disponível no ambiente de CI — substituí-lo por `FakeGarageSimulatorClient` (uma implementação local e determinística de `IGarageSimulatorClient`) permite testar toda a orquestração da nossa aplicação sem depender de um serviço externo estar no ar.

### 7.2 `FakeGarageSimulatorClient` — Dados de Teste Determinísticos

```csharp
public sealed class FakeGarageSimulatorClient : IGarageSimulatorClient
{
    public static readonly GarageSpotDto SpotA1 = new(1, "A", -23.561684, -46.655981);
    public static readonly GarageSpotDto SpotA2 = new(2, "A", -23.561700, -46.656000);

    public Task<GarageConfigurationDto> GetGarageConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var configuration = new GarageConfigurationDto(
            Garage: [new GarageSectorDto("A", 10.0m, 2)],
            Spots: [SpotA1, SpotA2]);

        return Task.FromResult(configuration);
    }
}
```

**Por que expor `SpotA1`/`SpotA2` como campos `public static readonly`, em vez de valores soltos dentro do método?** Os testes (`ParkingFlowTests`) precisam enviar, no evento `PARKED`, exatamente as mesmas coordenadas que o fake "simulador" reporta para a vaga — caso contrário `FindByCoordinateAsync` (match exato, ver documentação de Infrastructure) não encontraria nenhuma vaga. Expor as coordenadas como constantes reutilizáveis evita duplicar os mesmos números mágicos em `FakeGarageSimulatorClient` e em `ParkingFlowTests`, garantindo que os dois sempre concordem.

**Capacidade de apenas 2 vagas (`MaxCapacity: 2`)** — um valor pequeno e deliberado, que torna trivial simular cenários de lotação alta/baixa em testes futuros sem precisar orquestrar dezenas de sessões.

### 7.3 `ParkingApiFactory` — Testcontainers + DbUp + Substituição de Serviços

```csharp
public sealed class ParkingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder().Build();

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        RunMigrations(_sqlContainer.GetConnectionString());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = _sqlContainer.GetConnectionString(),
                ["Simulator:BaseUrl"] = "http://simulator.invalid"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGarageSimulatorClient>();
            services.AddSingleton<IGarageSimulatorClient, FakeGarageSimulatorClient>();
        });
    }
}
```

**Ordem de execução crítica:** `InitializeAsync` (chamado pelo xUnit antes de qualquer teste da coleção) sobe o container **e já aplica as migrations** contra ele. Só depois disso, quando o primeiro teste chama `factory.CreateClient()`, o `WebApplicationFactory` constrói o host — e `ConfigureWebHost` já injeta a connection string do container (já migrado) na configuração. Isso garante que, quando `GarageSyncStartupService` roda (disparado pelo próprio startup do host, dentro de `CreateClient()`), as tabelas já existem.

**`configBuilder.AddInMemoryCollection(...)` sobrescrevendo `ConnectionStrings:SqlServer` e `Simulator:BaseUrl`** — a técnica de configuração usada aqui é mais simples que a do projeto de referência (que removia e recriava o `DbContextOptions` manualmente via `ConfigureServices`): como `AddInfrastructure` já lê a connection string via `IConfiguration` no momento em que é chamada (dentro do `Program.cs` da própria Api), basta que a configuração de teste **sobrescreva a chave antes** de `AddDbContext` ser executado. Isso funciona porque `ConfigureAppConfiguration` roda antes de `ConfigureServices`/`Program.cs` no pipeline do `WebApplicationFactory`.

**`services.RemoveAll<IGarageSimulatorClient>()` seguido de `AddSingleton<IGarageSimulatorClient, FakeGarageSimulatorClient>()`** — remove o registro real (o `HttpClient` tipado com `AddStandardResilienceHandler`, configurado em `AddInfrastructure`) e o substitui pelo fake. `RemoveAll` (de `Microsoft.Extensions.DependencyInjection.Extensions`) é necessário porque `AddHttpClient<TClient, TImplementation>` registra múltiplos serviços internamente — um simples `Remove` do primeiro descriptor encontrado não seria suficiente para garantir que só o fake responda.

**`Simulator:BaseUrl` ainda é sobrescrito para um valor inválido (`"http://simulator.invalid"`)** mesmo com o cliente substituído — por completude e clareza: garante que, se por engano o `HttpClient` real ainda fosse resolvido em vez do fake, o teste falharia ruidosamente (erro de DNS) em vez de silenciosamente tentar acessar uma URL de desenvolvimento real.

### 7.4 `IntegrationTestCollection` — Compartilhamento do Container

```csharp
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<ParkingApiFactory>
{
    public const string Name = "Integration";
}
```

Subir um container SQL Server leva alguns segundos. Compartilhar uma única instância de `ParkingApiFactory` (e portanto do container) entre todas as classes de teste da coleção evita pagar esse custo repetidamente — mesma razão de design do `gestao-faturas`.

### 7.5 `ParkingFlowTests` — O Cenário End-to-End

```csharp
[Fact]
public async Task FluxoCompleto_EntradaEstacionamentoSaida_DeveRefletirNaReceita()
{
    var client = factory.CreateClient();
    var licensePlate = $"E2E{Random.Shared.Next(1000, 9999)}";
    var entryTime = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    var exitTime = entryTime.AddMinutes(91);

    var entryResponse = await client.PostAsJsonAsync("/webhook", new
    {
        license_plate = licensePlate, entry_time = entryTime, event_type = "ENTRY"
    });

    var parkedResponse = await client.PostAsJsonAsync("/webhook", new
    {
        license_plate = licensePlate,
        lat = FakeGarageSimulatorClient.SpotA1.Lat,
        lng = FakeGarageSimulatorClient.SpotA1.Lng,
        event_type = "PARKED"
    });

    var exitResponse = await client.PostAsJsonAsync("/webhook", new
    {
        license_plate = licensePlate, exit_time = exitTime, event_type = "EXIT"
    });

    var revenueResponse = await client.GetAsync($"/revenue?sector=A&date={exitTime:yyyy-MM-dd}");
    var revenue = await revenueResponse.Content.ReadFromJsonAsync<RevenueResponseDto>();

    entryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    parkedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    exitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    revenueResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    revenue!.Amount.Should().BeGreaterThan(0m);
}
```

**Este é o teste que, historicamente, capturou o bug do `IUnitOfWork` ausente** (seção 1.2). Antes da correção, `entryResponse.StatusCode` vinha `422` (`"garagem no limite de capacidade"`) porque `SyncGarageHandler` nunca persistia os setores sincronizados — `GetTotalCapacityAsync()` retornava `0`, e o Handler de entrada interpretava capacidade zero como sinônimo de garagem cheia (`totalCapacity == 0 ? 100m : ...`, ver documentação de Application). Ao adicionar diagnóstico temporário (ler o corpo da resposta de erro na própria assertion) foi possível confirmar a causa exata antes de corrigir — uma técnica útil sempre que um teste de integração falha de forma inesperada: inspecionar o `ProblemDetails` retornado é geralmente mais rápido do que adicionar breakpoints.

**Por que `licensePlate = $"E2E{Random.Shared.Next(1000, 9999)}"` gerado por execução, em vez de uma placa fixa?** Embora não haja uma constraint de unicidade de placa no banco (uma mesma placa pode ter múltiplas sessões ao longo do tempo — ver documentação de Migrations), usar uma placa aleatória por execução evita qualquer interferência entre execuções repetidas do teste no mesmo container (útil durante o desenvolvimento, quando o teste é rodado várias vezes seguidas).

**Por que `entryTime` é uma data fixa (`2026-01-01T08:00:00Z`) e não `DateTime.UtcNow`?** Para que `GET /revenue?date=...` (que filtra por dia civil) funcione de forma previsível independentemente do horário em que a suíte de testes realmente rodar — se `entryTime` fosse `DateTime.UtcNow` executado perto da meia-noite UTC, `entryTime` e `exitTime` (91 minutos depois) poderiam cair em dois dias diferentes, quebrando a consulta de receita por data.

**Por que o teste verifica apenas `revenue.Amount.Should().BeGreaterThan(0m)`, em vez do valor exato (`20m`, como nos testes de Domain/Application)?** Porque testes de integração compartilham o mesmo container entre execuções da mesma coleção — se outro teste da suíte também gerar receita no setor `A` no mesmo dia antes deste rodar, o valor exato acumulado dependeria da ordem de execução dos testes, tornando uma asserção de igualdade frágil. Verificar "maior que zero" confirma que o fluxo completo (incluindo a soma feita por `GetRevenueAsync`) funciona, sem acoplar o teste a uma ordem de execução específica — o valor exato já é validado, de forma isolada e determinística, pelos testes de Domain (`RegistrarSaida_DeveArredondarHoraCheiaParaCima`).

```csharp
[Fact]
public async Task Webhook_DeveRetornarBadRequest_QuandoEventTypeDesconhecido()
{
    var client = factory.CreateClient();

    var response = await client.PostAsJsonAsync("/webhook", new
    {
        license_plate = "XXX0000", event_type = "UNKNOWN"
    });

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

Testa o `switch` de despacho do `WebhookController` (ver documentação de API, seção 5.1) através da pilha HTTP real — confirma que um `event_type` desconhecido retorna `400`, sem sequer acionar o MediatR.

---

## 8. Relação entre os Tipos de Teste

| O que é testado | Domain.Tests | Application.Tests | Integration.Tests |
|------------------|:---:|:---:|:---:|
| Preço dinâmico e cálculo de cobrança (guards, invariantes) | ✅ | — | — |
| Orquestração dos Handlers (fluxo, correlação de repositórios) | — | ✅ | — |
| Validação de entrada (Validators) | — | ✅ | ✅ |
| Persistência real (EF Core + SQL Server, `SaveChangesAsync`) | — | — | ✅ |
| Sincronização com o simulador (via fake) | — | ✅ (mock) | ✅ (fake) |
| API HTTP (status codes, roteamento de `event_type`) | — | — | ✅ |
| Middleware de exceções | — | — | ✅ |
| Fluxo end-to-end completo (ENTRY → PARKED → EXIT → revenue) | — | — | ✅ |

---

## 9. Convenções de Nomenclatura dos Testes

```
[Método]_[Resultado esperado]_[Condição]
```

Exemplos deste projeto:
- `IniciarEntrada_DeveAplicarDesconto10Porcento_QuandoLotacaoAbaixoDe25`
- `RegistrarSaida_DeveUsarMultiplicadorDaEntrada_NaoODaSaida`
- `Handle_DeveRetornarNotFound_QuandoSessaoAtivaNaoExiste`
- `FluxoCompleto_EntradaEstacionamentoSaida_DeveRefletirNaReceita`

O nome do teste é sua própria documentação — ao ler a saída do `dotnet test`, fica imediatamente claro o que cada teste valida, sem abrir o arquivo.

---

## 10. Como Executar os Testes

### Pré-requisito para Integration Tests

`ParkingManagement.Integration.Tests` requer **Docker em execução** — o Testcontainers sobe um container SQL Server automaticamente.

### Executar todas as suítes

```bash
dotnet test
```

### Por suíte

```bash
dotnet test tests/ParkingManagement.Domain.Tests
dotnet test tests/ParkingManagement.Application.Tests
dotnet test tests/ParkingManagement.Integration.Tests
```

### Resultado obtido nesta solução

| Suíte | Testes | Tipo |
|-------|--------|------|
| `ParkingManagement.Domain.Tests` | 56 | Unitários — sem mocks, sem banco |
| `ParkingManagement.Application.Tests` | 38 | Unitários — mocks de repositórios/UnitOfWork/simulador |
| `ParkingManagement.Integration.Tests` | 2 | E2E — API real + SQL Server real em container |
| **Total** | **96** | |

---

## 11. Resumo das Decisões Técnicas

### 11.1 Decisões comuns a todas as suítes

| Decisão | Alternativa | Por que escolhemos assim |
|---------|-------------|--------------------------|
| xUnit + FluentAssertions + Bogus | MSTest/NUnit, `Assert` nativo, dados fixos | Padrão de mercado; mensagens de erro mais descritivas; dados aleatórios revelam bugs que valores fixos não revelariam |
| AAA em todos os testes | Sem separação de fases | Clareza estrutural e facilidade de diagnóstico de falhas |

### 11.2 Decisões do `Domain.Tests` e `Application.Tests`

| Decisão | Alternativa | Por que escolhemos assim |
|---------|-------------|--------------------------|
| NSubstitute para mocking (incluindo `IUnitOfWork` e `IGarageSimulatorClient`) | Moq | Sintaxe mais fluente; `Received(1)`/`DidNotReceive()` mais legíveis que `Verify(..., Times.Once())` |
| Testes de fronteira nos limites do multiplicador (50%, 75%) | Testar só valores "no meio" das faixas | Erros de `<` vs `<=` se escondem exatamente nos limites |
| 91 minutos como cenário padrão de cobrança | 120 minutos (número redondo) | Distingue `Math.Ceiling` de `Math.Round` — 120 min não distinguiria as duas estratégias |
| Verificar `spot.Status` além do `Result` do Handler | Verificar só o retorno | Protege contra regressão em que uma das duas mutações (sessão + vaga) deixe de ser chamada |

### 11.3 Decisões do `Integration.Tests`

| Decisão | Alternativa | Por que escolhemos assim |
|---------|-------------|--------------------------|
| `Testcontainers.MsSql` (SQL Server real) | Provider `InMemory` do EF Core | `InMemory` não valida constraints, tipos nem *owned types* reais — daria falso positivo |
| `FakeGarageSimulatorClient` local | Apontar para um simulador real em CI | O simulador é um serviço de terceiros, indisponível no ambiente de testes automatizados |
| Coordenadas do fake expostas como constantes públicas | Hardcoded separadamente no fake e no teste | Garante que fake e teste nunca divirjam nas mesmas coordenadas usadas para correlação de vaga |
| `ConfigureAppConfiguration` sobrescrevendo chaves de configuração | `ConfigureServices` removendo/recriando `DbContextOptions` manualmente | Mais simples, já que `AddInfrastructure` lê a connection string via `IConfiguration` no momento certo do pipeline |
| `ICollectionFixture` compartilhando o container | Nova instância por classe de teste | Subir um container SQL Server é caro; compartilhar reduz o tempo total da suíte |
| Asserção "maior que zero" na receita do fluxo E2E | Valor exato (`20m`) | Evita acoplamento à ordem de execução entre testes que compartilham o mesmo container; o valor exato já é validado isoladamente no Domain.Tests |
| `public partial class Program;` na Api | Projeto de testes referenciar a Api de outra forma | Necessário para `WebApplicationFactory<Program>` funcionar a partir de outro assembly |
