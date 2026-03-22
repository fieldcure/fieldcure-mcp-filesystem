using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FieldCure.Mcp.Filesystem.Tests;

[TestClass]
public class McpIntegrationTests
{
    private McpClient _client = null!;
    private string _testDir = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"mcp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = ["run", "--no-build", "--project",
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                    "src", "FieldCure.Mcp.Filesystem")),
                "--", _testDir],
            Name = "fieldcure-mcp-filesystem",
        });

        _client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ClientInfo = new() { Name = "TestClient", Version = "1.0" },
        });
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _client.DisposeAsync();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [TestMethod]
    public async Task ListTools_ReturnsAll12Tools()
    {
        var tools = await _client.ListToolsAsync();
        Assert.AreEqual(12, tools.Count);
    }

    [TestMethod]
    public async Task WriteAndReadFile_RoundTrip()
    {
        var filePath = Path.Combine(_testDir, "hello.txt");

        await _client.CallToolAsync("write_file", new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["content"] = "Hello, MCP!",
        });

        var result = await _client.CallToolAsync("read_file", new Dictionary<string, object?>
        {
            ["path"] = filePath,
        });

        var text = GetText(result);
        StringAssert.Contains(text, "Hello, MCP!");
    }

    [TestMethod]
    public async Task ReadFile_OutsideSandbox_Fails()
    {
        var result = await _client.CallToolAsync("read_file", new Dictionary<string, object?>
        {
            ["path"] = @"C:\Windows\System32\config\SAM",
        });

        Assert.IsTrue(result.IsError);
    }

    [TestMethod]
    public async Task ListDirectory_ReturnsEntries()
    {
        File.WriteAllText(Path.Combine(_testDir, "a.txt"), "aaa");
        Directory.CreateDirectory(Path.Combine(_testDir, "subdir"));

        var result = await _client.CallToolAsync("list_directory", new Dictionary<string, object?>
        {
            ["path"] = _testDir,
        });

        var text = GetText(result);
        StringAssert.Contains(text, "[DIR]");
        StringAssert.Contains(text, "[FILE]");
    }

    [TestMethod]
    public async Task ModifyFile_ReplacesText()
    {
        var filePath = Path.Combine(_testDir, "modify.txt");
        File.WriteAllText(filePath, "foo bar foo");

        await _client.CallToolAsync("modify_file", new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["find"] = "foo",
            ["replace"] = "baz",
        });

        var content = File.ReadAllText(filePath);
        Assert.AreEqual("baz bar baz", content);
    }

    [TestMethod]
    public async Task SearchFiles_FindsByPattern()
    {
        File.WriteAllText(Path.Combine(_testDir, "test.cs"), "code");
        File.WriteAllText(Path.Combine(_testDir, "test.txt"), "text");

        var result = await _client.CallToolAsync("search_files", new Dictionary<string, object?>
        {
            ["path"] = _testDir,
            ["pattern"] = "*.cs",
        });

        var text = GetText(result);
        StringAssert.Contains(text, "test.cs");
        Assert.IsFalse(text.Contains("test.txt"));
    }

    private static string GetText(CallToolResult result)
    {
        return string.Join("\n", result.Content
            .OfType<TextContentBlock>()
            .Select(t => t.Text ?? ""));
    }
}
