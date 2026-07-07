using FluentValidation;
using ParkingManagement.Application.Common.Errors;

namespace ParkingManagement.Application.Features.Parking.Commands.RegisterExit;

public sealed class RegisterExitValidator : AbstractValidator<RegisterExitRequest>
{
    public RegisterExitValidator()
    {
        RuleFor(x => x.LicensePlate)
            .NotEmpty().WithMessage(ApplicationErrorMessages.Parking.LicensePlateObrigatoria);

        RuleFor(x => x.ExitTime)
            .NotEqual(default(DateTime)).WithMessage(ApplicationErrorMessages.Parking.ExitTimeObrigatorio);
    }
}
