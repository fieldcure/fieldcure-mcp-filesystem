# Contributing to FieldCure.Mcp.Filesystem

Thank you for your interest in contributing!

## Adding a New Tool

1. Add tool method with `[McpTool]` attribute to the appropriate file in `Tools/`
   - File operations → `FileOperationTools.cs`
   - Directory operations → `DirectoryTools.cs`
   - Search/info → `SearchTools.cs`
2. All paths must go through `IPathValidator.ValidatePath()` before any I/O
3. Add unit tests in `tests/`
4. Update README — add tool to the tools table

## Security Guidelines

- **Never bypass `IPathValidator`** — all file paths must be validated
- Use `_validator.ValidatePath(path)` at the start of every tool method
- Atomic writes (temp file + rename) for any write operation
- Binary detection before returning file contents

## Bug Fixes

- Include test cases that reproduce the issue
- Ensure all existing tests pass (`dotnet test`)

## Code Style

- Follow existing patterns (nullable enabled, file-scoped namespaces)
- XML documentation on public APIs
- Comments in English
- MCP tool annotations: `ReadOnly`, `Destructive`, `Idempotent`

## Building

```bash
dotnet build
dotnet test
```

## License

By contributing, you agree that your contributions will be licensed under MIT.
