using TempooAnalytics.ServiceDefaults;

namespace TempooAnalytics.Api.Infrastructure;

public static class BuilderExtensions
{
    public static TBuilder AddBuilderExtensions<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.AddServiceDefaults();
        
        var isTest = builder.Configuration["TEST_EXECUTION"] == "true" || 
                     Environment.GetEnvironmentVariable("TEST_EXECUTION") == "true" ||
                     AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName?.Contains("Microsoft.AspNetCore.Mvc.Testing") == true);

        builder.Services.AddServicesExtensions(builder.Configuration, isTest, builder.Environment.EnvironmentName);
        return builder;
    }
}
