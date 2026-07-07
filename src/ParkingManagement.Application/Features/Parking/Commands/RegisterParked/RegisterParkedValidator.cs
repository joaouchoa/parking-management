using FluentValidation;
using ParkingManagement.Application.Common.Errors;

namespace ParkingManagement.Application.Features.Parking.Commands.RegisterParked;

public sealed class RegisterParkedValidator : AbstractValidator<RegisterParkedRequest>
{
    public RegisterParkedValidator()
    {
        RuleFor(x => x.LicensePlate)
            .NotEmpty().WithMessage(ApplicationErrorMessages.Parking.LicensePlateObrigatoria);
    }
}
