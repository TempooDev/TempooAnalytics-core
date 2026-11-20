using FluentValidation;
using TempooAnalytics.Api.Features.Analytics.Dtos;

namespace TempooAnalytics.Api.Features.Analytics.Validators;

public sealed class GroupedEventsQueryDtoValidator : AbstractValidator<GroupedEventsQueryDto>
{
    public GroupedEventsQueryDtoValidator()
    {
        RuleFor(x => x.EventName).NotEmpty().WithMessage("eventName is required");
        RuleFor(x => x.Days).GreaterThan(0).When(x => x.Days.HasValue).WithMessage("days must be positive");
        // keys may be empty (we allow returning empty result), so no rule for Keys
    }
}
