using FieldCure.Mcp.Filesystem.Security;
using FieldCure.Mcp.Filesystem.Tools;

namespace FieldCure.Mcp.Filesystem.Tests.Tools;

[TestClass]
public class FileOperationToolsTests
{
    private string _testRoot = null!;
    private PathValidator _validator = null!;

    [TestInitialize]
    public void Setup()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mcp-fs-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _validator = new PathValidator([_testRoot]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    // --- DeleteFile tests ---

    [TestMethod]
    public async Task DeleteFile_ExistingFile_DeletesAndReturnsMessage()
    {
        var filePath = Path.Combine(_testRoot, "delete-me.txt");
        File.WriteAllText(filePath, "goodbye");

        var result = await FileOperationTools.DeleteFile(_validator, filePath);

        Assert.IsFalse(File.Exists(filePath));
        StringAssert.Contains(result, "Deleted file:");
    }

    [TestMethod]
    public async Task DeleteFile_EmptyDirectory_DeletesWithoutRecursive()
    {
        var dirPath = Path.Combine(_testRoot, "empty-dir");
        Directory.CreateDirectory(dirPath);

        var result = await FileOperationTools.DeleteFile(_validator, dirPath);

        Assert.IsFalse(Directory.Exists(dirPath));
        StringAssert.Contains(result, "Deleted directory:");
    }

    [TestMethod]
    public async Task DeleteFile_NonEmptyDirectory_WithoutRecursive_Throws()
    {
        var dirPath = Path.Combine(_testRoot, "non-empty");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "child.txt"), "data");

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            FileOperationTools.DeleteFile(_validator, dirPath, recursive: false));

        Assert.IsTrue(Directory.Exists(dirPath));
    }

    [TestMethod]
    public async Task DeleteFile_NonEmptyDirectory_WithRecursive_Deletes()
    {
        var dirPath = Path.Combine(_testRoot, "non-empty");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "child.txt"), "data");

        var result = await FileOperationTools.DeleteFile(_validator, dirPath, recursive: true);

        Assert.IsFalse(Directory.Exists(dirPath));
        StringAssert.Contains(result, "recursive");
    }

    [TestMethod]
    public async Task DeleteFile_NotFound_Throws()
    {
        var filePath = Path.Combine(_testRoot, "ghost.txt");

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            FileOperationTools.DeleteFile(_validator, filePath));
    }

    [TestMethod]
    public async Task DeleteFile_OutsideSandbox_Throws()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "outside-delete.txt");

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            FileOperationTools.DeleteFile(_validator, outsidePath));
    }

    // --- AppendFile tests ---

    [TestMethod]
    public async Task AppendFile_NewFile_CreatesWithContent()
    {
        var filePath = Path.Combine(_testRoot, "new-append.txt");

        await FileOperationTools.AppendFile(_validator, filePath, "first line");

        Assert.AreEqual("first line", File.ReadAllText(filePath));
    }

    [TestMethod]
    public async Task AppendFile_ExistingFile_AppendsWithNewline()
    {
        var filePath = Path.Combine(_testRoot, "append.txt");
        File.WriteAllText(filePath, "existing");

        await FileOperationTools.AppendFile(_validator, filePath, "appended");

        var content = File.ReadAllText(filePath);
        Assert.AreEqual("existing\nappended", content);
    }

    [TestMethod]
    public async Task AppendFile_ExistingFileEndsWithNewline_NoExtraNewline()
    {
        var filePath = Path.Combine(_testRoot, "append-nl.txt");
        File.WriteAllText(filePath, "existing\n");

        await FileOperationTools.AppendFile(_validator, filePath, "appended");

        var content = File.ReadAllText(filePath);
        Assert.AreEqual("existing\nappended", content);
    }

    [TestMethod]
    public async Task AppendFile_NewlineFalse_NoNewlineInserted()
    {
        var filePath = Path.Combine(_testRoot, "append-no-nl.txt");
        File.WriteAllText(filePath, "existing");

        await FileOperationTools.AppendFile(_validator, filePath, "appended", newline: false);

        var content = File.ReadAllText(filePath);
        Assert.AreEqual("existingappended", content);
    }

    // --- ReadFileLines tests ---

    [TestMethod]
    public async Task ReadFileLines_SpecificRange_ReturnsLines()
    {
        var filePath = Path.Combine(_testRoot, "lines.txt");
        File.WriteAllLines(filePath, Enumerable.Range(1, 20).Select(i => $"Line {i}"));

        var result = await FileOperationTools.ReadFileLines(_validator, filePath, startLine: 5, endLine: 10);

        StringAssert.Contains(result, "Lines 5-10 of 20 total");
        StringAssert.Contains(result, "Line 5");
        StringAssert.Contains(result, "Line 10");
        Assert.IsFalse(result.Contains("Line 4"));
        Assert.IsFalse(result.Contains("Line 11"));
    }

    [TestMethod]
    public async Task ReadFileLines_ToEndOfFile_ReturnsRemainingLines()
    {
        var filePath = Path.Combine(_testRoot, "lines-eof.txt");
        File.WriteAllLines(filePath, ["A", "B", "C", "D", "E"]);

        var result = await FileOperationTools.ReadFileLines(_validator, filePath, startLine: 3);

        StringAssert.Contains(result, "Lines 3-5 of 5 total");
        StringAssert.Contains(result, "C");
        StringAssert.Contains(result, "E");
    }

    [TestMethod]
    public async Task ReadFileLines_StartLineLessThan1_Throws()
    {
        var filePath = Path.Combine(_testRoot, "lines.txt");
        File.WriteAllText(filePath, "content");

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            FileOperationTools.ReadFileLines(_validator, filePath, startLine: 0));
    }

    [TestMethod]
    public async Task ReadFileLines_StartLineExceedsTotal_Throws()
    {
        var filePath = Path.Combine(_testRoot, "short.txt");
        File.WriteAllLines(filePath, ["only one"]);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            FileOperationTools.ReadFileLines(_validator, filePath, startLine: 5));
    }

    [TestMethod]
    public async Task ReadFileLines_EndLineBeforeStartLine_Throws()
    {
        var filePath = Path.Combine(_testRoot, "range.txt");
        File.WriteAllLines(filePath, ["A", "B", "C"]);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            FileOperationTools.ReadFileLines(_validator, filePath, startLine: 3, endLine: 1));
    }

    [TestMethod]
    public async Task ReadFileLines_FileNotFound_Throws()
    {
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            FileOperationTools.ReadFileLines(_validator, Path.Combine(_testRoot, "nope.txt"), startLine: 1));
    }

    // --- ConvertToMarkdown tests ---

    [TestMethod]
    public async Task ConvertToMarkdown_TextFile_Unsupported_Throws()
    {
        var filePath = Path.Combine(_testRoot, "plain.txt");
        File.WriteAllText(filePath, "hello");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            FileOperationTools.ConvertToMarkdown(_validator, filePath));
    }

    [TestMethod]
    public async Task ConvertDirectoryToMarkdown_UnsupportedFiles_AreSkipped()
    {
        File.WriteAllText(Path.Combine(_testRoot, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_testRoot, "b.md"), "# b");

        var result = await FileOperationTools.ConvertDirectoryToMarkdown(_validator, _testRoot);

        StringAssert.Contains(result, "Converted 0 file(s)");
        StringAssert.Contains(result, "Skipped unsupported: 2");
    }

    [TestMethod]
    public async Task ConvertDirectoryToMarkdown_MissingDirectory_Throws()
    {
        var missing = Path.Combine(_testRoot, "missing");

        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() =>
            FileOperationTools.ConvertDirectoryToMarkdown(_validator, missing));
    }
}
