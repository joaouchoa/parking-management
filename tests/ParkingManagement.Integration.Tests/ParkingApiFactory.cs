using System.Reflection;
using DbUp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParkingManagement.Application.Common.Integrations;
using ParkingManagement.Integration.Tests.Fakes;
using Testcontainers.MsSql;

namespace ParkingManagement.Integration.Tests;

public sealed class ParkingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder().Build();

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        RunMigrations(_sqlContainer.GetConnectionString());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sqlContainer.DisposeAsync();
    }

    private static void RunMigrations(string connectionString)
    {
        EnsureDatabase.For.SqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException("Falha ao aplicar migrations no container de teste.", result.Error);
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
