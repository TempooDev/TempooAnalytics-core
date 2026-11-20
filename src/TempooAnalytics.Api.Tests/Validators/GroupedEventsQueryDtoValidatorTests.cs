using FluentValidation.TestHelper;
using TempooAnalytics.Api.Features.Analytics.Validators;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using Xunit;

namespace TempooAnalytics.Api.Tests.Validators;

public class GroupedEventsQueryDtoValidatorTests
{
    private readonly GroupedEventsQueryDtoValidator _validator = new();

    [Fact]
    public void Should_have_error_when_eventName_missing()
    {
        var dto = new GroupedEventsQueryDto { EventName = null, Days = 7 };

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.EventName);
    }

    [Fact]
    public void Should_have_error_when_days_non_positive()
    {
        var dto = new GroupedEventsQueryDto { EventName = "evt", Days = 0 };

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Days);
    }

    [Fact]
    public void Should_not_have_error_for_valid_input()
    {
        var dto = new GroupedEventsQueryDto { EventName = "evt", Days = 7, Keys = "k" };

        var result = _validator.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.EventName);
        result.ShouldNotHaveValidationErrorFor(x => x.Days);
    }
}
