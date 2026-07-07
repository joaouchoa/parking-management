# Documentação Técnica — Camada de Migrations (`ParkingManagement.Infrastructure.Migrations`)

> **Objetivo deste documento:** explicar em profundidade cada decisão conceitual e técnica tomada na construção da camada de migrations do sistema de Gestão de Estacionamento. Serve como referência para qualquer desenvolvedor que precise entender como o banco SQL Server é criado, versionado e evoluído ao longo do tempo.

---

## 1. O Problema que as Migrations Resolvem

Sem um mecanismo de migrations, evoluir o schema de um banco em produção de forma segura e repetível é praticamente impossível — rodar SQL manualmente não deixa histórico, recriar o banco perde dados, e cada ambiente (dev, homologação, produção) tende a divergir silenciosamente.

**Migrations resolvem isso:** são scripts SQL versionados e numerados, aplicados **sequencialmente e uma única vez** em cada ambiente. O sistema rastreia o que já foi aplicado e executa apenas o que falta.

---

## 2. Por que DbUp e não EF Core Migrations?

Mesma decisão do projeto de referência (`gestao-faturas`), replicada aqui como parte da arquitetura a ser seguida:

| Critério | EF Core Migrations | DbUp ✅ |
|----------|--------------------|---------|
| **Formato dos scripts** | C# gerado automaticamente | SQL puro |
| **Controle do SQL** | Indireto (EF gera o SQL) | Total (você escreve o SQL) |
| **Revisão de código** | Difícil de revisar | Fácil de revisar no PR |
| **Portabilidade** | Acoplado ao EF Core | Independente de ORM |
| **Idempotência** | Controlada pelo EF | Controlada por você (`IF NOT EXISTS`) |

> **Importante:** o EF Core ainda é usado para **leitura e escrita** (queries e `SaveChanges`, ver documentação de Infrastructure). O DbUp cuida apenas da **estrutura** (criação de schema, tabelas, índices). São ferramentas complementares.

**Diferença em relação ao `gestao-faturas`:** lá, o uso de PostgreSQL + DbUp era uma **premissa documentada** (substituição do SQL Server sugerido pelo enunciado). Aqui, o banco já é o sugerido pelo enunciado (SQL Server) — a escolha de manter o DbUp, em vez de usar `dotnet ef migrations`, é puramente uma decisão de **arquitetura consistente entre os dois projetos**, não uma substituição de tecnologia de banco.

---

## 3. Estrutura do Projeto

```
ParkingManagement.Infrastructure.Migrations/
├── Scripts/
│   ├── 0001_create_schema.sql                   ← Cria o schema `parking`
│   ├── 0002_create_table_sectors.sql             ← Tabela de setores
│   ├── 0003_create_table_spots.sql               ← Tabela de vagas
│   ├── 0004_create_table_parking_sessions.sql    ← Tabela de sessões de estacionamento
│   └── 0005_indexes.sql                          ← Índices de performance
├── Program.cs                                    ← Runner que executa o DbUp
└── ParkingManagement.Infrastructure.Migrations.csproj
```

**Por que projeto separado (com `OutputType=Exe`), e não uma classe dentro da Api?** Permite rodar as migrations independentemente da API subir — essencial no `docker-compose`, onde o serviço `migrations` roda, aplica os scripts e **encerra** (`service_completed_successfully`), só então liberando a API para iniciar. CI/CD também pode executar esse projeto como uma etapa isolada do pipeline.

---

## 4. A Convenção de Nomenclatura dos Scripts

```
0001_create_schema.sql
0002_create_table_sectors.sql
0003_create_table_spots.sql
0004_create_table_parking_sessions.sql
0005_indexes.sql
```

O prefixo numérico com zeros à esquerda garante ordem de execução correta mesmo com centenas de scripts futuros. **Regra de ouro:** um script já executado em produção nunca é modificado — evoluções viram um novo script com o próximo número.

---

## 5. Os Scripts SQL em Detalhe

### 5.1 `0001_create_schema.sql`

```sql
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'parking')
BEGIN
    EXEC('CREATE SCHEMA parking');
END
```

**Por que um schema próprio (`parking`) em vez do `dbo` padrão do SQL Server?** Documenta explicitamente, no próprio banco, que estas tabelas pertencem à aplicação de gestão de estacionamento — importante se este banco físico vier a ser compartilhado com outros sistemas no futuro. Este é o mesmo schema referenciado em todas as `IEntityTypeConfiguration<T>` da Infrastructure (`builder.ToTable("...", "parking")`) — os dois lados **precisam bater exatamente**, já que o DbUp cria a estrutura e o EF Core apenas a consome.

**Por que `EXEC('CREATE SCHEMA parking')` dentro de um `IF NOT EXISTS`, e não `CREATE SCHEMA IF NOT EXISTS` direto (como no PostgreSQL do projeto de referência)?** O T-SQL do SQL Server **não suporta** a sintaxe `CREATE SCHEMA IF NOT EXISTS` — `CREATE SCHEMA` precisa ser a única instrução do batch em muitas versões do engine. A forma idiomática de obter idempotência em T-SQL é checar a existência via `sys.schemas` e só então executar o `CREATE SCHEMA` dinamicamente com `EXEC(...)`, contornando a restrição de "instrução única no batch".

### 5.2 `0002_create_table_sectors.sql`

```sql
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sectors' AND schema_id = SCHEMA_ID('parking'))
BEGIN
    CREATE TABLE parking.Sectors
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Code NVARCHAR(10) NOT NULL,
        BasePrice DECIMAL(18,2) NOT NULL,
        MaxCapacity INT NOT NULL
    );
END
```

| Coluna | Tipo | Por que esse tipo |
|--------|------|-------------------|
| `Id` | `UNIQUEIDENTIFIER` | Identidade gerada pela aplicação (`Guid.NewGuid()`), sem depender de `IDENTITY`/sequência do banco |
| `Code` | `NVARCHAR(10)` | Códigos de setor do simulador são curtos (`"A"`, `"B"`...); `NVARCHAR` suporta Unicode, adequado mesmo que o desafio use apenas letras ASCII |
| `BasePrice` | `DECIMAL(18,2)` | Precisão decimal exata — mesmo raciocínio de qualquer valor monetário: `FLOAT`/`REAL` introduziriam erro de arredondamento |
| `MaxCapacity` | `INT` | Contagem de vagas, sempre um número inteiro não negativo |

**Por que não há `CONSTRAINT UNIQUE` em `Code` já neste script?** Índices (incluindo os únicos) ficam centralizados no script `0005_indexes.sql` — ver seção 4 do documento de Infrastructure e a justificativa na seção 5.4 abaixo.

### 5.3 `0003_create_table_spots.sql`

```sql
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Spots' AND schema_id = SCHEMA_ID('parking'))
BEGIN
    CREATE TABLE parking.Spots
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ExternalId BIGINT NOT NULL,
        SectorCode NVARCHAR(10) NOT NULL,
        Latitude FLOAT NOT NULL,
        Longitude FLOAT NOT NULL,
        Status NVARCHAR(20) NOT NULL
    );
END
```

| Coluna | Tipo | Por que esse tipo |
|--------|------|-------------------|
| `ExternalId` | `BIGINT` | Espelha o `"id": 1` retornado por `GET /garage` — `long` no C#, `BIGINT` no banco, para não limitar a faixa de valores que o simulador possa usar |
| `Latitude`/`Longitude` | `FLOAT` | Coordenadas geográficas são naturalmente valores de ponto flutuante — não há necessidade de precisão decimal exata como em valores monetários, e `FLOAT` (double-precision) tem resolução mais que suficiente para coordenadas de GPS |
| `Status` | `NVARCHAR(20)` | Reflete a decisão de mapear o enum `SpotStatus` como string no EF Core (`HasConversion<string>()`) — ver documentação de Infrastructure |

**Por que não existe uma `FOREIGN KEY` explícita de `Spots.SectorCode` para `Sectors.Code`?** O `SectorCode` em `Spots` é preenchido diretamente a partir do JSON do simulador (`GET /garage` retorna `spots` e `garage` como duas listas paralelas, correlacionadas por código de setor, não por uma FK de banco). Adicionar uma FK aqui exigiria garantir que todo `Sector` seja inserido **antes** de qualquer `Spot` do mesmo setor dentro do mesmo processo de sincronização (`SyncGarageHandler`, ver documentação de Application) — o que já é o comportamento observado, mas não é reforçado no schema para manter a sincronização simples e tolerante à ordem exata dos dois loops de upsert.

### 5.4 `0004_create_table_parking_sessions.sql`

```sql
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ParkingSessions' AND schema_id = SCHEMA_ID('parking'))
BEGIN
    CREATE TABLE parking.ParkingSessions
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        LicensePlate NVARCHAR(8) NOT NULL,
        EntryTime DATETIME2 NOT NULL,
        OccupancyPercentageAtEntry DECIMAL(5,2) NOT NULL,
        PriceMultiplier DECIMAL(5,2) NOT NULL,
        ParkedAt DATETIME2 NULL,
        ParkedLatitude FLOAT NULL,
        ParkedLongitude FLOAT NULL,
        SpotId UNIQUEIDENTIFIER NULL,
        SectorCode NVARCHAR(10) NULL,
        ExitTime DATETIME2 NULL,
        AmountCharged DECIMAL(18,2) NULL,
        Status NVARCHAR(20) NOT NULL
    );
END
```

Esta é a tabela mais rica do sistema — reflete diretamente a estrutura do aggregate root `ParkingSession` (ver documentação de Domínio, seção 5.4), inclusive nas colunas dos três Value Objects mapeados como *owned types*: `LicensePlate` (uma coluna), `PricingSnapshot` (duas colunas: `OccupancyPercentageAtEntry`, `PriceMultiplier`) e o VO de coordenada da posição de estacionamento (duas colunas: `ParkedLatitude`, `ParkedLongitude`).

**Por que tantas colunas `NULL` (`ParkedAt`, `SpotId`, `SectorCode`, `ExitTime`, `AmountCharged`)?** Porque a tabela armazena o agregado nas **três fases de vida** que ele pode assumir (`Entrou`, `Estacionado`, `Finalizado`) — mesma decisão de design do domínio, refletida fielmente no schema físico. Uma sessão recém-criada (`ENTRY`) legitimamente ainda não tem vaga, setor ou horário de saída.

**Por que `LicensePlate NVARCHAR(8)` e não um tamanho maior?** A regra do domínio (`LicensePlate`, ver documentação de Domínio, seção 5.1) limita placas a `[A-Z0-9]{5,8}` — 8 caracteres é o máximo possível após normalização, então o `NVARCHAR(8)` é exatamente o suficiente, sem desperdício.

**Por que `DECIMAL(5,2)` para `OccupancyPercentageAtEntry` e `PriceMultiplier`, mas `DECIMAL(18,2)` para `AmountCharged`?** Percentuais de ocupação (`0` a `100`) e multiplicadores de preço (`0.90` a `1.25`) nunca excedem 3 dígitos antes da vírgula — `DECIMAL(5,2)` (até `999.99`) é suficiente e mais compacto. `AmountCharged` é um valor monetário que, em tese, poderia crescer bem mais (permanências muito longas, tarifas altas) — o padrão `DECIMAL(18,2)` usado para dinheiro em todo o restante do sistema se mantém aqui.

### 5.5 `0005_indexes.sql`

```sql
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Sectors_Code')
    CREATE UNIQUE INDEX IX_Sectors_Code ON parking.Sectors (Code);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Spots_ExternalId')
    CREATE UNIQUE INDEX IX_Spots_ExternalId ON parking.Spots (ExternalId);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ParkingSessions_LicensePlate')
    CREATE INDEX IX_ParkingSessions_LicensePlate ON parking.ParkingSessions (LicensePlate);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ParkingSessions_Status')
    CREATE INDEX IX_ParkingSessions_Status ON parking.ParkingSessions (Status);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ParkingSessions_SectorCode_ExitTime')
    CREATE INDEX IX_ParkingSessions_SectorCode_ExitTime ON parking.ParkingSessions (SectorCode, ExitTime);
```

**Por que índices separados num script próprio, e não junto com o `CREATE TABLE`?** Índices têm custo de escrita — cada `INSERT`/`UPDATE` precisa atualizá-los. Mantê-los isolados facilita revisão e eventual ajuste futuro sem tocar na definição da tabela.

**Mapeamento de cada índice para o caso de uso que ele acelera:**

| Índice | Acelera |
|--------|---------|
| `IX_Sectors_Code` (único) | Impede setores duplicados; usado por `GetByCodeAsync` a cada `RegisterEntry`/`RegisterExit`/`GetRevenue` |
| `IX_Spots_ExternalId` (único) | Impede vagas duplicadas na sincronização (`SyncGarage` faz *upsert* por `ExternalId`) |
| `IX_ParkingSessions_LicensePlate` | Acelera `GetActiveByLicensePlateAsync` — chamado a **cada evento de webhook** (`PARKED`, `EXIT`) |
| `IX_ParkingSessions_Status` | Acelera filtros por status (usado internamente pelo `GetActiveByLicensePlateAsync`, que exclui `Finalizado`) |
| `IX_ParkingSessions_SectorCode_ExitTime` (composto) | Acelera diretamente `GetRevenueAsync` — a query de `GET /revenue` filtra por exatamente essas duas colunas |

**Por que `IX_ParkingSessions_LicensePlate` não é único?** Porque uma mesma placa pode, legitimamente, ter **múltiplas sessões ao longo do tempo** (o carro entra e sai várias vezes ao longo de dias diferentes) — o que a regra de negócio impede é **duas sessões simultaneamente ativas**. Essa invariante é garantida pela lógica de aplicação (`GetActiveByLicensePlateAsync` + os guards do domínio), não por uma constraint de unicidade no banco, que seria incorreta aqui.

---

## 6. O Runner (`Program.cs`)

```csharp
public static class Program
{
    public static int Main(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? Environment.GetEnvironmentVariable("SQLSERVER_CONN")
            ?? throw new InvalidOperationException(
                "Informe a connection string via argumento ou variável de ambiente SQLSERVER_CONN.");

        EnsureDatabase.For.SqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(result.Error);
            Console.ResetColor();
            return -1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Migrations aplicadas com sucesso.");
        Console.ResetColor();
        return 0;
    }
}
```

**`args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("SQLSERVER_CONN") ?? throw`** — mesma filosofia do `gestao-faturas`: a connection string vem de linha de comando ou variável de ambiente, nunca hardcoded. Se nenhuma for informada, o programa falha imediatamente com mensagem clara.

**`EnsureDatabase.For.SqlDatabase(connectionString)`** — cria o banco de dados (`ParkingManagement`) se ele ainda não existir, eliminando a necessidade de provisionamento manual antes da primeira execução — importante para o `docker-compose`, onde o container do SQL Server sobe "vazio".

**`WithScriptsEmbeddedInAssembly`** — os 5 scripts SQL viajam embutidos dentro do `.dll` compilado (ver `EmbeddedResource` no `.csproj`, seção 7), não como arquivos soltos no disco — funciona identicamente em qualquer ambiente, incluindo containers.

**`public static class Program` com `Main` explícito, em vez de top-level statements** — diferente do `Program.cs` da Api (que usa top-level statements + `public partial class Program;` para ser acessível aos testes), este projeto console não precisa ser referenciado por nenhum projeto de teste como tipo genérico — não há necessidade da mesma ginástica de visibilidade. Uma classe `Program` explícita com `Main` é a forma mais direta e legível para um executável simples de linha de comando.

**`result.Successful` e o código de saída (`return -1` / `return 0`)** — o código de saída do processo é o que o `docker-compose` (e qualquer pipeline de CI/CD) usa para decidir se a etapa de migrations foi bem-sucedida. Um código diferente de zero interrompe a subida dos serviços dependentes (`condition: service_completed_successfully`).

---

## 7. `EmbeddedResource` no `.csproj`

```xml
<ItemGroup>
    <PackageReference Include="dbup-sqlserver" Version="7.2.0" />
</ItemGroup>

<ItemGroup>
    <EmbeddedResource Include="Scripts\*.sql" />
</ItemGroup>
```

**`dbup-sqlserver`, não `dbup-postgresql` (usado no `gestao-faturas`)** — é o provider do DbUp específico para T-SQL/SQL Server, responsável por gerar a tabela de controle `SchemaVersions` com a sintaxe correta do engine e por rodar cada script dentro de uma transação usando os comandos apropriados ao SQL Server.

O glob `Scripts\*.sql` inclui automaticamente qualquer novo script adicionado à pasta — nenhuma edição manual do `.csproj` é necessária ao criar `0006_...sql` no futuro.

---

## 8. A Tabela `SchemaVersions` (Controle de Versão do DbUp)

Ao rodar pela primeira vez contra um banco novo, o DbUp cria automaticamente:

```sql
CREATE TABLE SchemaVersions (
    Id           INT IDENTITY PRIMARY KEY,
    ScriptName   NVARCHAR(255) NOT NULL,
    Applied      DATETIME      NOT NULL
);
```

Cada script executado gera um registro nesta tabela. Nas execuções seguintes, o DbUp compara os scripts embutidos no assembly contra o que já consta em `SchemaVersions` e só aplica os que ainda faltam — tornando o processo **idempotente**: seguro de rodar múltiplas vezes, em qualquer ambiente, sem duplicar efeitos.

---

## 9. Como Adicionar uma Nova Migration

1. Criar `Scripts/0006_<descrição>.sql` com o próximo número sequencial.
2. Escrever SQL idempotente (`IF NOT EXISTS` para T-SQL, seguindo o padrão desta pasta).
3. O arquivo é automaticamente incluído como `EmbeddedResource` pelo glob já configurado.
4. Na próxima execução do runner, apenas o novo script é aplicado nos ambientes que ainda não o têm.

**Nunca modificar um script já aplicado em qualquer ambiente compartilhado** — correções viram um novo script.

---

## 10. Comandos de Referência

### Rodar as migrations localmente

```powershell
dotnet run --project src/ParkingManagement.Infrastructure.Migrations `
  -- "Server=localhost,1433;Database=ParkingManagement;User Id=sa;Password=Your_password123;TrustServerCertificate=True;"
```

### Via variável de ambiente

```powershell
$env:SQLSERVER_CONN = "Server=localhost,1433;Database=ParkingManagement;User Id=sa;Password=Your_password123;TrustServerCertificate=True;"
dotnet run --project src/ParkingManagement.Infrastructure.Migrations
```

### Verificar tabelas criadas (via `sqlcmd`)

```powershell
sqlcmd -S localhost,1433 -U sa -P Your_password123 -d ParkingManagement -Q "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES"
```

### Verificar scripts aplicados

```powershell
sqlcmd -S localhost,1433 -U sa -P Your_password123 -d ParkingManagement `
  -Q "SELECT ScriptName, Applied FROM SchemaVersions ORDER BY Id"
```

---

## 11. Diagrama da Estrutura no Banco

```
┌────────────────────────────────────────────────────────────────────┐
│                    BANCO: ParkingManagement                        │
│                       schema: parking                              │
│                                                                     │
│  ┌────────────────────────────────┐                                │
│  │        parking.Sectors         │                                │
│  │  ──────────────────────────    │                                │
│  │  Id           UNIQUEIDENTIFIER │                                │
│  │  Code         NVARCHAR(10) UQ  │                                │
│  │  BasePrice    DECIMAL(18,2)    │                                │
│  │  MaxCapacity  INT              │                                │
│  └────────────────────────────────┘                                │
│                                                                     │
│  ┌────────────────────────────────┐                                │
│  │        parking.Spots           │                                │
│  │  ──────────────────────────    │                                │
│  │  Id           UNIQUEIDENTIFIER │                                │
│  │  ExternalId   BIGINT UQ        │                                │
│  │  SectorCode   NVARCHAR(10)     │                                │
│  │  Latitude     FLOAT            │                                │
│  │  Longitude    FLOAT            │                                │
│  │  Status       NVARCHAR(20)     │                                │
│  └────────────────────────────────┘                                │
│                                                                     │
│  ┌──────────────────────────────────────┐                          │
│  │      parking.ParkingSessions         │                          │
│  │  ──────────────────────────────────  │                          │
│  │  Id                        UNIQUEIDENTIFIER                     │
│  │  LicensePlate              NVARCHAR(8)   [idx]                  │
│  │  EntryTime                 DATETIME2                            │
│  │  OccupancyPercentageAtEntry DECIMAL(5,2)                        │
│  │  PriceMultiplier           DECIMAL(5,2)                         │
│  │  ParkedAt                  DATETIME2  NULL                      │
│  │  ParkedLatitude/Longitude  FLOAT      NULL                      │
│  │  SpotId                    UNIQUEIDENTIFIER NULL                │
│  │  SectorCode                NVARCHAR(10) NULL  [idx composto]    │
│  │  ExitTime                  DATETIME2  NULL     [idx composto]   │
│  │  AmountCharged             DECIMAL(18,2) NULL                   │
│  │  Status                    NVARCHAR(20)        [idx]            │
│  └──────────────────────────────────────┘                          │
│                                                                     │
│  ┌────────────────────────────────┐                                │
│  │      dbo.SchemaVersions        │  ← Controle DbUp (schema dbo) │
│  │  Id, ScriptName, Applied       │                                │
│  └────────────────────────────────┘                                │
└────────────────────────────────────────────────────────────────────┘
```

---

## 12. Resumo das Decisões Técnicas

| Decisão | Alternativa | Por que escolhemos assim |
|---------|-------------|--------------------------|
| DbUp com SQL puro (T-SQL) | EF Core Migrations | Consistência arquitetural com o `gestao-faturas`; controle total do SQL, revisável por qualquer DBA |
| Schema `parking` dedicado | Schema `dbo` padrão | Isola as tabelas da aplicação; precisa bater exatamente com o `ToTable(..., "parking")` do EF Core |
| `EXEC('CREATE SCHEMA ...')` dentro de `IF NOT EXISTS` | `CREATE SCHEMA IF NOT EXISTS` | T-SQL não aceita `CREATE SCHEMA` combinado com outras instruções no mesmo batch |
| Índices em script separado (`0005`) | Índices junto do `CREATE TABLE` | Facilita revisão e ajuste futuro de performance sem tocar na definição da tabela |
| `IX_ParkingSessions_LicensePlate` não único | Constraint `UNIQUE` na placa | Uma placa pode ter várias sessões ao longo do tempo; só uma sessão *ativa* por vez é a regra, garantida pela aplicação |
| Sem FK entre `Spots.SectorCode` e `Sectors.Code` | Foreign key explícita | Os dois vêm de listas paralelas no JSON do simulador, sincronizadas no mesmo processo, sem garantia de ordem de inserção reforçada pelo schema |
| Connection string via arg ou env var | Hardcoded no código | Segurança — credenciais nunca no código-fonte, mesmo padrão do projeto de referência |
| Runner com classe `Program`/`Main` explícitos | Top-level statements | Projeto console simples, sem necessidade de ser referenciado como tipo genérico por testes |
