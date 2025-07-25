using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnPy
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            var harmony = new Harmony("com.ludumancer.pawnpy");
            
            // Patch to ensure PythonCommunication component exists
            harmony.Patch(
                original: AccessTools.Method(typeof(Game), "LoadGame"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(EnsurePythonComponent))
            );
            
            // Patch to capture pawn state changes
            harmony.Patch(
                original: AccessTools.Method(typeof(Pawn), "Tick"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(CapturePawnState))
            );
            
            // Patch to log command execution
            harmony.Patch(
                original: AccessTools.Method(typeof(Verse.AI.Pawn_PathFollower), "StartPath"),
                prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(LogPathCommand))
            );
            
            Log.Message("[PawnPy] Harmony patches applied");
        }

        // Ensures our communication component exists
        public static void EnsurePythonComponent()
        {
            if (Current.Game.GetComponent<PythonCommunication>() == null)
            {
                Current.Game.components.Add(new PythonCommunication(Current.Game));
                Log.Message("[PawnPy] Added PythonCommunication component to existing game");
            }
        }

        // Captures pawn state changes for Python
        public static void CapturePawnState(Pawn __instance)
        {
            if (__instance.IsHashIntervalTick(60) && __instance.Spawned)
            {
                PythonCommunication.Instance?.UpdatePawnState(__instance);
            }
        }

        // Logs movement commands for debugging
        public static void LogPathCommand(Pawn ___pawn, LocalTargetInfo dest)
        {
            if (___pawn?.Faction == Faction.OfPlayer)
            {
                Log.Message($"[PawnPy] Pawn {___pawn.LabelShort} moving to {dest.Cell}");
            }
        }
    }
}