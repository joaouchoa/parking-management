FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/ParkingManagement.Infrastructure.Migrations/ParkingManagement.Infrastructure.Migrations.csproj", "src/ParkingManagement.Infrastructure.Migrations/"]
RUN dotnet restore "src/ParkingManagement.Infrastructure.Migrations/ParkingManagement.Infrastructure.Migrations.csproj"

COPY . .
RUN dotnet publish "src/ParkingManagement.Infrastructure.Migrations/ParkingManagement.Infrastructure.Migrations.csproj" \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ParkingManagement.Infrastructure.Migrations.dll"]
