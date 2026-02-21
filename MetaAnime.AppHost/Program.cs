var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("metaanime");

var api = builder.AddProject<Projects.MetaAnime_Presentation>("api")
    .WithReference(postgres)
    .WaitFor(postgres);

builder.AddExecutable("ui", "npm", "../ui/meta-anime-ui", "run", "start")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 4200, isProxied: false)
    .WithEnvironment("BROWSER", "none");

builder.Build().Run();
