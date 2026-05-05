# Soul Link

Soul Link is a multiplayer Slay the Spire 2 co-op mod where all players share HP and Gold. Inspired by the various Soul Link mods.

## Features

### NOTE! This is a super early release version of the mod, and is nowhere near finished. Nothing is balanced, nor does everything work how you expect it to.

- Health is now a pool that's shared between all players. Changes to Current HP (healing/taking damage), and Max HP will change this shared pool, and should reflect in the UI
- All health changes are split by # of players (eg `gaining +9 MaxHP in a 2player game -> gain +4 MaxHP to the shared pool`).
    - Note that numbers shown in events/relics/cards/potions etc will not reflect the actual change - refer to the event log in the top right throughout the run
- Gold is now a pool that's shared between all players. Spending and gaining gold affects the shared pool, and should reflect in the number at the top. Don't be greedy and spend all the gold before your team does :)
- There are 3 extra panels that persist throughout the run which provide a bit more info on the state of the game

More changes are coming down the pipeline!

There's a bunch of changes and bugs that I'm aware of, but please let me know about bugs you encounter, or just any opinions on balance/gameplay and such. Keen to hear everyone's thoughts.

## Installing

Copy the zip's `SoulLink.dll` and `SoulLink.json` into your STS2 `mods/SoulLink/` folder.

## Building

Requirements:

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

## Other

- The latest release (v0.1.0) is NOT what's currently in main as of 23/04/26
- This is a project to help familiarise myself with some AI coding tools
- Check out the STS2 modding discord for more!
