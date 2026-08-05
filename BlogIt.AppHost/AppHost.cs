var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter("blogit-sql-password", secret: true);

// Azure SQL Server (local dev SQL container)
var sql = builder.AddSqlServer("blogit-sql")
    .WithPassword(sqlPassword)
    .WithLifetime(ContainerLifetime.Persistent);
var db = sql.AddDatabase("BlogItDb");

// Azure Blob Storage (uses Azurite emulator in dev)
var storage = builder.AddAzureStorage("blogit-storage")
    .RunAsEmulator();

var blobs = storage.AddBlobs("BlogItStorage");

// Web app (serves public site, admin WASM, and API)
var web = builder.AddProject<Projects.BlogIt_Web>("blogit-web")
    .WithReference(db)
    .WithReference(blobs)
    .WithExternalHttpEndpoints();

builder.Build().Run();
