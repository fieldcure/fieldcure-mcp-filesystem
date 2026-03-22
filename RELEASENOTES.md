# Release Notes

## v0.1.0 (2026-03-22)

Initial release.

### Features

- **12 MCP tools** for filesystem operations via stdio transport
  - **File operations**: `read_file`, `read_multiple_files`, `write_file`, `modify_file`, `copy_file`, `move_file`
  - **Directory operations**: `list_directory`, `create_directory`, `directory_tree`
  - **Search & info**: `search_files`, `search_within_files`, `get_file_info`
- **Path sandboxing** via allowed directories (CLI args)
  - Directory traversal prevention
  - Symlink resolution and escape detection
  - NTFS Alternate Data Stream blocking
  - Windows reserved name rejection
  - Directory-separator-aware prefix matching
- **Atomic writes** for `write_file` and `modify_file` (temp file + rename)
- **Binary file detection** with automatic base64 encoding
- **Regex support** in `modify_file` with 5-second timeout
- **dotnet tool** packaging for global installation via NuGet

### Tech Stack

- .NET 8.0
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) v1.1.0
- Microsoft.Extensions.Hosting
- MSTest (21 tests: 15 unit + 6 integration)
