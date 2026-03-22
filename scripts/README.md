# Scripts

## publish-nuget.ps1

Automates NuGet package build, code signing, and publishing.

### Prerequisites

- GlobalSign EV code signing USB dongle connected
- NuGet.org API key (issue at [nuget.org/account/apikeys](https://www.nuget.org/account/apikeys))

### Usage

```powershell
# pack → sign → push (full)
.\scripts\publish-nuget.ps1 -NuGetApiKey <key>

# API key via environment variable
$env:NUGET_API_KEY = '<key>'
.\scripts\publish-nuget.ps1

# pack → sign only (pre-release verification)
.\scripts\publish-nuget.ps1 -SkipPush

# pack only (build check, no USB dongle needed)
.\scripts\publish-nuget.ps1 -SkipSign -SkipPush
```

### Package

| PackageId | Project |
|---|---|
| `FieldCure.Mcp.Filesystem` | src/FieldCure.Mcp.Filesystem |

### Signing Certificate

- **Issuer**: GlobalSign
- **Subject**: Fieldcure Co., Ltd.
- **Method**: USB token (EV Code Signing)
- **Timestamp**: GlobalSign TSA

### Output

Built `.nupkg` files are created in the `artifacts/` directory.
