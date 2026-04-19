using System;
using MonoMod.Cil;
using Terraria;
using Terraria.ModLoader;

namespace ArmSlopeFix
{
    /// <summary>
    /// Fixes ARM64 float precision bug in Terraria's slope collision code.
    ///
    /// On ARM64 (macOS Apple Silicon), strict IEEE 754 32-bit precision causes
    /// the slope pass-through check in both Collision.TileCollision and
    /// Collision.SlopeCollision to fail when Velocity.X is zero (player stopped).
    /// The check uses Math.Abs(Velocity.X) as tolerance — when the player stops,
    /// the tolerance disappears and the check fails by ~1 ULP.
    ///
    /// This mod replaces Math.Abs(Velocity.X) with a fixed 1-pixel tolerance,
    /// making the check consistent regardless of player speed.
    ///
    /// Install on server for NPC/mob fix, on client for player fix, or both.
    /// </summary>
    public class ArmSlopeFix : Mod
    {
        // 1 pixel tolerance. Replaces Math.Abs(Velocity.X) which scaled
        // with speed and caused the check to fail when velocity was zero.
        private const float SLOPE_TOLERANCE = 1f;

        public override void Load()
        {
            IL_Collision.TileCollision += PatchTileCollisionSlopeCheck;
            IL_Collision.SlopeCollision += PatchSlopeCollisionSlopeCheck;
        }

        public override void Unload()
        {
            IL_Collision.TileCollision -= PatchTileCollisionSlopeCheck;
            IL_Collision.SlopeCollision -= PatchSlopeCollisionSlopeCheck;
        }

        /// <summary>
        /// Patches the slope pass-through check in Collision.TileCollision.
        ///
        /// Original (for both slope types):
        ///   vector3.Y + Height - Math.Abs(Velocity.X) <= vector4.Y + num10
        ///
        /// Patched:
        ///   vector3.Y + Height - 1 <= vector4.Y + num10
        /// </summary>
        private static void PatchTileCollisionSlopeCheck(ILContext il)
        {
            try
            {
                var c = new ILCursor(il);
                int patches = PatchMathAbsCalls(c, "TileCollision");

                if (patches != 2)
                {
                    var log = ModContent.GetInstance<ArmSlopeFix>().Logger;
                    log.Warn($"ArmSlopeFix: TileCollision expected 2 patches but applied {patches}. " +
                             "The tModLoader version may have changed the collision code.");
                }
            }
            catch (Exception e)
            {
                var log = ModContent.GetInstance<ArmSlopeFix>().Logger;
                log.Error($"ArmSlopeFix: Failed to patch TileCollision: {e}");
            }
        }

        /// <summary>
        /// Patches the slope pass-through check in Collision.SlopeCollision.
        ///
        /// Original (for both slope types):
        ///   vector.Y + Height - Math.Abs(Velocity.X) - 1f <= vector4.Y + num6
        ///
        /// Patched:
        ///   vector.Y + Height - 1 - 1f <= vector4.Y + num6
        ///
        /// SlopeCollision is what ACTUALLY corrects the player position to
        /// follow the slope surface. If this check fails on ARM64, the
        /// player never gets adjusted to the slope, and then TileCollision
        /// sees the slope as a solid wall blocking horizontal movement.
        /// </summary>
        private static void PatchSlopeCollisionSlopeCheck(ILContext il)
        {
            try
            {
                var c = new ILCursor(il);
                int patches = PatchMathAbsCalls(c, "SlopeCollision");

                if (patches != 2)
                {
                    var log = ModContent.GetInstance<ArmSlopeFix>().Logger;
                    log.Warn($"ArmSlopeFix: SlopeCollision expected 2 patches but applied {patches}. " +
                             "The tModLoader version may have changed the collision code.");
                }
            }
            catch (Exception e)
            {
                var log = ModContent.GetInstance<ArmSlopeFix>().Logger;
                log.Error($"ArmSlopeFix: Failed to patch SlopeCollision: {e}");
            }
        }

        /// <summary>
        /// Finds all calls to Math.Abs and replaces them with a fixed constant.
        /// Returns the number of patches applied.
        /// </summary>
        private static int PatchMathAbsCalls(ILCursor c, string methodName)
        {
            int patches = 0;
            while (c.TryGotoNext(
                i => i.MatchCall(typeof(Math), nameof(Math.Abs))))
            {
                // Before: ... Velocity.X, call Math.Abs
                // After:  ... (pop Velocity.X), ldc.r4 SLOPE_TOLERANCE
                c.Emit(Mono.Cecil.Cil.OpCodes.Pop);
                c.Emit(Mono.Cecil.Cil.OpCodes.Ldc_R4, SLOPE_TOLERANCE);
                c.Index++; // skip past the original call Math.Abs
                patches++;
            }
            return patches;
        }
    }
}
