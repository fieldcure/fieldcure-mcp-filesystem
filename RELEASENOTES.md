# Release Notes

## v0.1.0 (2026-03-22)

Initial release.

### Features

- **12 MCP tools** for filesystem operations via stdio transport
  - **File operations**: `ReadFile`, `ReadMultipleFiles`, `WriteFile`, `ModifyFile`, `CopyFile`, `MoveFile`
  - **Directory operations**: `ListDirectory`, `CreateDirectory`, `DirectoryTree`
  - **Search & info**: `SearchFiles`, `SearchWithinFiles`, `GetFileInfo`
- **Path sandboxing** via allowed directories (CLI args)
  - Directory traversal prevention
  - Symlink resolution and escape detection
  - NTFS Alternate Data Stream blocking
  - Windows reserved name rejection
  - Directory-separator-aware prefix matching
- **Atomic writes** for `WriteFile` and `ModifyFile` (temp file + rename)
- **Binary file detection** with automatic base64 encoding
- **Regex support** in `ModifyFile` with 5-second timeout

### Tech Stack

- .NET 8.0
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) v1.1.0
- Microsoft.Extensions.Hosting
- MSTest (15 unit tests)
