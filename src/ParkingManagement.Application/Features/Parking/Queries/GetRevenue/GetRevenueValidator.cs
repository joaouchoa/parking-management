using FluentValidation;
using ParkingManagement.Application.Common.Errors;

namespace ParkingManagement.Application.Features.Parking.Queries.GetRevenue;

public sealed class GetRevenueValidator : AbstractValidator<GetRevenueRequest>
{
    public GetRevenueValidator()
    {
        RuleFor(x => x.Sector)
            .NotEmpty().WithMessage(ApplicationErrorMessages.Revenue.SetorObrigatorio);

        RuleFor(x => x.Date)
            .NotEqual(default(DateOnly)).WithMessage(ApplicationErrorMessages.Revenue.DataObrigatoria);
    }
}
