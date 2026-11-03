using System;
using System.IO;
using System.Linq;
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    // Ephemeral per debug run: the container is deleted when the apphost stops, so
    // the dev database never carries state between runs (no volumes, no reuse).
    .WithLifetime(ContainerLifetime.Session)
    .AddDatabase("metaanime");

var api = builder.AddProject<Projects.MetaAnime_Presentation>("api")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_CONTENTROOT",
        Path.GetDirectoryName(new Projects.MetaAnime_Presentation().ProjectPath)
            ?? throw new InvalidOperationException("Could not determine the MetaAnime.Presentation project directory."));

// On Bazzite/Homebrew hosts, GUI-launched IDEs (Rider -> Aspire DCP) do not put
// Homebrew's bin directory on PATH, so "npm" cannot be resolved. Prepend the known
// install directories when they exist and resolve npm to an absolute path so the
// resource starts regardless of how DCP resolves bare command names.
var brewBin = "/home/linuxbrew/.linuxbrew/bin";
var uiPathEnv = string.Join(':', ((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        + ":" + string.Join(':', new[] { brewBin, "/usr/local/bin" }.Where(Directory.Exists)))
    .Split(':', StringSplitOptions.RemoveEmptyEntries)
    .Distinct());
var npmExe = uiPathEnv.Split(':')
    .Select(dir => Path.Combine(dir, "npm"))
    .FirstOrDefault(File.Exists)
    ?? "npm";

builder.AddExecutable("ui", npmExe, "../ui/meta-anime-ui", "run", "start")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 4200, isProxied: false)
    .WithEnvironment("PATH", uiPathEnv)
    .WithEnvironment("BROWSER", "none");

builder.Build().Run();
