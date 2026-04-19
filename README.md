# FieldCure MCP Filesystem Server

[![NuGet](https://img.shields.io/nuget/v/FieldCure.Mcp.Filesystem)](https://www.nuget.org/packages/FieldCure.Mcp.Filesystem)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-mcp-filesystem/blob/main/LICENSE)

A secure [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server that exposes local filesystem operations to AI clients. Built with C# and the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

## Features

- **17 filesystem tools**: read, write, append, delete, search, copy, move, convert-to-markdown, and more
- **Sandboxed access**: all operations restricted to explicitly allowed directories
- **MCP roots protocol**: clients can change allowed directories at runtime without restarting the server
- **Security hardened**: path traversal prevention, symlink resolution, NTFS ADS blocking, Windows reserved name rejection
- **Atomic writes**: temp-file-then-rename pattern prevents data corruption
- **Binary detection**: automatically returns base64 for binary files, UTF-8 for text
- **Built-in document parsing**: supported document formats can be read directly and converted to markdown on disk
- **Stdio transport**: standard MCP subprocess model via JSON-RPC over stdin/stdout

## Installation

### dotnet tool

```bash
dotnet tool install -g FieldCure.Mcp.Filesystem
```

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

### VS Code

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

## Tools

### File Operations

| Tool | Description |
|------|-------------|
| `read_file` | Read file contents (text as UTF-8, binary as base64) |
| `read_multiple_files` | Read multiple files at once with per-file error handling |
| `read_file_lines` | Read a specific range of lines |
| `convert_to_markdown` | Convert one supported document file into a markdown file on disk |
| `convert_directory_to_markdown` | Batch-convert supported document files in a directory to markdown files |
| `write_file` | Create or overwrite a file with atomic write |
| `append_file` | Append content to end of file with auto-newline |
| `modify_file` | Find and replace text (plain text or regex) |
| `copy_file` | Copy a file or directory recursively |
| `move_file` | Move or rename a file or directory |
| `delete_file` | Delete a file or directory |

### Directory Operations

| Tool | Description |
|------|-------------|
| `list_directory` | List contents with type markers, sizes, and dates |
| `create_directory` | Create a directory recursively |
| `directory_tree` | Hierarchical tree view with configurable depth |

### Search & Info

| Tool | Description |
|------|-------------|
| `search_files` | Find files by glob pattern |
| `search_within_files` | Search text content across files with line numbers |
| `get_file_info` | File or directory metadata |

## MCP Roots Protocol

The server supports the MCP roots protocol for runtime directory changes. When the client changes roots:

1. The server requests the updated roots list.
2. Allowed directories are replaced entirely.
3. CLI args provide the initial sandbox.
4. An empty roots list means all file access is denied.

## Security Model

All paths are validated through `IPathValidator` before any filesystem operation:

1. Allowed directories define the sandbox boundary.
2. `.` and `..` are normalized via `Path.GetFullPath()`.
3. Symlinks must resolve within allowed directories.
4. Prefix matching is directory-separator-aware.
5. NTFS alternate data streams are blocked.
6. Windows reserved names are rejected.
7. Allowed directory updates are thread-safe.

The markdown conversion tools validate both source and output paths through the same sandbox rules. They write converted markdown directly to disk instead of returning full document text through MCP, which is usually more token-efficient than `read_file` plus `write_file`.

## Project Structure

```text
src/FieldCure.Mcp.Filesystem/
|- Program.cs
|- Security/
|  |- IPathValidator.cs
|  \- PathValidator.cs
|- Tools/
|  |- FileOperationTools.cs
|  |- DirectoryTools.cs
|  \- SearchTools.cs
\- Utilities/
   |- EncodingDetector.cs
   \- FileSize.cs
```

## Development

```bash
dotnet build
dotnet test
dotnet pack src/FieldCure.Mcp.Filesystem -c Release
```

## Limitations

| Supported | Not Yet Supported |
|-----------|-------------------|
| Text file read/write (UTF-8) | Non-UTF-8 encoding auto-detection |
| Binary file detection + base64 | Streaming for very large files (>100 MB) |
| Atomic writes | File watching / change notifications |
| Glob pattern search | Full-text indexing |
| Regex find-and-replace | Multi-file transactional writes |
| Symlink resolution | Cross-platform symlink creation |
| MCP roots protocol | HTTP transport |
| Document text extraction / markdown conversion for formats supported by DocumentParsers | OCR for scanned documents |

## See Also

Part of the [AssistStudio ecosystem](https://github.com/fieldcure/fieldcure-assiststudio#packages).

## License

[MIT](LICENSE)
