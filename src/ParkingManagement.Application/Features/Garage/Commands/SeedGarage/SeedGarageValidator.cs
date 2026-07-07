using FluentValidation;
using ParkingManagement.Application.Common.Errors;

namespace ParkingManagement.Application.Features.Garage.Commands.SeedGarage;

public sealed class SeedGarageValidator : AbstractValidator<SeedGarageRequest>
{
    public SeedGarageValidator()
    {
        RuleFor(x => x.Garage)
            .NotNull().WithMessage(ApplicationErrorMessages.Garage.ListaSetoresObrigatoria);

        RuleFor(x => x.Spots)
            .NotNull().WithMessage(ApplicationErrorMessages.Garage.ListaVagasObrigatoria);
    }
}
