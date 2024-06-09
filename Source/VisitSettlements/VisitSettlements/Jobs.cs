using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

public class JobDriver_InteractWithAlly : JobDriver
{
    private Pawn TargetPawn => (Pawn)TargetThingA;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return pawn.Reserve(TargetPawn, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

        Toil interactToil = new Toil();
        interactToil.initAction = () =>
        {
            Find.WindowStack.Add(new VS_Dialog_SettlementInteraction(pawn, TargetPawn));
        };
        yield return interactToil;
    }
}

[DefOf]
public static class SettlementJobDefOf
{
    public static JobDef InteractWithAlly;
}
