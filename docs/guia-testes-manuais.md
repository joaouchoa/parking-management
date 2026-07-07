# Guia de Testes Manuais — ParkingManagement

> **Objetivo deste guia:** te ensinar a subir o sistema do zero na sua máquina e testar, na mão (Swagger, `curl`/`Invoke-RestMethod`), o fluxo completo do desafio — sincronização da garagem, entrada, estacionamento, saída e consulta de receita — entendendo *por que* cada resposta é o que é. Não é um script para copiar e colar sem pensar; é para você acompanhar os números e verificar se batem com a regra de negócio.

---

## 0. O problema que este guia resolve: não temos o simulador real

O enunciado do desafio pressupõe que existe um **simulador** rodando, que expõe `GET /garage` e envia eventos para o nosso `POST /webhook`. Esse simulador é uma peça externa da Estapar que não temos disponível neste ambiente.

Para testar manualmente sem ele, este guia te ensina duas formas de popular a garagem (setores/vagas), e depois, em qualquer uma delas, fazer **você mesmo**, manualmente, o papel do simulador — enviando os eventos `ENTRY`, `PARKED` e `EXIT` para `POST /webhook` via Swagger ou `Invoke-RestMethod`.

| Opção | Como popula a garagem | Quando usar |
|-------|------------------------|-------------|
| **A — Stub do simulador** (seção 4, Opção A) | Sobe um servidor HTTP minúsculo em PowerShell que responde `GET /garage`; a API o consome automaticamente no boot (`GarageSyncStartupService`) ou via `POST /garage/sync` | Quando você quer validar de verdade a **RN-1** (sincronização automática no startup) — é o caminho mais fiel ao que o simulador real faria |
| **B — `POST /garage/seed`** (seção 4, Opção B) | Chama um endpoint da própria API passando a configuração da garagem direto no corpo da requisição — sem nenhum servidor HTTP externo | Quando você só quer testar rapidamente o webhook e a receita, sem manter um terceiro terminal aberto |

Isso é exatamente o que o `GarageSyncStartupService` da API espera encontrar no startup (ver `docs/documentacao-tecnica-api.md`, seção 8) — ele não sabe (nem precisa saber) se está falando com o simulador real ou com o nosso stub. A Opção B (`/garage/seed`) nem passa por ele — é um caminho independente, documentado em `docs/documentacao-tecnica-api.md`, seção 8.2.2.

---

## 1. Pré-requisitos

| Ferramenta | Por quê |
|------------|---------|
| .NET SDK 8 (ou superior, com suporte a `net8.0`) | Compilar e rodar a API e o projeto de Migrations |
| Docker Desktop (rodando) | Subir o SQL Server em container |
| PowerShell | Rodar o stub do simulador e disparar as requisições de teste |

Confirme que o Docker está de pé antes de continuar:

```powershell
docker info
```

---

## 2. Passo 1 — Subir o SQL Server

```powershell
docker run -d --name parking-sqlserver `
  -e "ACCEPT_EULA=Y" `
  -e "MSSQL_SA_PASSWORD=Your_password123" `
  -p 1433:1433 `
  mcr.microsoft.com/mssql/server:2022-latest
```

Aguarde alguns segundos para o SQL Server terminar de inicializar (a primeira subida demora mais, pois baixa a imagem). Você pode acompanhar com:

```powershell
docker logs -f parking-sqlserver
```

Procure pela linha `SQL Server is now ready for client connections` antes de seguir para o próximo passo.

> A senha `Your_password123` é a mesma já configurada em `src/ParkingManagement.Api/appsettings.json` (`ConnectionStrings:SqlServer`). Se você mudar a senha aqui, mude também lá.

---

## 3. Passo 2 — Rodar as Migrations

Com o SQL Server no ar, aplique o schema (`parking.Sectors`, `parking.Spots`, `parking.ParkingSessions` e índices — ver `docs/documentacao-tecnica-migrations.md`):

```powershell
dotnet run --project src/ParkingManagement.Infrastructure.Migrations `
  -- "Server=localhost,1433;Database=ParkingManagement;User Id=sa;Password=Your_password123;TrustServerCertificate=True;"
```

Saída esperada: uma lista dos 5 scripts (`0001_...` a `0005_...`) sendo aplicados, terminando em **"Migrations aplicadas com sucesso."**

Se quiser conferir visualmente que as tabelas existem:

```powershell
sqlcmd -S localhost,1433 -U sa -P "Your_password123" -C -d ParkingManagement -Q "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES"
```

---

## 4. Passo 3 — Popular a garagem (escolha a Opção A ou B)

### Opção A — Subir o stub do simulador (`GET /garage`)

Use esta opção se quiser ver a **sincronização automática no boot** (RN-1) acontecendo de verdade, ou testar `POST /garage/sync`.

Abra **um novo terminal PowerShell** (vai ficar ocupado rodando o stub) e cole o script abaixo. Ele sobe um servidor HTTP mínimo na porta `3000`, respondendo `GET /garage` com **1 setor (`A`) e 2 vagas** — números pequenos de propósito, para tornar fácil provocar a garagem cheia (RN-3/RN-8) mais adiante.

```powershell
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:3000/")
$listener.Start()
Write-Host "Stub do simulador ouvindo em http://localhost:3000 (Ctrl+C para parar)" -ForegroundColor Green

$json = @'
{
  "garage": [
    { "sector": "A", "basePrice": 10.0, "max_capacity": 2 }
  ],
  "spots": [
    { "id": 1, "sector": "A", "lat": -23.561684, "lng": -46.655981 },
    { "id": 2, "sector": "A", "lat": -23.561700, "lng": -46.656000 }
  ]
}
'@
$buffer = [System.Text.Encoding]::UTF8.GetBytes($json)

while ($listener.IsListening) {
    $context = $listener.GetContext()
    $response = $context.Response
    $response.ContentType = "application/json"
    $response.ContentLength64 = $buffer.Length
    $response.OutputStream.Write($buffer, 0, $buffer.Length)
    $response.OutputStream.Close()
}
```

Deixe essa janela aberta e rodando durante todo o resto do guia — é ela que faz o papel de `GET /garage` do simulador real (ver `IGarageSimulatorClient`/`GarageSimulatorClient`, documentados em `docs/documentacao-tecnica-infrastructure.md`).

> **Guarde as duas coordenadas** (`-23.561684, -46.655981` e `-23.561700, -46.656000`) — você vai usá-las manualmente no evento `PARKED`, no papel do simulador informando "o carro estacionou aqui".

### Opção B — `POST /garage/seed` (mais rápido, sem terceiro terminal)

Use esta opção se só quiser testar o webhook e a receita, sem manter um stub rodando à parte. Ela **não precisa** do passo acima — pule direto para a seção 5 (rodar a API) e, com a API já de pé, chame o endpoint abaixo pelo Swagger ou `Invoke-RestMethod`:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5089/garage/seed" -ContentType "application/json" -Body (@{
    garage = @(
        @{ sector = "A"; basePrice = 10.0; max_capacity = 2 }
    )
    spots = @(
        @{ id = 1; sector = "A"; lat = -23.561684; lng = -46.655981 }
        @{ id = 2; sector = "A"; lat = -23.561700; lng = -46.656000 }
    )
} | ConvertTo-Json -Depth 5)
```

Resposta esperada: `200 OK` com `{ "sectorsUpserted": 1, "spotsUpserted": 2 }` — a mesma configuração (1 setor, 2 vagas, mesmas coordenadas) que a Opção A traria, só que sem nenhum servidor HTTP externo. Veja `docs/documentacao-tecnica-api.md`, seção 8.2.2, para o porquê deste endpoint existir.

> **Diferença importante em relação à Opção A:** com a Opção B, o log de boot da API (seção 5) vai mostrar o aviso `"Não foi possível sincronizar..."` mesmo assim — é esperado, porque o `GarageSyncStartupService` roda **antes** de você conseguir chamar `/garage/seed` (que só existe depois que a API já subiu). O aviso não impede nada; a garagem fica populada assim que você chama o endpoint.

---

## 5. Passo 4 — Rodar a API

Em um **terceiro terminal** (ou o segundo, se você escolheu a Opção B e pulou o stub):

```powershell
dotnet run --project src/ParkingManagement.Api
```

A configuração padrão (`appsettings.json`) já aponta `Simulator:BaseUrl` para `http://localhost:3000` — exatamente onde o stub da Opção A está ouvindo (se você escolheu a Opção A).

**O que observar no console:**

- **Opção A** (stub rodando): `"Garagem sincronizada: 1 setores e 2 vagas."` — a sincronização automática (RN-1) funcionou.
- **Opção B** (sem stub): `"Não foi possível sincronizar a configuração da garagem com o simulador (...)"` — **esperado**, já que não há nada respondendo em `Simulator:BaseUrl` ainda. Prossiga chamando `POST /garage/seed` (Opção B acima) antes de enviar qualquer evento de webhook.

Se a aplicação **encerrar sozinha logo após subir** (diferente do aviso acima, que é só um log e a API continua de pé), veja a seção 9, "Solução de problemas".

Com a API de pé, o navegador deve abrir automaticamente no Swagger (`https://localhost:xxxx/swagger` — a porta exata aparece no console). É por ali que você pode disparar as requisições manualmente pela UI, se preferir a `Invoke-RestMethod` usada neste guia.

---

## 6. Roteiro guiado — validando cada regra de negócio com números reais

Pré-requisito: a garagem já precisa estar populada (1 setor `A`, 2 vagas) — via Opção A (stub) ou Opção B (`POST /garage/seed`), seção 4.

Abra mais um terminal (além do da API, e do stub se você tiver escolhido a Opção A). É aqui que você faz manualmente o papel do simulador, enviando os três eventos do webhook.

### 6.1 Veículo A entra — lotação 0%, desconto de 10%

```powershell
$respA = Invoke-RestMethod -Method Post -Uri "http://localhost:5089/webhook" -ContentType "application/json" -Body (@{
    license_plate = "ABC1234"
    entry_time    = "2026-01-01T08:00:00Z"
    event_type    = "ENTRY"
} | ConvertTo-Json)
```

> Ajuste a porta (`5089`) para a que apareceu no console da API (`ASPNETCORE_URLS`/perfil `http`).

**Por que isso deve retornar `200 OK` (sem corpo)?** A garagem tem 2 vagas e nenhuma está ocupada ainda (`0/2 = 0%` de lotação). Como `0% < 25%`, o `PricingSnapshot` calculado internamente aplica **desconto de 10%** (multiplicador `0.90`) — mas isso não aparece na resposta do webhook (o enunciado só exige `200` em caso de sucesso); ele fica "congelado" dentro da sessão até a saída (ver `docs/documentacao-tecnica-dominio.md`, seção 5.2).

### 6.2 Veículo A estaciona na vaga 1

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5089/webhook" -ContentType "application/json" -Body (@{
    license_plate = "ABC1234"
    lat           = -23.561684
    lng           = -46.655981
    event_type    = "PARKED"
} | ConvertTo-Json)
```

**O que aconteceu por trás:** a API recebeu as coordenadas, encontrou (por *match* exato) a vaga cadastrada com essas coordenadas exatas (a vaga `1`, sincronizada do stub), marcou-a como `Ocupada` e gravou `SectorCode = "A"` na sessão do veículo `ABC1234`.

### 6.3 Veículo B entra — lotação agora é 50%, preço normal

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5089/webhook" -ContentType "application/json" -Body (@{
    license_plate = "XYZ9999"
    entry_time    = "2026-01-01T08:10:00Z"
    event_type    = "ENTRY"
} | ConvertTo-Json)
```

**Por que a lotação agora é 50%, e não 0%?** Porque o veículo A já ocupou a vaga 1 (passo 6.2) — `1 vaga ocupada / 2 vagas totais = 50%`. Como a faixa `<= 50%` corresponde a preço **normal** (multiplicador `1.00`), este segundo veículo entra sem desconto nem acréscimo.

### 6.4 Veículo B estaciona na vaga 2

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5089/webhook" -ContentType "application/json" -Body (@{
    license_plate = "XYZ9999"
    lat           = -23.561700
    lng           = -46.656000
    event_type    = "PARKED"
} | ConvertTo-Json)
```

Agora **as duas vagas da garagem estão ocupadas** (100% de lotação).

### 6.5 Veículo C tenta entrar — garagem cheia, bloqueado com 422

```powershell
try {
    Invoke-RestMethod -Method Post -Uri "http://localhost:5089/webhook" -ContentType "application/json" -Body (@{
        license_plate = "DEF5678"
        entry_time    = "2026-01-01T08:20:00Z"
        event_type    = "ENTRY"
    } | ConvertTo-Json)
} catch {
    $_.Exception.Response.StatusCode.value__
    $_.ErrorDetails.Message
}
```

**Resultado esperado:** `422` com um corpo `ProblemDetails` contendo `"Não é possível registrar entrada: garagem no limite de capacidade."` — a mensagem exata de `ParkingSessionErrors.GaragemCheia` (ver `docs/documentacao-tecnica-dominio.md`, seção 5.4). Isso confirma a RN-3/RN-8 do enunciado: **nenhuma entrada nova é aceita com 100% de lotação**.

> `Invoke-RestMethod` lança uma exceção para respostas HTTP de erro (4xx/5xx) — por isso o `try/catch` é necessário para inspecionar o status code e o corpo do erro, diferente das chamadas de sucesso acima.

### 6.6 Veículo A sai — cobrança com 30 min grátis + hora arredondada para cima

```powershell
$saida = Invoke-RestMethod -Method Post -Uri "http://localhost:5089/webhook" -ContentType "application/json" -Body (@{
    license_plate = "ABC1234"
    exit_time     = "2026-01-01T09:31:00Z"
    event_type    = "EXIT"
} | ConvertTo-Json)
```

**Confira a conta na mão, exatamente como o domínio calcula (`ParkingSession.CalcularValorCobrado`):**

| Passo | Valor |
|-------|-------|
| Entrada | `08:00:00` |
| Saída | `09:31:00` |
| Minutos decorridos | `91` |
| Minutos grátis | `30` |
| Minutos cobráveis | `91 − 30 = 61` |
| Horas cobradas (arredondado **para cima**) | `ceil(61 / 60) = 2` |
| Tarifa base do setor `A` | `10,00` |
| Multiplicador **travado na entrada** (lotação era 0% quando A entrou) | `0.90` |
| **Valor cobrado** | `2 × 10,00 × 0.90 = 18,00` |

**Ponto de atenção didático:** repare que o multiplicador usado é `0.90` (o de quando o veículo A **entrou**, às 08:00, com a garagem vazia) — **não** o multiplicador de agora, que seria `1.00` (garagem ainda em 50% de ocupação, já que o veículo B continua estacionado). Se você refizer essa conta usando `1.00` por engano, o resultado bateria errado (`20,00`) — essa é exatamente a "premissa da lotação travada na entrada" documentada no `PLAN.md` e no domínio.

### 6.7 Consultar a receita do setor A no dia

```powershell
$data = "2026-01-01"
Invoke-RestMethod -Method Get -Uri "http://localhost:5089/revenue?sector=A&date=$data"
```

**Resposta esperada:**

```json
{
  "amount": 18.00,
  "currency": "BRL",
  "timestamp": "..."
}
```

**Por que `18.00`, e não `18.00 + algo do veículo B`?** Porque `GetRevenueAsync` só soma sessões com `Status = Finalizado` (ver `docs/documentacao-tecnica-infrastructure.md`, seção 6.3) — o veículo B ainda está estacionado, não gerou cobrança. Se você quiser conferir isso na prática, repita o passo 6.6 para o veículo `XYZ9999` (com um `exit_time` de sua escolha) e rode a consulta de novo — o valor deve aumentar de acordo com o tempo que você calculou na mão.

---

## 7. Testando pelo Swagger, em vez de `Invoke-RestMethod`

Se preferir a interface visual (`https://localhost:xxxx/swagger`):

1. Expanda `POST /webhook`, clique em **Try it out**.
2. Cole um dos corpos JSON usados na seção 6 (ex: o do passo 6.1) na caixa de request body.
3. Clique **Execute** e confira o `Code` da resposta (`200`, `404` ou `422`) e o corpo, se houver.
4. Para `GET /revenue`, preencha os campos `sector` e `date` nos parâmetros de query e clique **Execute**.

O Swagger é especialmente útil para os cenários de erro da seção 8 — você vê o `ProblemDetails` completo formatado, sem precisar de `try/catch`.

---

## 8. Cenários de erro para testar deliberadamente

| Cenário | Como provocar | Resultado esperado |
|---------|----------------|----------------------|
| `event_type` desconhecido | `POST /webhook` com `"event_type": "FOO"` | `400 Bad Request` — `"O evento 'FOO' não é suportado..."` |
| `PARKED` sem `ENTRY` prévio | `POST /webhook` `PARKED` para uma placa nunca usada | `404 Not Found` — `"Nenhuma sessão ativa encontrada para esta placa."` |
| `PARKED` com coordenadas que não existem | `lat`/`lng` que não batem com nenhuma vaga sincronizada | `404 Not Found` — `"Nenhuma vaga corresponde às coordenadas informadas."` |
| `EXIT` duplicado | Repetir o passo 6.6 para o mesmo veículo `ABC1234` | `404 Not Found` — a sessão já foi finalizada, então deixa de ser considerada "ativa" (`GetActiveByLicensePlateAsync` ignora sessões `Finalizado`) |
| `EXIT` sem ter passado por `PARKED` | `ENTRY` de uma placa nova, seguido direto de `EXIT` (sem `PARKED` no meio) | `404 Not Found` — a sessão ainda não tem `SectorCode` (só é gravado no `PARKED`), então a busca pelo setor falha antes mesmo de chegar à regra de domínio |
| `GET /revenue` para setor inexistente | `?sector=Z&date=2026-01-01` | `404 Not Found` — `"Setor não encontrado."` |
| Placa vazia | `POST /webhook` `ENTRY` com `"license_plate": ""` | `400 Bad Request` — falha de validação do FluentValidation |

---

## 9. Inspecionar o banco diretamente

Para conferir o estado exato das tabelas (útil para depurar se um teste manual não bateu com o esperado):

```powershell
sqlcmd -S localhost,1433 -U sa -P "Your_password123" -C -d ParkingManagement `
  -Q "SELECT LicensePlate, Status, SectorCode, AmountCharged FROM parking.ParkingSessions"

sqlcmd -S localhost,1433 -U sa -P "Your_password123" -C -d ParkingManagement `
  -Q "SELECT ExternalId, SectorCode, Status FROM parking.Spots"
```

---

## 10. Solução de problemas comuns

| Sintoma | Causa provável | Como resolver |
|---------|------------------|-----------------|
| API encerra sozinha logo após `dotnet run`, log `"Falha ao sincronizar a configuração da garagem..."` | O stub do passo 4 não estava rodando (ou caiu) quando a API subiu | Suba o stub **antes** da API; se a API já subiu, pare (`Ctrl+C`) e rode `dotnet run` de novo |
| `Login failed for user 'sa'` ao rodar migrations | SQL Server ainda não terminou de inicializar | Aguarde a linha `SQL Server is now ready for client connections` no `docker logs -f parking-sqlserver` |
| `Invalid object name 'parking.Sectors'` | Migrations não foram aplicadas | Rode o passo 3 novamente |
| Todo `POST /webhook` retorna `422 "garagem no limite de capacidade"`, mesmo logo após subir | Sinal do bug de persistência já documentado e corrigido (`IUnitOfWork` ausente) — se reaparecer, é uma regressão | Confirme que o handler correspondente chama `unitOfWork.SaveChangesAsync(...)` (ver `docs/documentacao-tecnica-application.md`, seção 4.3) |
| `POST /webhook` (`ENTRY`) retorna `422 "garagem no limite de capacidade"` logo na primeira chamada, usando a Opção B | Você esqueceu de chamar `POST /garage/seed` antes do primeiro evento — a garagem está vazia (`0` de capacidade total, que o handler interpreta como cheia) | Rode o `POST /garage/seed` da seção 4 (Opção B) antes de qualquer evento de webhook |
| Quer recomeçar do zero | Estado do banco "sujo" de testes anteriores | `docker rm -f parking-sqlserver` e repita a partir do passo 2 |

---

## 11. Encerrando o ambiente

```powershell
# Terminal da API: Ctrl+C
# Terminal do stub: Ctrl+C
docker rm -f parking-sqlserver
```

---

## 12. E os testes automatizados?

Este guia cobre testes **manuais**, para você enxergar o sistema se comportando de verdade e conferir os números na mão. Para a suíte automatizada (Domain, Application e Integration Tests, incluindo o mesmo fluxo completo validado aqui via `WebApplicationFactory` + SQL Server real em container), veja `docs/documentacao-tecnica-testes.md` e rode:

```powershell
dotnet test
```
