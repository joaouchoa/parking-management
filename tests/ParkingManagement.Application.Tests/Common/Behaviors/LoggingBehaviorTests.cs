using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ParkingManagement.Application.Common.Behaviors;

namespace ParkingManagement.Application.Tests.Common.Behaviors;

public class LoggingBehaviorTests
{
    public sealed record TestRequest(string Value);

    private readonly ILogger<LoggingBehavior<TestRequest, string>> _logger =
        Substitute.For<ILogger<LoggingBehavior<TestRequest, string>>>();

    [Fact]
    public async Task Handle_DeveRetornarRespostaDoNext()
    {
        // Arrange
        var behavior = new LoggingBehavior<TestRequest, string>(_logger);
        var request = new TestRequest("qualquer");

        // Act
        var result = await behavior.Handle(request, () => Task.FromResult("ok"), CancellationToken.None);

        // Assert
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_DevePropagarExcecao_QuandoNextLanca()
    {
        // Arrange
        var behavior = new LoggingBehavior<TestRequest, string>(_logger);
        var request = new TestRequest("qualquer");

        // Act
        Func<Task> act = () => behavior.Handle(
            request,
            () => throw new InvalidOperationException("falhou"),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("falhou");
    }

    [Fact]
    public async Task Handle_DeveChamarNextExatamenteUmaVez()
    {
        // Arrange
        var chamadas = 0;
        var behavior = new LoggingBehavior<TestRequest, string>(_logger);
        var request = new TestRequest("qualquer");

        // Act
        await behavior.Handle(
            request,
            () =>
            {
                chamadas++;
                return Task.FromResult("ok");
            },
            CancellationToken.None);

        // Assert
        chamadas.Should().Be(1);
    }
}
