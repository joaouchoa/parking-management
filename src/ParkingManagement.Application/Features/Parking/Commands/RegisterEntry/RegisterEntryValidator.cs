using FluentValidation;
using ParkingManagement.Application.Common.Errors;

namespace ParkingManagement.Application.Features.Parking.Commands.RegisterEntry;

public sealed class RegisterEntryValidator : AbstractValidator<RegisterEntryRequest>
{
    public RegisterEntryValidator()
    {
        RuleFor(x => x.LicensePlate)
            .NotEmpty().WithMessage(ApplicationErrorMessages.Parking.LicensePlateObrigatoria);

        RuleFor(x => x.EntryTime)
            .NotEqual(default(DateTime)).WithMessage(ApplicationErrorMessages.Parking.EntryTimeObrigatorio);
    }
}
