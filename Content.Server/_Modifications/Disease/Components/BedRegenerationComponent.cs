// Developed by Nox for the Sunrise Station project.
// Author: KloopRe

using Content.Shared._Modifications.Disease;

namespace Content.Server._Modifications.Disease.Components;

[RegisterComponent]
public sealed partial class BedRegenerationComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public BedRegenerationType RegenerationType = BedRegenerationType.Normal;
}
