using FluentValidation;

namespace TempooAnalytics.Api.Shared.Validation;

public sealed class ValidateRequestFilter<T> : IEndpointFilter where T : class
{
    private readonly IValidator<T> _validator;

    public ValidateRequestFilter(IValidator<T> validator)
    {
        _validator = validator;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // find first argument of type T
        var arg = context.Arguments.FirstOrDefault(a => a is T) as T;
        if (arg is null)
        {
            // nothing to validate
            return await next(context);
        }

        var result = await _validator.ValidateAsync(arg as T);
        if (!result.IsValid)
        {
            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            return Results.ValidationProblem(errors);
        }

        return await next(context);
    }
}
