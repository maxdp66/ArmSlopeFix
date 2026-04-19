using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArmSlopeFix
{
    /// <summary>
    /// Fixes ARM64 float precision bug in Terraria's slope collision code.
    ///
    /// On ARM64 (macOS Apple Silicon), strict IEEE 754 32-bit precision causes
    /// the slope pass-through check in Collision.TileCollision (lines 751/755)
    /// to fail by a single ULP compared to x86_64 (where intermediates can
    /// retain extended precision via x87 registers or Rosetta 2 translation).
    ///
    /// This affects both players AND NPCs/mobs, which use the same collision
    /// pipeline. That's why this fix applies at the Collision level rather
    /// than per-entity hooks.
    ///
    /// Install on server for NPC/mob fix, on client for player fix, or both.
    /// </summary>
    public class ArmSlopeFix : Mod
    {
        public override void Load()
        {
            On_Collision.TileCollision += Hook_TileCollision;
        }

        public override void Unload()
        {
            On_Collision.TileCollision -= Hook_TileCollision;
        }

        private static Vector2 Hook_TileCollision(
            On_Collision.orig_TileCollision orig,
            Vector2 Position,
            Vector2 Velocity,
            int Width,
            int Height,
            bool fallThrough,
            bool fall2,
            int gravDir)
        {
            Vector2 result = orig(Position, Velocity, Width, Height, fallThrough, fall2, gravDir);

            // Only intervene when a vertical collision was detected
            // (TileCollision modified the Y velocity)
            if (Math.Abs(result.Y - Velocity.Y) < 0.001f)
                return result;

            // Check if any slope tile is in the entity's collision area.
            // If so, the vertical collision may be a false positive caused by
            // the float precision bug in the slope pass-through check.
            // Undo it by returning the original velocity — this lets the
            // entity continue without a position correction that would
            // trigger the falling animation.
            if (IsNearSlope(Position, Velocity, Width, Height))
                return Velocity;

            return result;
        }

        /// <summary>
        /// Scans tiles in and around the entity's movement area for any slope tile.
        /// </summary>
        private static bool IsNearSlope(Vector2 Position, Vector2 Velocity, int Width, int Height)
        {
            int left = Math.Max(0, (int)(Position.X / 16f) - 1);
            int right = Math.Min(Main.maxTilesX - 1, (int)((Position.X + Width + Math.Abs(Velocity.X)) / 16f) + 1);
            int top = Math.Max(0, (int)(Position.Y / 16f) - 1);
            int bottom = Math.Min(Main.maxTilesY - 1, (int)((Position.Y + Height + Math.Abs(Velocity.Y)) / 16f) + 1);

            for (int x = left; x <= right; x++)
            {
                for (int y = top; y <= bottom; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (tile != null && tile.HasTile && !tile.IsActuated
                        && Main.tileSolid[tile.TileType]
                        && tile.Slope > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
