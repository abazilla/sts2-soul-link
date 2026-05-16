# Soul Link

Soul Link is a multiplayer Slay the Spire 2 co-op mod where all players share HP and Gold. Inspired by the various Soul Link mods.

https://github.com/user-attachments/assets/2958dcbe-a784-4a89-a63a-d1345021a4d7

## Features

### NOTE! This is a super early release version of the mod, and is nowhere near finished. Nothing is balanced, nor does everything work how you expect it to.

- Shared Health Pool: Health can now be a shared pool between all players.
    - Changes to Current HP (healing/taking damage), and Max HP will change this shared pool
    - Split HP Mode: Health changes can also split by # of players (eg `gaining +9 MaxHP in a 2player game -> gain +4 MaxHP to the shared pool`).
- Shared Gold Pool: Gold can now be a shared pool between all players.
    - Spending and gaining gold affects the shared pool, and should reflect in the number at the top. Don't be greedy and spend all the gold before your team does :)
- Logs in the top right to show any events affected by the above options
- In lobby settings panel

## Installing

Copy the zip's `SoulLink.dll` and `SoulLink.json` into your STS2 `mods/SoulLink/` folder.

https://github.com/user-attachments/assets/272fa213-e270-4dd5-a026-d83b97959286

https://github.com/user-attachments/assets/f979f3dd-20fa-4303-8427-7a858d756cf1

## Known bugs

- (Shared Gold Pool) Event options that cost gold can be selected even if it falls below 0 via other players
- Values shown on relics/cards/events (particularly around health) do not reflect if the "HP/MaxHP Split toggle" is on

Please let me know about bugs you encounter, or just any opinions on balance/gameplay and such. Keen to hear everyone's thoughts.

## Building

Requirements:

- Slay the Spire 2
- .NET 9.0 SDK

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

- This is a project to help familiarise myself with some AI coding tools
- Check out the STS2 modding discord for more!

## Credits

- [HarmonyLib](https://github.com/pardeike/Harmony) by Andreas Pardeike — runtime patching used throughout the mod
- [sts2-modding-mcp](https://github.com/elliotttate/sts2-modding-mcp) by elliotttate — MCP server used during development

## License

MIT — see [LICENSE](LICENSE).
