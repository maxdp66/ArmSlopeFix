# ARM Slope Fix

Fixes a float precision bug in Terraria's collision code that causes players and NPCs to get stuck on slopes when running tModLoader as ARM64 on macOS (Apple Silicon).

## The Bug

On ARM64, strict IEEE 754 32-bit float precision causes the slope pass-through check in `Collision.TileCollision` to fail by a single ULP compared to x86_64. This means:

- Players can't walk up slopes going left-to-right (slope type 1)
- NPCs and mobs get stuck on the same slopes
- Works fine under x86_64 (Rosetta 2)

## How It Works

The mod hooks `Collision.TileCollision` — the same method used by both players and NPCs. When a vertical collision is detected near a slope tile that shouldn't have blocked movement, it retries with a 1-pixel upward nudge to clear the float precision threshold.

## Installation

### Client (single player / hosting)

1. Build the mod or download from releases
2. Copy `ArmSlopeFix.tmod` to your tModLoader Mods folder:
   - macOS: `~/Library/Application Support/Steam/steamapps/common/tModLoader/tModLoader/Mods/`
   - Or use the in-game Mod Browser if published to the Workshop
3. Enable the mod in-game

### Server (dedicated server with NPCs/mobs)

Same process — copy the `.tmod` file to the server's Mods directory:

```bash
# Linux server
cp ArmSlopeFix.tmod ~/.local/share/Terraria/tModLoader/Mods/

# Or if using the tModLoader server management script
cp ArmSlopeFix.tmod ~/tmodserver/.local/share/Terraria/tModLoader/Mods/
```

Then enable it in `enabled.json` or via the server console:

```
modlist add ArmSlopeFix
```

**Both client and server should have the mod installed** for the fix to apply to both player movement and NPC/mob behavior.

## Building

Requires tModLoader 1.4.4+ development setup.

```bash
# Point the .csproj at your tModLoader install, then:
dotnet build -c Release
```

Or build through the in-game Mod Sources menu.

## Technical Details

The root cause is in `Collision.cs` lines 751 and 755:

```csharp
// Slope type 1 (rising right)
vector3.Y + Height - Math.Abs(Velocity.X) <= vector4.Y + num10

// Slope type 2 (rising left)
vector3.Y + Height - Math.Abs(Velocity.X) <= vector4.Y + num10
```

On x86_64, intermediate float results can retain extended precision (x87 registers or Rosetta 2 translation). On ARM64, all operations are strict IEEE 754 32-bit. The difference is ~1 ULP (~0.0000001), but it's enough to flip the `<=` comparison from true (pass through) to false (hard collision).

## Compatibility

- Works on all platforms (the fix only activates when near slope tiles)
- Safe to use on x86_64 — the nudge is harmless if the original check passed
- Works server-side for NPC/mob fixes
- Minimal performance impact (slope scan is O(tile area) only on collision)
