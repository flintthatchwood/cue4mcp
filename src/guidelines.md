# CUE4Parse MCP Server Guidelines

## Overview

This MCP server provides tools for working with Unreal Engine archive files (PAK/UTOC/UCAS). The server uses CUE4Parse to read and extract data from game archives.

## General Concepts

### File Paths

**All file paths must use Linux-style forward slashes (`/`) as path separators.**

- File paths in tools do **not** include file extensions
- Example: `FortniteGame/Content/Athena/Items/Cosmetics/Characters/CID_A_112`
- Backslashes (`\`) are **not allowed** and will result in error code -32602
- Path matching is case-insensitive
- Paths are **not normalized** - they must be provided in the correct format

### Pattern Matching

All file filtering uses **regex patterns** with case-insensitive matching:

- **Must use forward slashes (`/`) for path separators in patterns**
- Backslashes are only valid when escaped for regex (`\\`) to match literal characters
- **Match file extensions:** `.*\.uasset$` or `.*\.umap$`
- **Match directories:** `ShooterGame/Content/Maps/.*`
- **Match substrings:** `.*Cosmetics.*Character.*`
- **Match multiple patterns:** `.*\.(uasset|umap)$`
- Invalid regex patterns return error code -32602

**Common regex examples:**
```
.*\.uasset$              - All .uasset files
.*\.umap$                - All .umap files
ShooterGame/.*           - All files under ShooterGame/
.*/Characters/.*         - All files in any Characters directory
.*Weapon.*\.uasset$      - All .uasset files with "Weapon" in the name
```

### Pagination

List operations use cursor-based pagination:

- **Cursor format:** Opaque base64-encoded tokens (treat as black box)
- **Page size:** Server-determined (currently 250 items)
- **Initial request:** Omit cursor parameter or use `cursor=null`
- **Continuation:** Use the `nextCursor` value from the previous response
- **End of results:** When `nextCursor` is not present in the response
- **Invalid cursor:** Returns error code -32602

**Example pagination flow:**
```
Request 1: (no cursor) → 250 items + nextCursor="MjUw"
Request 2: cursor="MjUw" → 250 items + nextCursor="NTAw"
Request 3: cursor="NTAw" → 150 items (no nextCursor = done)
```

### Package Structure

Unreal Engine packages contain:
- **Metadata:** Package summary with version info, flags, counts
- **Exports:** Individual objects/assets within the package (classes, blueprints, data, etc.)
- **Properties:** Key-value data on each export

**Typical workflow:**
1. List files to find packages of interest
2. Get file metadata to see available exports
3. Get specific exports to access full data

### Error Handling

All tools follow MCP error conventions:

| Error Code | Meaning |
|------------|---------|
| -32602 | Invalid/missing required parameters, invalid regex, backslash in path |
| -32002 | Package not found or load failure |

Error responses include:
```json
{
  "error": "Description of the error",
  "code": -32602,
  "stackTrace": "..."
}
```

## Path Separator Requirements

**Critical:** All path-based parameters must use forward slashes (`/`):

- ✅ Correct: `FortniteGame/Content/Items/Weapons/Rifle.uasset`
- ❌ Wrong: `FortniteGame\Content\Items\Weapons\Rifle.uasset`

**In regex patterns:**
- ✅ Correct: `ShooterGame/Content/.*` (forward slash for path)
- ✅ Correct: `.*\\.uasset$` (escaped backslash for regex literal)
- ❌ Wrong: `ShooterGame\Content\.*` (backslash as path separator)

**The server does NOT normalize paths - they must be correct on input.**

## Output Directory

Export tools write files to a configured output directory:
- Configured via server settings
- File list exports to: `{OutputDirectory}/all.json`
- Individual file exports to: `{OutputDirectory}/{filePath}.json`
- Automatically creates subdirectories as needed

## Performance Considerations

- **Pagination:** List operations return results in pages to avoid timeouts
- **Parallel processing:** Batch exports process multiple files concurrently
- **Caching:** Already-exported files are skipped automatically
- **Shared file provider:** A single file provider instance serves all tools
- **Server-side filtering:** All pattern matching happens in the server
- **Regex performance:** Complex patterns may impact filtering speed on large file sets

## Usage Tips

- Use list tools first to discover available files
- Always use forward slashes (`/`) in paths and patterns
- Regex patterns are powerful but can be complex - test on small sets first
- Export operations write to disk and can be slow for many files
- Get file metadata before loading full export data to check what's available
- Cursor pagination allows processing very large file sets incrementally
- Use `$` anchor to match file extensions precisely (e.g., `.*\.uasset$`)
- Remember: paths are case-insensitive but must use correct forward slash format
