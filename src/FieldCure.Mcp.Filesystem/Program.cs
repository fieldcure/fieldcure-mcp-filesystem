using System.Reflection;
using FieldCure.Mcp.Filesystem.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
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
            Title = "FieldCure Filesystem",
            Description = "Sandboxed file/directory operations with built-in document parsing",
            Version = typeof(Program).Assembly
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0",
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

// Register roots/list_changed handler for runtime directory updates.
// When the client sends this notification, the server requests the new roots
// and replaces the allowed directories entirely (override, not merge).
var server = app.Services.GetRequiredService<ModelContextProtocol.Server.McpServer>();
var pathValidator = app.Services.GetRequiredService<IPathValidator>();

server.RegisterNotificationHandler(
    NotificationMethods.RootsListChangedNotification,
    async (notification, cancellationToken) =>
    {
        var rootsResult = await server.RequestRootsAsync(new(), cancellationToken);

        var folders = rootsResult.Roots
            .Select(r => new Uri(r.Uri).LocalPath)
            .ToList();

        pathValidator.UpdateDirectories(folders);
    });

await app.RunAsync();
return 0;
