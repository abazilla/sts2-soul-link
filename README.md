# Soul Link

A Slay the Spire 2 co-op mod that links all players together — sharing one HP pool and one gold pool.

## Features

- Shared HP across all players
- Shared gold across all players
- Configurable sync settings per run

## Requirements

- Slay the Spire 2
- .NET 9.0 SDK

## Building

Save the below code to a file named `Local.props` to set your STS2 path for building:

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

## Installing

Copy the output `SoulLink.dll` and `SoulLink.json` into your STS2 `mods/SoulLink/` folder
