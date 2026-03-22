# FieldCure MCP Filesystem Server

A secure [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server that exposes local filesystem operations to AI clients. Built with C# and the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

## Features

- **12 filesystem tools** — read, write, search, copy, move, and more
- **Sandboxed access** — all operations restricted to explicitly allowed directories
- **Security hardened** — path traversal prevention, symlink resolution, NTFS ADS blocking, Windows reserved name rejection
- **Atomic writes** — temp-file-then-rename pattern prevents data corruption
- **Binary detection** — automatically returns base64 for binary files, UTF-8 for text
- **Stdio transport** — standard MCP subprocess model via JSON-RPC over stdin/stdout

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

## Quick Start

```bash
# Clone and build
git clone https://github.com/fieldcure/fieldcure-mcp-filesystem.git
cd fieldcure-mcp-filesystem
dotnet build

# Run with allowed directories
dotnet run --project src/FieldCure.Mcp.Filesystem -- "C:\Users\me\Documents" "C:\Projects"
```

## Configuration

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "dotnet",
      "args": [
        "run",
        "--project", "C:\\path\\to\\fieldcure-mcp-filesystem\\src\\FieldCure.Mcp.Filesystem",
        "--",
        "C:\\Users\\me\\Documents",
        "C:\\Projects"
      ]
    }
  }
}
```

Or with a published binary:

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "C:\\path\\to\\fieldcure-mcp-filesystem.exe",
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
      "command": "dotnet",
      "args": [
        "run",
        "--project", "C:\\path\\to\\fieldcure-mcp-filesystem\\src\\FieldCure.Mcp.Filesystem",
        "--",
        "${workspaceFolder}"
      ]
    }
  }
}
```

## Tools

### File Operations

| Tool | Description |
|------|-------------|
| `ReadFile` | Read file contents (text as UTF-8, binary as base64) |
| `ReadMultipleFiles` | Read multiple files at once with per-file error handling |
| `WriteFile` | Create or overwrite a file with atomic write |
| `ModifyFile` | Find and replace text (plain text or regex) |
| `CopyFile` | Copy a file or directory recursively |
| `MoveFile` | Move or rename a file or directory |

### Directory Operations

| Tool | Description |
|------|-------------|
| `ListDirectory` | List contents with type markers, sizes, and dates |
| `CreateDirectory` | Create a directory (recursive) |
| `DirectoryTree` | Hierarchical tree view with configurable depth |

### Search & Info

| Tool | Description |
|------|-------------|
| `SearchFiles` | Find files by glob pattern (e.g., `*.cs`, `readme*`) |
| `SearchWithinFiles` | Search text content across files with line numbers |
| `GetFileInfo` | File/directory metadata (size, dates, attributes) |

## Security Model

All paths are validated through `IPathValidator` before any filesystem operation:

1. **Allowed directories** — CLI args define the sandbox boundary
2. **Path normalization** — `.` and `..` resolved via `Path.GetFullPath()`
3. **Symlink resolution** — final target must be within allowed directories
4. **Directory-separator-aware prefix matching** — prevents `/allowed` from matching `/allowed-secret`
5. **NTFS ADS blocking** — alternate data stream paths rejected
6. **Windows reserved names** — `CON`, `PRN`, `AUX`, `NUL`, etc. rejected

## Project Structure

```
src/FieldCure.Mcp.Filesystem/
├── Program.cs                  # Entry point, DI setup, stdio transport
├── Security/
│   ├── IPathValidator.cs       # Path validation interface
│   └── PathValidator.cs        # Sandbox implementation
├── Tools/
│   ├── FileOperationTools.cs   # 6 file tools
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

# Publish single file
dotnet publish src/FieldCure.Mcp.Filesystem -c Release -r win-x64 --self-contained false
```

## License

[MIT](LICENSE)
