# Soul Link

Soul Link is a multiplayer Slay the Spire 2 co-op mod that links all players together — sharing one HP pool and one gold pool.

## Features

### NOTE! This is a super early release version of the mod, and is nowhere near finished. Nothing is balanced, nor does everything work how you expect it to.

- Shared HP across all players
- Shared gold across all players

More changes are coming down the pipeline!

## Installing

Copy zip's `SoulLink.dll` and `SoulLink.json` into your STS2 `mods/SoulLink/` folder.

## Building

Requirements:

- Slay the Spire 2
- .NET 9.0 SDK

Create a `Local.props` file in the project root with your STS2 path:

```xml
<Project>
  <PropertyGroup>
    <STS2GameDir>/path/to/Slay the Spire 2</STS2GameDir>
  </PropertyGroup>
</Project>
```

Then build:

```bash
dotnet build
```

## Other

- The latest release (v0.1.0) is NOT what's currently in main as of 23/04/26
- This is a project to help familiarise myself with some AI coding tools
- Check out the STS2 modding discord for more!
