# FieldCure.Mcp.Filesystem

**Secure MCP server for local filesystem operations** — read, write, search, copy, move files and directories with path sandboxing. Supports MCP roots protocol for runtime directory changes.

## Install

```bash
dotnet tool install -g FieldCure.Mcp.Filesystem
```

## Quick Start

```bash
fieldcure-mcp-filesystem "C:\Users\me\Documents" "C:\Projects"
```

Pass one or more directories as arguments to define the sandbox boundary.

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "fieldcure-mcp-filesystem",
      "args": ["C:\\Users\\me\\Documents", "C:\\Projects"]
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
      "args": ["${workspaceFolder}"]
    }
  }
}
```

## Tools (15)

| Category | Tools |
|----------|-------|
| **File** | `read_file`, `read_multiple_files`, `read_file_lines`, `write_file`, `append_file`, `modify_file`, `copy_file`, `move_file`, `delete_file` |
| **Directory** | `list_directory`, `create_directory`, `directory_tree` |
| **Search & Info** | `search_files`, `search_within_files`, `get_file_info` |

## MCP Roots Protocol

Supports runtime directory changes via the [MCP roots protocol](https://modelcontextprotocol.info/specification/draft/client/roots/). Clients can send `notifications/roots/list_changed` to switch allowed directories without restarting the server. CLI args serve as the initial value; roots override them entirely.

## Security

All paths are validated through a sandbox before any filesystem operation:

- **Allowed directories** — CLI args define the initial sandbox boundary (overridable via roots)
- **Path traversal prevention** — `..` resolved and validated
- **Symlink resolution** — final target must be within allowed directories
- **NTFS ADS blocking** — alternate data stream paths rejected
- **Windows reserved names** — `CON`, `PRN`, `AUX`, `NUL`, etc. rejected

## Requirements

- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) or later

## See Also

Part of the [AssistStudio ecosystem](https://github.com/fieldcure/fieldcure-assiststudio#packages).
## Links

- [GitHub](https://github.com/fieldcure/fieldcure-mcp-filesystem)
- [MCP Specification](https://modelcontextprotocol.io)
