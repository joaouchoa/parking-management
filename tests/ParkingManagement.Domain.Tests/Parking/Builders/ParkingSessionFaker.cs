using Bogus;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Parking;
using ParkingManagement.Domain.Parking.ValueObjects;

namespace ParkingManagement.Domain.Tests.Parking.Builders;

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
