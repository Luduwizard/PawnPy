using RimWorld;
using Verse;
using System.Linq;
using Verse.AI;

namespace PawnPy
{
    public abstract class PawnCommand
    {
        public int PawnID { get; set; }
        public abstract void Execute(Pawn pawn);
    }

    public class MoveToCommand : PawnCommand
    {
        public int X { get; set; }
        public int Z { get; set; }

        public override void Execute(Pawn pawn)
        {
            var target = new IntVec3(X, 0, Z);
            pawn.pather.StartPath(target, PathEndMode.OnCell);
        }
    }

    public class AttackCommand : PawnCommand
    {
        public int TargetID { get; set; }

        public override void Execute(Pawn pawn)
        {
            // Look for the target in the map
            Thing target = Find.CurrentMap.listerThings.AllThings
                .FirstOrDefault(t => t.thingIDNumber == TargetID);

            if (target != null)
            {
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.AttackMelee, target));
            }
        }
    }

    public class InteractCommand : PawnCommand
    {
        public int TargetID { get; set; }
        public string Interaction { get; set; }  // "Chat", "Arrest", etc.

        public override void Execute(Pawn pawn)
        {
            Pawn target = Find.CurrentMap.mapPawns.AllPawns
                .FirstOrDefault(p => p.thingIDNumber == TargetID);

            if (target != null)
            {
                switch (Interaction.ToLower())
                {
                    case "arrest":
                        pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Arrest, target));
                        break;
                    case "chat":
                        pawn.interactions.TryInteractWith(target, InteractionDefOf.Chitchat);
                        break;
                }
            }
        }
    }

    public class UseItemCommand : PawnCommand
    {
        public int ItemID { get; set; }

        public override void Execute(Pawn pawn)
        {
            Thing item = pawn.inventory?.innerContainer?
                .FirstOrDefault(t => t.thingIDNumber == ItemID);

            if (item != null)
            {
                // Example: Use comms console
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.UseCommsConsole, item));
            }
        }
    }
}