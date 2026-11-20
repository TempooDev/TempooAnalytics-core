using FluentValidation;
using TempooAnalytics.Api.Features.Analytics.Dtos;

namespace TempooAnalytics.Api.Features.Analytics.Validators;

public sealed class DaysQueryDtoValidator : AbstractValidator<DaysQueryDto>
{
    public DaysQueryDtoValidator()
    {
        RuleFor(x => x.Days).GreaterThan(0).When(x => x.Days.HasValue).WithMessage("days must be positive");
    }
}
