FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/ParkingManagement.Api/ParkingManagement.Api.csproj",                       "src/ParkingManagement.Api/"]
COPY ["src/ParkingManagement.Application/ParkingManagement.Application.csproj",       "src/ParkingManagement.Application/"]
COPY ["src/ParkingManagement.Domain/ParkingManagement.Domain.csproj",                 "src/ParkingManagement.Domain/"]
COPY ["src/ParkingManagement.Infrastructure/ParkingManagement.Infrastructure.csproj", "src/ParkingManagement.Infrastructure/"]
RUN dotnet restore "src/ParkingManagement.Api/ParkingManagement.Api.csproj"

COPY . .
RUN dotnet publish "src/ParkingManagement.Api/ParkingManagement.Api.csproj" \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ParkingManagement.Api.dll"]
