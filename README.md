# FieldCure MCP Filesystem Server

[![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.Filesystem)](https://www.nuget.org/packages/FieldCure.Mcp.Filesystem)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-mcp-filesystem/blob/main/LICENSE)

A secure [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server that exposes local filesystem operations to AI clients. Built with C# and the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

## Features

- **15 filesystem tools** — read, write, append, delete, search, copy, move, and more
- **Sandboxed access** — all operations restricted to explicitly allowed directories
- **MCP roots protocol** — clients can change allowed directories at runtime without restarting the server
- **Security hardened** — path traversal prevention, symlink resolution, NTFS ADS blocking, Windows reserved name rejection
- **Atomic writes** — temp-file-then-rename pattern prevents data corruption
- **Binary detection** — automatically returns base64 for binary files, UTF-8 for text
- **Stdio transport** — standard MCP subprocess model via JSON-RPC over stdin/stdout

## Installation

### dotnet tool (recommended)

```bash
dotnet tool install -g FieldCure.Mcp.Filesystem
```

After installation, the `fieldcure-mcp-filesystem` command is available globally.

### From source

```bash
git clone https://github.com/fieldcure/fieldcure-mcp-filesystem.git
cd fieldcure-mcp-filesystem
dotnet build
```

## Requirements

- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) or later

## Configuration

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "fieldcure-mcp-filesystem",
      "args": [
        "C:\\Users\\me\\Documents",
        "C:\\Projects"
      ]
    }
  }
}
```

### VS Code (Copilot)

Add to `.vscode/mcp.json`:

```json
{
  "servers": {
    "filesystem": {
      "command": "fieldcure-mcp-filesystem",
      "args": [
        "${workspaceFolder}"
      ]
    }
  }
}
```

### From source (without dotnet tool)

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "dotnet",
      "args": [
        "run",
        "--project", "C:\\path\\to\\fieldcure-mcp-filesystem\\src\\FieldCure.Mcp.Filesystem",
        "--",
        "C:\\Users\\me\\Documents"
      ]
    }
  }
}
```

## Tools

### File Operations

| Tool | Description |
|------|-------------|
| `read_file` | Read file contents (text as UTF-8, binary as base64) |
| `read_multiple_files` | Read multiple files at once with per-file error handling |
| `read_file_lines` | Read a specific range of lines (1-based, useful for large files) |
| `write_file` | Create or overwrite a file with atomic write |
| `append_file` | Append content to end of file with auto-newline |
| `modify_file` | Find and replace text (plain text or regex) |
| `copy_file` | Copy a file or directory recursively |
| `move_file` | Move or rename a file or directory |
| `delete_file` | Delete a file or directory (with recursive safety guard) |

### Directory Operations

| Tool | Description |
|------|-------------|
| `list_directory` | List contents with type markers, sizes, and dates |
| `create_directory` | Create a directory (recursive) |
| `directory_tree` | Hierarchical tree view with configurable depth |

### Search & Info

| Tool | Description |
|------|-------------|
| `search_files` | Find files by glob pattern (e.g., `*.cs`, `readme*`) |
| `search_within_files` | Search text content across files with line numbers |
| `get_file_info` | File/directory metadata (size, dates, attributes) |

## MCP Roots Protocol

The server supports the [MCP roots protocol](https://modelcontextprotocol.info/specification/draft/client/roots/) for runtime directory changes. When a client sends a `notifications/roots/list_changed` notification:

1. Server calls `roots/list` to get the new directory list from the client
2. Allowed directories are **replaced entirely** (override, not merge)
3. CLI args serve as the initial value; roots override them once received
4. Empty roots list = all file access denied

This enables multi-tab clients to switch project folders without restarting the server process.

## Security Model

All paths are validated through `IPathValidator` before any filesystem operation:

1. **Allowed directories** — CLI args define the initial sandbox boundary (overridable via roots)
2. **Path normalization** — `.` and `..` resolved via `Path.GetFullPath()`
3. **Symlink resolution** — final target must be within allowed directories
4. **Directory-separator-aware prefix matching** — prevents `/allowed` from matching `/allowed-secret`
5. **NTFS ADS blocking** — alternate data stream paths rejected
6. **Windows reserved names** — `CON`, `PRN`, `AUX`, `NUL`, etc. rejected
7. **Thread-safe directory updates** — `ReaderWriterLockSlim` protects concurrent access during roots changes

## Project Structure

```
src/FieldCure.Mcp.Filesystem/
├── Program.cs                  # Entry point, DI setup, stdio transport
├── Security/
│   ├── IPathValidator.cs       # Path validation interface
│   └── PathValidator.cs        # Sandbox implementation
├── Tools/
│   ├── FileOperationTools.cs   # 9 file tools
│   ├── DirectoryTools.cs       # 3 directory tools
│   └── SearchTools.cs          # 3 search/info tools
└── Utilities/
    └── EncodingDetector.cs     # Text/binary detection
```

## Development

```bash
# Build
dotnet build

# Test
dotnet test

# Pack as dotnet tool
dotnet pack src/FieldCure.Mcp.Filesystem -c Release
```

## Limitations

| Supported | Not Yet Supported |
|-----------|-------------------|
| Text file read/write (UTF-8) | Non-UTF-8 encoding auto-detection |
| Binary file detection + base64 | Streaming for very large files (>100 MB) |
| Atomic writes (temp + rename) | File watching / change notifications |
| Glob pattern search | Full-text indexing |
| Regex find-and-replace | Multi-file transactional writes |
| Symlink resolution | Cross-platform symlink creation |
| NTFS ADS blocking | Linux extended attributes |
| MCP roots protocol | HTTP transport (stdio only) |
| Document text extraction (DOCX, HWPX, XLSX, PPTX via DocumentParsers) | OCR for scanned documents |

## See Also

Part of the [AssistStudio ecosystem](https://github.com/fieldcure/fieldcure-assiststudio#packages).

## License

[MIT](LICENSE)
