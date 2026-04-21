# FieldCure.Mcp.Filesystem

Secure MCP server for local filesystem operations: read, write, search, copy, move, and convert supported documents to markdown within a sandboxed directory boundary.

<!-- mcp-name: io.github.fieldcure/filesystem -->

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

### VS Code

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

## Tools (17)

| Category | Tools |
|----------|-------|
| File | `read_file`, `read_multiple_files`, `read_file_lines`, `convert_to_markdown`, `convert_directory_to_markdown`, `write_file`, `append_file`, `modify_file`, `copy_file`, `move_file`, `delete_file` |
| Directory | `list_directory`, `create_directory`, `directory_tree` |
| Search & Info | `search_files`, `search_within_files`, `get_file_info` |

## Markdown Conversion

`convert_to_markdown` and `convert_directory_to_markdown` are intended for cases where the user wants a file saved as markdown without forcing the LLM to read the entire document into context and then write it back out again.

The server parses supported document formats directly and writes `.md` files on disk, which is usually much more token-efficient than `read_file` plus `write_file`.

Supported conversion depends on the installed `FieldCure.DocumentParsers` package.

## MCP Roots Protocol

Supports runtime directory changes via the MCP roots protocol. Clients can send `notifications/roots/list_changed` to switch allowed directories without restarting the server. CLI args serve as the initial value; roots override them entirely.

## Security

All paths are validated through a sandbox before any filesystem operation:

- Allowed directories define the initial sandbox boundary
- Path traversal prevention resolves and validates `..`
- Symlink resolution requires the final target to remain inside allowed directories
- NTFS alternate data streams are blocked
- Windows reserved names such as `CON`, `PRN`, `AUX`, and `NUL` are rejected

Markdown conversion validates both input and output paths through the same sandbox.

## Requirements

- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) or later

## Links

- [GitHub](https://github.com/fieldcure/fieldcure-mcp-filesystem)
- [MCP Specification](https://modelcontextprotocol.io)
