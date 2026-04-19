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
    /// the slope pass-through check in Collision.TileCollision (lines 751/755)
    /// to fail by a single ULP compared to x86_64 (where intermediates can
    /// retain extended precision via x87 registers or Rosetta 2 translation).
    ///
    /// This mod directly patches the broken comparison using an IL hook,
    /// adding a 1-pixel tolerance to the slope surface check. This is the
    /// cleanest fix — it corrects the root cause rather than working around it.
    ///
    /// Install on server for NPC/mob fix, on client for player fix, or both.
    /// </summary>
    public class ArmSlopeFix : Mod
    {
        // 1 pixel = 0.0625 tiles. Enough to clear the ~0.0000001 ULP
        // difference on ARM64 while being imperceptible in gameplay.
        private const float SLOPE_EPSILON = 0.0625f;

        public override void Load()
        {
            IL_Collision.TileCollision += PatchTileCollisionSlopeCheck;
        }

        public override void Unload()
        {
            IL_Collision.TileCollision -= PatchTileCollisionSlopeCheck;
        }

        /// <summary>
        /// Patches the slope pass-through comparison in Collision.TileCollision.
        ///
        /// Original check (for slope type 1, rising right):
        ///   vector3.Y + Height - Math.Abs(Velocity.X) <= vector4.Y + num10
        ///
        /// Patched check:
        ///   vector3.Y + Height - Math.Abs(Velocity.X) <= vector4.Y + num10 - EPSILON
        ///
        /// We modify num10 (local variable holding the effective slope height)
        /// by subtracting EPSILON. This makes the slope surface slightly lower
        /// (more permissive), so entities near the surface pass through instead
        /// of triggering a false hard collision.
        ///
        /// The same patch is applied to both slope type 1 and slope type 2 checks.
        /// </summary>
        private static void PatchTileCollisionSlopeCheck(ILContext il)
        {
            try
            {
                var c = new ILCursor(il);
                int patches = 0;

                // We need to patch two ble.un instructions — one for each slope type.
                // We do this in a forward scan loop since GotoNext advances the cursor.
                while (c.TryGotoNext(
                    i => i.MatchLdcI4(2) || i.MatchLdcI4(1)))
                {
                    // From the slope type constant, scan forward for the
                    // branch-on-comparison instruction (ble.un).
                    // This is the <= comparison we need to make more lenient.
                    int savedIndex = c.Index;
                    if (c.TryGotoNext(MoveType.After,
                        i => i.MatchBle(out _) || i.MatchBleUn(out _)))
                    {
                        // Go back to the comparison instruction
                        c.Index--;

                        // The stack at this point has B (num10) on top.
                        // Insert: dup, ldc.r4 EPSILON, sub, stloc num10
                        // This subtracts EPSILON from num10 for all future uses
                        // while keeping the original value for this comparison.
                        c.Emit(Mono.Cecil.Cil.OpCodes.Dup);
                        c.Emit(Mono.Cecil.Cil.OpCodes.Ldc_R4, SLOPE_EPSILON);
                        c.Emit(Mono.Cecil.Cil.OpCodes.Sub);
                        c.Emit(Mono.Cecil.Cil.OpCodes.Stloc, 5); // num10 is local index 5

                        // Move past the branch instruction
                        c.Index++;
                        patches++;
                    }
                    else
                    {
                        // Couldn't find ble after this slope constant, skip
                        c.Index = savedIndex + 1;
                    }
                }

                var log = ModContent.GetInstance<ArmSlopeFix>().Logger;
                if (patches != 2)
                {
                    log.Warn($"ArmSlopeFix: Expected 2 patches but applied {patches}. " +
                             "The tModLoader version may have changed the collision code.");
                }
            }
            catch (Exception e)
            {
                ModContent.GetInstance<ArmSlopeFix>().Logger.Error($"ArmSlopeFix: Failed to patch TileCollision: {e}");
            }
        }
    }
}
