using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// Resolve the server executable from the build output
var serverDll = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..",
    "src", "FieldCure.Mcp.Filesystem", "bin", "Debug", "net8.0",
    "fieldcure-mcp-filesystem.dll"));

if (!File.Exists(serverDll))
{
    Console.Error.WriteLine($"Server not found at: {serverDll}");
    Console.Error.WriteLine("Build the server first: dotnet build src/FieldCure.Mcp.Filesystem");
    return 1;
}

// Create two temp directories for the test
var initialDir = Path.Combine(Path.GetTempPath(), $"mcp-roots-initial-{Guid.NewGuid():N}");
var updatedDir = Path.Combine(Path.GetTempPath(), $"mcp-roots-updated-{Guid.NewGuid():N}");

Directory.CreateDirectory(initialDir);
Directory.CreateDirectory(updatedDir);

// Seed a file in each directory
File.WriteAllText(Path.Combine(initialDir, "hello.txt"), "initial content");
File.WriteAllText(Path.Combine(updatedDir, "world.txt"), "updated content");

try
{
    return await RunTestAsync(serverDll, initialDir, updatedDir);
}
finally
{
    Directory.Delete(initialDir, recursive: true);
    Directory.Delete(updatedDir, recursive: true);
}

static async Task<int> RunTestAsync(string serverDll, string initialDir, string updatedDir)
{
    var passed = 0;
    var failed = 0;

    // The roots the client will advertise to the server.
    // Initially null (server uses CLI args). After roots change, we swap to updatedDir.
    List<Root>? currentRoots = null;

    var client = await McpClient.CreateAsync(
        new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [serverDll, initialDir],
        }),
        new McpClientOptions
        {
            ClientInfo = new() { Name = "roots-test-client", Version = "1.0.0" },
            Capabilities = new ClientCapabilities
            {
                Roots = new() { ListChanged = true },
            },
            Handlers = new McpClientHandlers
            {
                RootsHandler = (request, ct) =>
                {
                    var roots = currentRoots ?? [new Root { Uri = new Uri(initialDir).AbsoluteUri, Name = "Initial" }];
                    return ValueTask.FromResult(new ListRootsResult { Roots = roots });
                },
            },
        });

    await using (client)
    {
        // --- Test 1: Initial directory is accessible ---
        Console.WriteLine("--- Test 1: Initial directory should be accessible ---");
        try
        {
            var result = await client.CallToolAsync("list_directory",
                new Dictionary<string, object?> { ["path"] = initialDir });

            if (result.IsError == true)
            {
                Console.WriteLine($"  FAIL: list_directory returned error: {GetText(result)}");
                failed++;
            }
            else
            {
                Console.WriteLine($"  PASS: list_directory succeeded");
                passed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: {ex.Message}");
            failed++;
        }

        // --- Test 2: Updated directory is NOT accessible yet ---
        Console.WriteLine("--- Test 2: Updated directory should NOT be accessible yet ---");
        try
        {
            var result = await client.CallToolAsync("list_directory",
                new Dictionary<string, object?> { ["path"] = updatedDir });

            if (result.IsError == true)
            {
                Console.WriteLine($"  PASS: Access correctly denied");
                passed++;
            }
            else
            {
                Console.WriteLine($"  FAIL: Should have been denied but succeeded");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: Unexpected exception: {ex.Message}");
            failed++;
        }

        // --- Trigger roots change ---
        Console.WriteLine("--- Sending roots/list_changed notification ---");
        currentRoots = [new Root { Uri = new Uri(updatedDir).AbsoluteUri, Name = "Updated" }];
        await client.SendNotificationAsync("notifications/roots/list_changed");
        await Task.Delay(500); // Allow server to process

        // --- Test 3: Initial directory should now be denied ---
        Console.WriteLine("--- Test 3: Initial directory should be DENIED after roots change ---");
        try
        {
            var result = await client.CallToolAsync("list_directory",
                new Dictionary<string, object?> { ["path"] = initialDir });

            if (result.IsError == true)
            {
                Console.WriteLine($"  PASS: Access correctly denied after roots change");
                passed++;
            }
            else
            {
                Console.WriteLine($"  FAIL: Should have been denied but succeeded");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: Unexpected exception: {ex.Message}");
            failed++;
        }

        // --- Test 4: Updated directory should now be accessible ---
        Console.WriteLine("--- Test 4: Updated directory should be accessible after roots change ---");
        try
        {
            var result = await client.CallToolAsync("list_directory",
                new Dictionary<string, object?> { ["path"] = updatedDir });

            if (result.IsError == true)
            {
                Console.WriteLine($"  FAIL: list_directory returned error: {GetText(result)}");
                failed++;
            }
            else
            {
                Console.WriteLine($"  PASS: list_directory succeeded on new root");
                passed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: {ex.Message}");
            failed++;
        }

        // --- Test 5: Read file in new root ---
        Console.WriteLine("--- Test 5: Read file in updated directory ---");
        try
        {
            var result = await client.CallToolAsync("read_file",
                new Dictionary<string, object?> { ["path"] = Path.Combine(updatedDir, "world.txt") });

            var text = GetText(result);
            if (result.IsError != true && text.Contains("updated content"))
            {
                Console.WriteLine($"  PASS: read_file returned correct content");
                passed++;
            }
            else
            {
                Console.WriteLine($"  FAIL: Unexpected result: {text}");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Results: {passed} passed, {failed} failed out of {passed + failed}");
    return failed == 0 ? 0 : 1;
}

static string GetText(CallToolResult result)
{
    return string.Join("\n", result.Content
        .OfType<TextContentBlock>()
        .Select(c => c.Text ?? ""));
}
