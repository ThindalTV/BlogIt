var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter(
    "blogit-sample-sql-password",
    new GenerateParameterDefault { MinLength = 24 },
    secret: true,
    persist: true);

var sql = builder.AddSqlServer("blogit-sample-sql")
    .WithPassword(sqlPassword)
    .WithLifetime(ContainerLifetime.Persistent);
var db = sql.AddDatabase("BlogItDb");

// The host process keeps media in BlogIt.Sample/App_Data/blogit-media across AppHost restarts.
builder.AddProject<Projects.BlogIt_Sample>("blogit-sample")
    .WithReference(db)
    .WaitFor(db)
    .WithExternalHttpEndpoints();

builder.Build().Run();
