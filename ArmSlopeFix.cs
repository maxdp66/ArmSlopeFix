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
    /// the slope pass-through check in Collision.TileCollision to fail when
    /// Velocity.X is zero (player stopped on slope). The check uses
    /// Math.Abs(Velocity.X) as a tolerance — when the player stops, the
    /// tolerance disappears and the check fails by ~1 ULP.
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
        }

        public override void Unload()
        {
            IL_Collision.TileCollision -= PatchTileCollisionSlopeCheck;
        }

        /// <summary>
        /// Patches the slope pass-through check in Collision.TileCollision.
        ///
        /// Original (for both slope types):
        ///   vector3.Y + Height - Math.Abs(Velocity.X) <= vector4.Y + num10
        ///
        /// Patched:
        ///   vector3.Y + Height - 1 <= vector4.Y + num10
        ///
        /// We find each `call Math.Abs` in the slope check section and replace
        /// it with a fixed constant. The Math.Abs call is unique to the slope
        /// checks in TileCollision.
        /// </summary>
        private static void PatchTileCollisionSlopeCheck(ILContext il)
        {
            try
            {
                var c = new ILCursor(il);
                int patches = 0;

                // Find each call to Math.Abs in the slope check section.
                // There are two: one for slope type 1 and one for slope type 2.
                while (c.TryGotoNext(
                    i => i.MatchCall(typeof(Math), nameof(Math.Abs))))
                {
                    // Before: ... Velocity.X, call Math.Abs
                    // After:  ... (pop Velocity.X), ldc.r4 1.0
                    //
                    // The stack has Velocity.X on top. We replace the Math.Abs
                    // call with: pop (remove Velocity.X), ldc.r4 SLOPE_TOLERANCE
                    c.Emit(Mono.Cecil.Cil.OpCodes.Pop);
                    c.Emit(Mono.Cecil.Cil.OpCodes.Ldc_R4, SLOPE_TOLERANCE);
                    c.Index++; // skip past the original call Math.Abs
                    patches++;
                }

                if (patches != 2)
                {
                    var log = ModContent.GetInstance<ArmSlopeFix>().Logger;
                    log.Warn($"ArmSlopeFix: Expected 2 patches but applied {patches}. " +
                             "The tModLoader version may have changed the collision code.");
                }
            }
            catch (Exception e)
            {
                var log = ModContent.GetInstance<ArmSlopeFix>().Logger;
                log.Error($"ArmSlopeFix: Failed to patch TileCollision: {e}");
            }
        }
    }
}
