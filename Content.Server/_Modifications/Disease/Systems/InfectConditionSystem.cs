using Content.Server._Modifications.Disease.Components;
using Content.Server.Mind;
using Content.Server.Objectives.Systems;
using Content.Shared._Modifications.Disease;
using Content.Shared._Modifications.Disease.Components;
using Content.Shared.Objectives.Components;

namespace Content.Server._Modifications.Disease.Systems;

public sealed partial class InfectConditionSystem : EntitySystem
{
    [Dependency] private NumberObjectiveSystem _number = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InfectConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
    }

    private void OnGetProgress(Entity<InfectConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        var target = _number.GetTarget(ent.Owner);
        if (target == 0)
        {
            args.Progress = 1f;
            return;
        }

        var count = ent.Comp.InfectedEntities.Count;
        args.Progress = count >= target ? 1f : (float)count / target;
    }
}
