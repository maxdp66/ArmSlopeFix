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
        // Re-entrancy guard to prevent infinite recursion when we retry with nudge
        [ThreadStatic]
        private static bool _inRetry;

        public override void Load()
        {
            On_Collision.TileCollision += Hook_TileCollision;
        }

        public override void Unload()
        {
            On_Collision.TileCollision -= Hook_TileCollision;
            _inRetry = false;
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

            // Only intervene when:
            // 1. We're not already in a retry (prevent recursion)
            // 2. A vertical collision happened (result.Y != Velocity.Y, meaning blocked)
            // 3. The entity had downward velocity (was falling onto something)
            if (_inRetry)
                return result;

            bool verticalBlocked = Math.Abs(result.Y - Velocity.Y) > 0.001f && result.Y <= 0f;
            if (!verticalBlocked)
                return result;

            // Check if any slope tile is in the entity's collision area
            if (!IsNearSlope(Position, Velocity, Width, Height))
                return result;

            // Retry with a tiny upward nudge — this shifts the float comparison
            // in TileCollision's slope check past the threshold that ARM64 fails.
            // 0.0625f = 1 pixel = 1/16th tile. Imperceptible but enough to clear
            // the ~0.0000001 ULP difference.
            const float EPSILON = 0.0625f;
            Vector2 nudgedPos = new Vector2(Position.X, Position.Y - EPSILON);

            _inRetry = true;
            try
            {
                Vector2 retry = orig(nudgedPos, Velocity, Width, Height, fallThrough, fall2, gravDir);
                // If the nudge resolved the collision (no longer fully blocked), use it
                if (retry.Y > result.Y + 0.001f)
                    return retry;
            }
            finally
            {
                _inRetry = false;
            }

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
