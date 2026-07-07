using FluentAssertions;
using ParkingManagement.Application.Common.Errors;
using ParkingManagement.Application.Features.Parking.Commands.RegisterParked;

namespace ParkingManagement.Application.Tests.Features.Parking;

public class RegisterParkedValidatorTests
{
    private readonly RegisterParkedValidator _validator = new();

    [Fact]
    public async Task Validar_DevePassar_QuandoDadosValidos()
    {
        // Arrange
        var request = new RegisterParkedRequest("ZUL0001", -23.561684, -46.655981);

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validar_DeveFalhar_QuandoPlacaVazia(string licensePlate)
    {
        // Arrange
        var request = new RegisterParkedRequest(licensePlate, -23.561684, -46.655981);

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == ApplicationErrorMessages.Parking.LicensePlateObrigatoria);
    }
}
