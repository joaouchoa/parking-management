using FluentAssertions;
using ParkingManagement.Application.Common.Errors;
using ParkingManagement.Application.Features.Parking.Queries.GetRevenue;

namespace ParkingManagement.Application.Tests.Features.Parking;

public class GetRevenueValidatorTests
{
    private readonly GetRevenueValidator _validator = new();

    [Fact]
    public async Task Validar_DevePassar_QuandoDadosValidos()
    {
        // Arrange
        var request = new GetRevenueRequest("A", DateOnly.FromDateTime(DateTime.UtcNow));

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validar_DeveFalhar_QuandoSetorVazio(string sector)
    {
        // Arrange
        var request = new GetRevenueRequest(sector, DateOnly.FromDateTime(DateTime.UtcNow));

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == ApplicationErrorMessages.Revenue.SetorObrigatorio);
    }

    [Fact]
    public async Task Validar_DeveFalhar_QuandoDataNaoInformada()
    {
        // Arrange
        var request = new GetRevenueRequest("A", default);

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == ApplicationErrorMessages.Revenue.DataObrigatoria);
    }
}
