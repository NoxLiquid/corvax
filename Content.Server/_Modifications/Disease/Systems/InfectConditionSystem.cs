using Content.Server._Modifications.Disease.Components;
using Content.Server.Mind;
using Content.Server.Objectives.Systems;
using Content.Shared._Modifications.Disease;
using Content.Shared._Modifications.Disease.Components;
using Content.Shared.Objectives.Components;

namespace Content.Server._Modifications.Disease.Systems;

public sealed partial class InfectConditionSystem : EntitySystem
{
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private NumberObjectiveSystem _number = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DiseaseComponent, CauseDiseaseEvent>(OnCauseDisease);
        SubscribeLocalEvent<InfectConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
    }

    private void OnCauseDisease(Entity<DiseaseComponent> entity, ref CauseDiseaseEvent args)
    {
        var strainId = args.SourceData.StrainId;
        if (string.IsNullOrEmpty(strainId))
            return;

        var query = EntityQueryEnumerator<SentientDiseaseComponent>();
        while (query.MoveNext(out var uid, out var sentient))
        {
            if (sentient.Data == null || sentient.Data.StrainId != strainId)
                continue;

            if (!_mind.TryGetMind(uid, out var mindUid, out var mind))
                continue;

            foreach (var obj in _mind.EnumerateObjectives<InfectConditionComponent>((mindUid, mind)))
            {
                var comp = Comp<InfectConditionComponent>(obj);
                comp.InfectedEntities.Add(entity.Owner);
            }
        }
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
