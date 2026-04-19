# ARM Slope Fix

Fixes a float precision bug in Terraria's collision code that causes players and NPCs to get stuck on slopes when running tModLoader as ARM64 on macOS (Apple Silicon).

## The Bug

On ARM64, strict IEEE 754 32-bit float precision causes the slope pass-through check in `Collision.TileCollision` (lines 751/755) to fail by a single ULP compared to x86_64. This means:

- Players can't walk up slopes going left-to-right (slope type 1)
- NPCs and mobs get stuck on the same slopes
- Running server as ARM causes NPC/mob stuck behavior for all clients
- Works fine under x86_64 (Rosetta 2)

## How It Works

The mod hooks `Collision.TileCollision` — the shared method used by both player and NPC collision pipelines. When a vertical collision is detected near a slope tile that shouldn't have blocked movement, it retries with a 1-pixel upward nudge (0.0625 tile units) to clear the float precision threshold.

## Installation

### Quick Install (prebuilt)

1. Download `ArmSlopeFix.tmod` from [Releases](https://github.com/maxdp66/ArmSlopeFix/releases)
2. Copy to your tModLoader Mods folder:
   - **macOS (client):** `~/Library/Application Support/Steam/steamapps/common/tModLoader/tModLoader/Mods/`
   - **Linux (server):** `~/.local/share/Terraria/tModLoader/Mods/`
3. Enable in-game or add to `enabled.json` on server

### Build from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download) and a tModLoader installation.

```bash
git clone https://github.com/maxdp66/ArmSlopeFix.git
cd ArmSlopeFix
dotnet build -p:tMLSteamPath=/path/to/tModLoader/
```

The `.tmod` file will be placed in your tModLoader Mods folder automatically.

### Server Setup

Copy the `.tmod` to the server's Mods directory:

```bash
cp ArmSlopeFix.tmod ~/tmodserver/.local/share/Terraria/tModLoader/Mods/
```

Then enable via server console:

```
modlist add ArmSlopeFix
```

**Both client and server should have the mod** for the fix to apply to both player movement and NPC/mob behavior.

## Technical Details

The root cause is in `Collision.cs` lines 751 and 755:

```csharp
// Slope type 1 (rising right) — line 751
vector3.Y + Height - Math.Abs(Velocity.X) <= vector4.Y + num10

// Slope type 2 (rising left) — line 755
vector3.Y + Height - Math.Abs(Velocity.X) <= vector4.Y + num10
```

On x86_64, intermediate float results can retain extended precision (x87 registers or Rosetta 2 translation). On ARM64, all operations are strict IEEE 754 32-bit. The difference is ~1 ULP (~0.0000001), but it's enough to flip the `<=` comparison from true (pass through slope) to false (hard collision).

## Compatibility

- tModLoader 1.4.4.9+ (2026.02)
- Works on all platforms (fix only activates near slope tiles)
- Safe on x86_64 — the nudge is harmless if the original check passed
- Works server-side for NPC/mob fixes
- Minimal performance impact (slope scan is O(tile area) only on collision)
