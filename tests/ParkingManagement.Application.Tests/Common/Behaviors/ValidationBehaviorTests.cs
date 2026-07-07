using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using NSubstitute;
using ParkingManagement.Application.Common.Behaviors;

namespace ParkingManagement.Application.Tests.Common.Behaviors;

public class ValidationBehaviorTests
{
    public sealed record TestRequest(string Value);

    [Fact]
    public async Task Handle_DeveChamarNext_QuandoNaoHaValidatorsRegistrados()
    {
        // Arrange
        var behavior = new ValidationBehavior<TestRequest, string>([]);
        var request = new TestRequest("qualquer");

        // Act
        var result = await behavior.Handle(request, () => Task.FromResult("ok"), CancellationToken.None);

        // Assert
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_DeveChamarNext_QuandoValidacaoPassa()
    {
        // Arrange
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.Validate(Arg.Any<ValidationContext<TestRequest>>()).Returns(new ValidationResult());

        var behavior = new ValidationBehavior<TestRequest, string>([validator]);
        var request = new TestRequest("qualquer");

        // Act
        var result = await behavior.Handle(request, () => Task.FromResult("ok"), CancellationToken.None);

        // Assert
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_DeveLancarValidationException_QuandoValidacaoFalha()
    {
        // Arrange
        var failure = new ValidationFailure("Value", "Campo obrigatório.");
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.Validate(Arg.Any<ValidationContext<TestRequest>>()).Returns(new ValidationResult([failure]));

        var behavior = new ValidationBehavior<TestRequest, string>([validator]);
        var request = new TestRequest("");

        // Act
        Func<Task> act = () => behavior.Handle(request, () => Task.FromResult("ok"), CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.ErrorMessage == "Campo obrigatório.");
    }

    [Fact]
    public async Task Handle_NaoDeveChamarNext_QuandoValidacaoFalha()
    {
        // Arrange
        var failure = new ValidationFailure("Value", "Campo obrigatório.");
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.Validate(Arg.Any<ValidationContext<TestRequest>>()).Returns(new ValidationResult([failure]));

        var nextCalled = false;
        RequestHandlerDelegate<string> next = () =>
        {
            nextCalled = true;
            return Task.FromResult("ok");
        };

        var behavior = new ValidationBehavior<TestRequest, string>([validator]);
        var request = new TestRequest("");

        // Act
        Func<Task> act = () => behavior.Handle(request, next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DeveConsiderarFalhasDeTodosOsValidators()
    {
        // Arrange
        var falhaA = new ValidationFailure("Value", "Erro do validator A.");
        var falhaB = new ValidationFailure("Value", "Erro do validator B.");

        var validatorA = Substitute.For<IValidator<TestRequest>>();
        validatorA.Validate(Arg.Any<ValidationContext<TestRequest>>()).Returns(new ValidationResult([falhaA]));

        var validatorB = Substitute.For<IValidator<TestRequest>>();
        validatorB.Validate(Arg.Any<ValidationContext<TestRequest>>()).Returns(new ValidationResult([falhaB]));

        var behavior = new ValidationBehavior<TestRequest, string>([validatorA, validatorB]);
        var request = new TestRequest("");

        // Act
        Func<Task> act = () => behavior.Handle(request, () => Task.FromResult("ok"), CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCount(2);
    }
}
