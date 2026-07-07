using FluentAssertions;
using ParkingManagement.Application.Common.Errors;
using ParkingManagement.Application.Common.Integrations;
using ParkingManagement.Application.Features.Garage.Commands.SeedGarage;

namespace ParkingManagement.Application.Tests.Features.Garage;

public class SeedGarageValidatorTests
{
    private readonly SeedGarageValidator _validator = new();

    [Fact]
    public async Task Validar_DevePassar_QuandoDadosValidos()
    {
        // Arrange
        var request = new SeedGarageRequest(
            Garage: [new GarageSectorDto("A", 10m, 100)],
            Spots: [new GarageSpotDto(1, "A", -23.561684, -46.655981)]);

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validar_DeveFalhar_QuandoGarageNulo()
    {
        // Arrange
        var request = new SeedGarageRequest(Garage: null!, Spots: []);

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == ApplicationErrorMessages.Garage.ListaSetoresObrigatoria);
    }

    [Fact]
    public async Task Validar_DeveFalhar_QuandoSpotsNulo()
    {
        // Arrange
        var request = new SeedGarageRequest(Garage: [], Spots: null!);

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == ApplicationErrorMessages.Garage.ListaVagasObrigatoria);
    }
}
