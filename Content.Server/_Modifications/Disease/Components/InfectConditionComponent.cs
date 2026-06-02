namespace Content.Server._Modifications.Disease.Components;

[RegisterComponent]
public sealed partial class InfectConditionComponent : Component
{
    public HashSet<EntityUid> InfectedEntities = new();
}
