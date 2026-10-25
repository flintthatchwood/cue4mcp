# Agent Notes

## Build
- Preferred build command for this repo is `./scripts/build.ps1`.
- This script downloads required native deps (for example `oo2core_9_win64.dll`) and publishes to `.work/build`.
- Use `dotnet build cue4mcp.slnx` only for quick compile checks; prefer `build.ps1` for normal build workflows.
