using FieldCure.Mcp.Filesystem.Security;

namespace FieldCure.Mcp.Filesystem.Tests.Security;

[TestClass]
public class PathValidatorTests
{
    private string _testRoot = null!;
    private PathValidator _validator = null!;

    [TestInitialize]
    public void Setup()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mcp-fs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _validator = new PathValidator([_testRoot]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [TestMethod]
    public void ValidateAndResolve_ExistingFileInsideAllowed_ReturnsResolvedPath()
    {
        var filePath = Path.Combine(_testRoot, "test.txt");
        File.WriteAllText(filePath, "hello");

        var result = _validator.ValidateAndResolve(filePath);

        Assert.AreEqual(filePath, result);
    }

    [TestMethod]
    public void ValidateAndResolve_AllowedDirectoryItself_ReturnsPath()
    {
        var result = _validator.ValidateAndResolve(_testRoot);

        Assert.AreEqual(_testRoot, result);
    }

    [TestMethod]
    public void ValidateAndResolve_NonExistentFileInsideAllowed_ReturnsPath()
    {
        var filePath = Path.Combine(_testRoot, "new-file.txt");

        var result = _validator.ValidateAndResolve(filePath);

        Assert.AreEqual(filePath, result);
    }

    [TestMethod]
    public void ValidateAndResolve_SubdirectoryPath_ReturnsPath()
    {
        var subDir = Path.Combine(_testRoot, "sub");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, "deep.txt");
        File.WriteAllText(filePath, "content");

        var result = _validator.ValidateAndResolve(filePath);

        Assert.AreEqual(filePath, result);
    }

    [TestMethod]
    public void ValidateAndResolve_PathOutsideAllowed_ThrowsUnauthorized()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "outside-test.txt");

        Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            _validator.ValidateAndResolve(outsidePath));
    }

    [TestMethod]
    public void ValidateAndResolve_TraversalAttack_ThrowsUnauthorized()
    {
        var traversalPath = Path.Combine(_testRoot, "..", "..", "etc", "passwd");

        Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            _validator.ValidateAndResolve(traversalPath));
    }

    [TestMethod]
    public void ValidateAndResolve_SimilarPrefixDirectory_ThrowsUnauthorized()
    {
        var secretDir = _testRoot + "-secret";
        Directory.CreateDirectory(secretDir);
        var secretFile = Path.Combine(secretDir, "secret.txt");
        File.WriteAllText(secretFile, "secret");

        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                _validator.ValidateAndResolve(secretFile));
        }
        finally
        {
            Directory.Delete(secretDir, recursive: true);
        }
    }

    [TestMethod]
    public void ValidateAndResolve_NullPath_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _validator.ValidateAndResolve(null!));
    }

    [TestMethod]
    public void ValidateAndResolve_EmptyPath_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            _validator.ValidateAndResolve(""));
    }

    [TestMethod]
    public void ValidateAndResolve_WhitespacePath_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            _validator.ValidateAndResolve("   "));
    }

    [TestMethod]
    public void ValidateAndResolve_MultipleAllowedDirectories_AcceptsBoth()
    {
        var secondRoot = Path.Combine(Path.GetTempPath(), $"mcp-fs-test2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(secondRoot);

        try
        {
            var multiValidator = new PathValidator([_testRoot, secondRoot]);

            var file1 = Path.Combine(_testRoot, "a.txt");
            var file2 = Path.Combine(secondRoot, "b.txt");

            multiValidator.ValidateAndResolve(file1);
            multiValidator.ValidateAndResolve(file2);
        }
        finally
        {
            Directory.Delete(secondRoot, recursive: true);
        }
    }

    [TestMethod]
    public void AllowedDirectories_ReturnsConfiguredDirectories()
    {
        Assert.AreEqual(1, _validator.AllowedDirectories.Count);
        StringAssert.Contains(_validator.AllowedDirectories[0], "mcp-fs-test-");
    }

    // Windows-specific tests
    [TestMethod]
    public void ValidateAndResolve_NtfsAds_ThrowsArgumentException()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var adsPath = Path.Combine(_testRoot, "file.txt:hidden");

        Assert.ThrowsExactly<ArgumentException>(() =>
            _validator.ValidateAndResolve(adsPath));
    }

    [TestMethod]
    public void ValidateAndResolve_ReservedName_ThrowsArgumentException()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var reservedPath = Path.Combine(_testRoot, "CON");

        Assert.ThrowsExactly<ArgumentException>(() =>
            _validator.ValidateAndResolve(reservedPath));
    }

    [TestMethod]
    public void ValidateAndResolve_ReservedNameWithExtension_ThrowsArgumentException()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var reservedPath = Path.Combine(_testRoot, "NUL.txt");

        Assert.ThrowsExactly<ArgumentException>(() =>
            _validator.ValidateAndResolve(reservedPath));
    }
}
