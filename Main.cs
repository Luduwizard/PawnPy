using RimWorld;
using Verse;

namespace PawnPy
{
    public class PawnPy : Mod
    {
        public PawnPy(ModContentPack content) : base(content)
        {
            Log.Message("[PawnPy] Mod initialized - creating communication component");
            // Harmony patches are applied via static constructor
        }
    }
}