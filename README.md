# Auto Westland Racer Doomsday Last Survivors

> **This project is incomplete and still has room to grow.** Anyone can explore it, finish missing parts, improve it, and adapt it for their own use. No separate license is included in this repository.

This is an experimental mod for the **Wasteland Racer** event in **Doomsday: Last Survivors**. It runs with MelonLoader and automates car selection, card plays, and item use during races.

## Status and compatibility

- The project and mod behavior are not complete or guaranteed; game or MelonLoader updates may require changes.
- The code targets game assemblies from version `1.63.0` and Unity `2022.3.62f2`.
- The build target is `net6.0` and `x64`; the project uses MelonLoader `0.7.3`.
- Game and MelonLoader DLLs are not stored in this repository. A local game installation is required to build the mod.

## How it works

The mod starts from `MelonMod` and periodically reads the game's cached race state. `RaceApi` copies player, card, car, item, and race-stage data into a managed snapshot. `RacerBrain` uses the current phase—car selection, waiting, racing, or end—to decide which request to send, with at most one action per decision interval.

Requests go through the game's public race-module API; the mod does not construct network packets. Harmony patches observe server replies so the mod can pause or retry with another choice when a request is rejected.

### Cards and items

- `ComboEvaluator` finds legal card combinations in the player's hand and ranks them by estimated value. When playing a single card, it can choose a less useful card to discard.
- Smart Play can hold a single card while leading or near a supply station, and play cards more aggressively near the finish or while behind.
- Cars are prioritized in this order: Dickey Z4, Bourbon 1500, then Beetle.
- Paid props that use Sprint Tokens are disabled by default.

## In-game settings

Press **F9** to show or hide the mod panel. The panel controls the master switch, car selection, card play, item use, Smart Play, and other settings.

- In-race automation is enabled by default.
- **Auto match** is disabled by default. Enabling it sends match-entry requests automatically; each accepted match consumes one **Racer Pass**.
- Paid props that use Sprint Tokens are disabled by default.

## Build requirements

1. Windows and .NET SDK 6 or later.
2. The PC version of the game installed through Steam.
3. MelonLoader installed in the game directory. The default path is:
   `C:\Program Files (x86)\Steam\steamapps\common\Doomsday Last Survivors`

The project reads compile-time references directly from:

- `MelonLoader\net6`
- `MelonLoader\Il2CppAssemblies`

These references are used only during compilation and are not copied to the output directory. If the game is installed elsewhere, pass its directory through the `GameDir` property.

## Build

Run this command from the project root:

```powershell
dotnet build .\WestlandRacerMod.csproj -c Release -p:DeployToMods=false
```

If Steam is installed in a different location:

```powershell
dotnet build .\WestlandRacerMod.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Doomsday Last Survivors" -p:DeployToMods=false
```

The output is `bin\Release\win-x64\WestlandRacerMod.dll`. `DeployToMods=false` prevents automatic copying of the mod into the game directory. To use the mod, copy the output DLL into the game's `Mods` folder. Do not copy the MelonLoader, Unity, or game reference DLLs alongside it.

## Test card logic

```powershell
dotnet run --project .\tests\ComboTests\ComboTests.csproj -c Release
```

These tests exercise the standalone card-combination logic and do not require game DLLs.

## Source layout

- `src\WestlandRacerMod.cs` — MelonLoader entry point and panel hotkey.
- `src\RacerBrain.cs` — race state machine and request decisions.
- `src\RaceApi.cs` — reads game caches and calls the race API.
- `src\ComboEvaluator.cs` and `src\GameConfig.cs` — card evaluation and race data.
- `src\RacerSettings.cs` and `src\RacerUi.cs` — settings and the F9 panel.
- `src\ReplyPatches.cs` and `src\RacerStats.cs` — reply observation and session statistics.
- `tests\ComboTests` — card-combination logic tests.
