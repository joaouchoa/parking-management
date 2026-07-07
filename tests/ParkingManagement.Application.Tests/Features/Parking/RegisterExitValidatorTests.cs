using FluentAssertions;
using ParkingManagement.Application.Common.Errors;
using ParkingManagement.Application.Features.Parking.Commands.RegisterExit;

namespace ParkingManagement.Application.Tests.Features.Parking;

public class RegisterExitValidatorTests
{
    private readonly RegisterExitValidator _validator = new();

    [Fact]
    public async Task Validar_DevePassar_QuandoDadosValidos()
    {
        // Arrange
        var request = new RegisterExitRequest("ZUL0001", DateTime.UtcNow);

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
        var request = new RegisterExitRequest(licensePlate, DateTime.UtcNow);

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == ApplicationErrorMessages.Parking.LicensePlateObrigatoria);
    }

    [Fact]
    public async Task Validar_DeveFalhar_QuandoExitTimeNaoInformado()
    {
        // Arrange
        var request = new RegisterExitRequest("ZUL0001", default);

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == ApplicationErrorMessages.Parking.ExitTimeObrigatorio);
    }
}
