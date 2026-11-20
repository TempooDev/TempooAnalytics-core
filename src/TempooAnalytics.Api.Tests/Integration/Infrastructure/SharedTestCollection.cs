using Xunit;

[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<PostgresFixture> { }