using ParkingManagement.Application.Common.Integrations;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Garage.Repositories;

namespace ParkingManagement.Application.Features.Garage;

public static class GarageUpsert
{
    public static async Task<(int SectorsUpserted, int SpotsUpserted)> ApplyAsync(
        GarageConfigurationDto configuration,
        ISectorRepository sectorRepository,
        ISpotRepository spotRepository,
        CancellationToken cancellationToken)
    {
        var sectorsUpserted = 0;
        foreach (var sectorDto in configuration.Garage)
        {
            var existing = await sectorRepository.GetByCodeAsync(sectorDto.Sector, cancellationToken);
            if (existing is null)
            {
                var sector = Sector.Criar(sectorDto.Sector, sectorDto.BasePrice, sectorDto.MaxCapacity);
                await sectorRepository.AddAsync(sector, cancellationToken);
            }
            else
            {
                existing.AtualizarConfiguracao(sectorDto.BasePrice, sectorDto.MaxCapacity);
            }

            sectorsUpserted++;
        }

        var spotsUpserted = 0;
        foreach (var spotDto in configuration.Spots)
        {
            var existing = await spotRepository.GetByExternalIdAsync(spotDto.Id, cancellationToken);
            if (existing is null)
            {
                var coordinate = GeoCoordinate.Criar(spotDto.Lat, spotDto.Lng);
                var spot = Spot.Criar(spotDto.Id, spotDto.Sector, coordinate);
                await spotRepository.AddAsync(spot, cancellationToken);
            }

            spotsUpserted++;
        }

        return (sectorsUpserted, spotsUpserted);
    }
}
