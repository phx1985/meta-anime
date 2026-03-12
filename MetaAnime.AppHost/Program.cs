var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("metaanime");

// Ollama: local AI for Bot-san.  The container auto-downloads the model on first run.
// Data is persisted in a named volume so the model survives container restarts.
var ollama = builder.AddOllama("ollama").WithDataVolume();
ollama.AddModel("qwen2.5:1.5b");

var api = builder.AddProject<Projects.MetaAnime_Presentation>("api")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("Ollama__BaseUrl", ollama.GetEndpoint("http"))
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_CONTENTROOT",
        Path.GetDirectoryName(new Projects.MetaAnime_Presentation().ProjectPath)
            ?? throw new InvalidOperationException("Could not determine the MetaAnime.Presentation project directory."));

builder.AddExecutable("ui", "npm", "../ui/meta-anime-ui", "run", "start")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 4200, isProxied: false)
    .WithEnvironment("BROWSER", "none");

builder.Build().Run();
