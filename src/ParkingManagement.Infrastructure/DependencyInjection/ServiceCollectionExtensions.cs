using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkingManagement.Application.Common.Integrations;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Domain.Parking.Repositories;
using ParkingManagement.Infrastructure.ExternalServices;
using ParkingManagement.Infrastructure.Persistence;
using ParkingManagement.Infrastructure.Repositories;

namespace ParkingManagement.Infrastructure.DependencyInjection;

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
