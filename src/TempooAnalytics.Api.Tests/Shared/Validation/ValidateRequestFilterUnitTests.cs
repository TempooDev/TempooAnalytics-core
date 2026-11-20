using System.Threading.Tasks;
using AwesomeAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Validators;
using TempooAnalytics.Api.Shared.Validation;
using Xunit;

namespace TempooAnalytics.Api.Tests.Shared.Validation;

public class ValidateRequestFilterUnitTests
{
    private sealed class TestEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        private readonly object?[] _args;

        public TestEndpointFilterInvocationContext(object?[] args) : base()
        {
            _args = args;
        }

        public override object?[] Arguments => _args;

        public override T GetArgument<T>(int index)
        {
            return (T)_args[index]!;
        }

        public override HttpContext HttpContext => new DefaultHttpContext();
    }

    [Fact]
    public async Task InvokeAsync_NoArgumentOfType_CallsNext()
    {
        var validator = new DaysQueryDtoValidator();
        var filter = new ValidateRequestFilter<DaysQueryDto>(validator);

        var ctx = new TestEndpointFilterInvocationContext(new object?[] { "not-the-dto" });

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return new ValueTask<object?>("next-called");
        };

        var result = await filter.InvokeAsync(ctx, next);

        nextCalled.Should().BeTrue();
        result.Should().Be("next-called");
    }

    [Fact]
    public async Task InvokeAsync_InvalidModel_ReturnsValidationProblemAndDoesNotCallNext()
    {
        var validator = new DaysQueryDtoValidator();
        var filter = new ValidateRequestFilter<DaysQueryDto>(validator);

        var dto = new DaysQueryDto { Days = -1 }; // Invalid: days must be positive
        var ctx = new TestEndpointFilterInvocationContext(new object?[] { dto });

        EndpointFilterDelegate next = _ => throw new System.Exception("next should not be called");

        var result = await filter.InvokeAsync(ctx, next);

        // Should return an IResult (ValidationProblem) and not throw
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IResult>();
    }

    [Fact]
    public async Task InvokeAsync_ValidModel_CallsNextAndForwardsResult()
    {
        var validator = new DaysQueryDtoValidator();
        var filter = new ValidateRequestFilter<DaysQueryDto>(validator);

        var dto = new DaysQueryDto { Days = 7 }; // Valid
        var ctx = new TestEndpointFilterInvocationContext(new object?[] { dto });

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return new ValueTask<object?>(42);
        };

        var result = await filter.InvokeAsync(ctx, next);

        nextCalled.Should().BeTrue();
        result.Should().Be(42);
    }
}
