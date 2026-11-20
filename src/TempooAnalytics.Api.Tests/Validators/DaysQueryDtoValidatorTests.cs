using FluentValidation.TestHelper;
using TempooAnalytics.Api.Features.Analytics.Validators;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using Xunit;

namespace TempooAnalytics.Api.Tests.Validators;

public class DaysQueryDtoValidatorTests
{
    private readonly DaysQueryDtoValidator _validator = new();

    [Fact]
    public void Should_not_have_error_when_days_null()
    {
        var dto = new DaysQueryDto { Days = null };

        var result = _validator.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.Days);
    }

    [Fact]
    public void Should_have_error_when_days_zero_or_negative()
    {
        var dto = new DaysQueryDto { Days = 0 };

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Days);
    }

    [Fact]
    public void Should_not_have_error_when_days_positive()
    {
        var dto = new DaysQueryDto { Days = 7 };

        var result = _validator.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.Days);
    }
}
