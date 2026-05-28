// Developed by Nox project.
// Author: KloopRe

using Content.Server._Modifications.Disease.Components;
using Content.Shared.Buckle.Components;
using Content.Shared._Modifications.Disease.Components;
using Content.Shared._Modifications.Disease;

namespace Content.Server._Modifications.Disease.Systems;

public sealed class BedRegenerationSystem : EntitySystem
{

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BedRegenerationComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<BedRegenerationComponent, UnstrappedEvent>(OnUnstrapped);
    }

    private void OnStrapped(Entity<BedRegenerationComponent> bed, ref StrappedEvent args)
    {
        if (TryComp<DiseaseComponent>(args.Buckle, out var diseaseComponent))
            diseaseComponent.RegenerationType = bed.Comp.RegenerationType;
    }

    private void OnUnstrapped(Entity<BedRegenerationComponent> bed, ref UnstrappedEvent args)
    {
        if (TryComp<DiseaseComponent>(args.Buckle, out var diseaseComponent))
            diseaseComponent.RegenerationType = BedRegenerationType.None;
    }
}
