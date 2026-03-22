using FieldCure.Mcp.Filesystem.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: fieldcure-mcp-filesystem <allowed-directory> [additional-directories...]");
    Console.Error.WriteLine("At least one allowed directory must be specified.");
    return 1;
}

// Resolve and validate allowed directories
var allowedDirectories = new List<string>();

foreach (var arg in args)
{
    var dir = Path.GetFullPath(arg);

    if (!Directory.Exists(dir))
    {
        Console.Error.WriteLine($"Warning: directory does not exist, skipping: {arg}");
        continue;
    }

    allowedDirectories.Add(dir);
    Console.Error.WriteLine($"Allowed directory: {dir}");
}

if (allowedDirectories.Count == 0)
{
    Console.Error.WriteLine("Error: no valid directories specified. Exiting.");
    return 1;
}

var builder = Host.CreateApplicationBuilder(Array.Empty<string>());

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddSingleton<IPathValidator>(new PathValidator(allowedDirectories))
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "fieldcure-mcp-filesystem",
            Version = "0.1.0",
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;
