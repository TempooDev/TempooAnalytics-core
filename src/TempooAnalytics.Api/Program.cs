// The host itself is ApiHost.RunAsync; this file stays as the entry point of the API project (and the type
// WebApplicationFactory<Program> is built on).
await TempooAnalytics.Api.ApiHost.RunAsync(args);

public partial class Program { }
