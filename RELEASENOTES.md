# Release Notes

## v0.4.0 (2026-03-24)

### New Feature

- **DocumentParsers integration** — `read_file` now extracts text from document formats (hwpx, docx) via `FieldCure.DocumentParsers`, checked before binary detection

### Changes

- Added `FieldCure.DocumentParsers` 0.1.x package dependency

---

## v0.3.0 (2026-03-23)

### New Feature

- **MCP roots protocol support** — clients can change allowed directories at runtime via `notifications/roots/list_changed`, without restarting the server process
  - `IPathValidator.UpdateDirectories()` for thread-safe directory replacement
  - `ReaderWriterLockSlim` protects concurrent read/write access
  - CLI args serve as initial value; roots override them entirely
  - Empty roots list denies all file access (by design)

### Changes

- Package icon added (Logo.png)
- Package description updated to mention roots support
- Test count: 40 → 43 (unit) + 5 (roots integration)

---

## v0.2.0 (2026-03-22)

### New Tools

- **`delete_file`** — Delete a file or directory with recursive safety guard (`Destructive` annotation)
- **`append_file`** — Append content to end of file with automatic newline separator
- **`read_file_lines`** — Read a specific line range (1-based) with 10,000-line cap for large files

### Changes

- Tool count: 12 → 15
- MCP tool annotations added (`ReadOnly`, `Destructive`, `Idempotent`)
- Test count: 21 → 40 (31 unit + 9 integration)

---

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
