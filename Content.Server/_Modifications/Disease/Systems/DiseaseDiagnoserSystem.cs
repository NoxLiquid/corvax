using Robust.Server.Audio;
using Content.Shared.Examine;
using Robust.Shared.Containers;
using Content.Server._Modifications.Disease.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Paper;
using System.Linq;
using Content.Server.Power.EntitySystems;
using Robust.Shared.Prototypes;
using Content.Shared._Modifications.Disease.Components;
using Robust.Server.GameObjects;
using Content.Shared._Modifications.TimeWindow;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared._Modifications.Disease;
using Content.Shared._Modifications.Disease.Prototypes;
using Content.Shared.Humanoid.Prototypes;

namespace Content.Server._Modifications.Disease.Systems;

public sealed partial class DiseaseDiagnoserSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private DiseaseDiagnoserConsoleSystem _console = default!;
    [Dependency] private PowerReceiverSystem _powerReceiverSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private DiseaseDiagnoserDataServerSystem _dataServer = default!;
    [Dependency] private PaperSystem _paperSystem = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private TimedWindowSystem _timedWindowSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    private const string DnaContainerKey = "dna_container_disease_diagnoser";
    private const string FlaskContainerKey = "flask_container_disease_diagnoser";
    public ProtoId<ReagentPrototype> Reagent = "DiseaseSolution";
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DiseaseDiagnoserComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<DiseaseDiagnoserComponent, AnchorStateChangedEvent>(OnAnchor);
        SubscribeLocalEvent<DiseaseDiagnoserComponent, PortDisconnectedEvent>(OnPortDisconnected);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DiseaseDiagnoserComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!_powerReceiverSystem.IsPowered(uid))
            {
                SetStatus((uid, comp), DiseaseDiagnoserStatus.Off);
                continue; // без питания ничего не делаем
            }

            // Если был выключен — включаем
            if (comp.Status == DiseaseDiagnoserStatus.Off)
                SetStatus((uid, comp), DiseaseDiagnoserStatus.On);

            if (Exists(comp.CurrentSoundEntity) && comp.Status != DiseaseDiagnoserStatus.Printing)
                continue;

            switch (comp.Status)
            {
                case DiseaseDiagnoserStatus.Printing:
                    if (_timedWindowSystem.IsExpired(comp.AnimationWindow))
                    {
                        EndPrintingReport((uid, comp));
                        SetStatus((uid, comp), DiseaseDiagnoserStatus.On);
                    }
                    break;

                case DiseaseDiagnoserStatus.Scanning:
                    if (!CanScanning((uid, comp)))
                    {
                        SetStatus((uid, comp), DiseaseDiagnoserStatus.Denial);
                        break;
                    }

                    EndScanDisease((uid, comp));
                    break;

                case DiseaseDiagnoserStatus.GenerateDisease:
                    if (!CanGenerateDisease((uid, comp)))
                    {
                        SetStatus((uid, comp), DiseaseDiagnoserStatus.Denial);
                        break;
                    }

                    EndGenerateDisease((uid, comp));
                    break;

                case DiseaseDiagnoserStatus.Denial:
                    SetStatus((uid, comp), DiseaseDiagnoserStatus.On);
                    break;

                case DiseaseDiagnoserStatus.Successfully:
                    SetStatus((uid, comp), DiseaseDiagnoserStatus.On);
                    break;

                case DiseaseDiagnoserStatus.On:
                default:
                    break;
            }

        }
    }

    private void OnPortDisconnected(Entity<DiseaseDiagnoserComponent> ent, ref PortDisconnectedEvent args)
    {
        if (args.Port == ent.Comp.DiseaseDiagnoserPort)
            ent.Comp.ConnectedConsole = null;
    }

    private void OnAnchor(Entity<DiseaseDiagnoserComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (ent.Comp.ConnectedConsole == null || !TryComp<DiseaseDiagnoserConsoleComponent>(ent.Comp.ConnectedConsole, out var console))
            return;

        if (args.Anchored)
        {
            _console.RecheckConnections((ent.Comp.ConnectedConsole.Value, console));
            return;
        }

        _console.UpdateUserInterface((ent.Comp.ConnectedConsole.Value, console));
    }

    private void OnExamine(EntityUid uid, DiseaseDiagnoserComponent component, ExaminedEvent args)
    {
        BaseContainer? container = default!;

        if (_container.TryGetContainer(uid, DnaContainerKey, out container))
        {

            if (container is ContainerSlot slot)
            {
                if (slot.ContainedEntity != null)
                    args.PushMarkup(Loc.GetString("disease-diagnoser-dna-material-attached"));
            }
        }

        if (_container.TryGetContainer(uid, FlaskContainerKey, out container))
        {
            if (container is ContainerSlot slot)
            {
                if (slot.ContainedEntity != null)
                    args.PushMarkup(Loc.GetString("disease-diagnoser-flask-attached"));
            }
        }
    }

    public void StartPrinting(Entity<DiseaseDiagnoserComponent?> ent, DiseaseData? data)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (ent.Comp.Status == DiseaseDiagnoserStatus.Off)
            return;

        ent.Comp.DiseaseDataCPU = data;
        SetStatus((ent, ent.Comp), DiseaseDiagnoserStatus.Printing);
    }

    public void StartScanDisease(Entity<DiseaseDiagnoserComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (!CanScanning((ent, ent.Comp)))
        {
            SetStatus((ent, ent.Comp), DiseaseDiagnoserStatus.Denial);
            return;
        }

        SetStatus((ent, ent.Comp), DiseaseDiagnoserStatus.Scanning);
    }

    private void EndPrintingReport(Entity<DiseaseDiagnoserComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        var data = ent.Comp.DiseaseDataCPU;

        var paper = Spawn(ent.Comp.Paper, Transform(ent).Coordinates);
        if (!TryComp<PaperComponent>(paper, out var paperComp))
        {
            QueueDel(paper);
            return;
        }

        if (data == null)
        {
            var noDiseaseText = Loc.GetString("disease-report-no-disease");

            _paperSystem.SetContent((paper, paperComp), noDiseaseText);
            return;
        }

        var symptomsText =
            data.ActiveSymptom.Count == 0
                ? Loc.GetString("disease-report-symptoms-none")
                : string.Join(", ", data.ActiveSymptom.Select(symptom =>
                {
                    var id = symptom.ToString();
                    return _prototypeManager.TryIndex<DiseaseSymptomPrototype>(id, out var proto)
                        ? proto.Name
                        : id;
                }));

        string bodyText;
        if (data.SpeciesWhitelist == null || data.SpeciesWhitelist.Count == 0)
        {
            bodyText = Loc.GetString("disease-report-body-any");
        }
        else
        {
            var names = new List<string>();
            foreach (var protoId in data.SpeciesWhitelist)
            {
                if (_prototypeManager.TryIndex(protoId, out SpeciesPrototype? sp))
                    names.Add(Loc.GetString(sp.Name));
                else
                    names.Add(protoId.ToString());
            }

            bodyText = string.Join(", ", names);
        }

        string medicineText;
        if (data.MedicineResistance == null || data.MedicineResistance.Count == 0)
        {
            medicineText = Loc.GetString("disease-report-medicine-none");
        }
        else
        {
            var lines = new List<string>();
            foreach (var kvp in data.MedicineResistance)
            {
                var reagentName = _prototypeManager.TryIndex<ReagentPrototype>(kvp.Key, out var rp)
                    ? rp.LocalizedName
                    : kvp.Key.ToString();
                lines.Add(Loc.GetString("disease-report-medicine-entry", ("name", reagentName), ("value", kvp.Value.ToString("0.00"))));
            }

            medicineText = string.Join("\n", lines);
        }

        var content = Loc.GetString("disease-report-full",
            ("strainId", data.StrainId),
            ("threshold", data.MaxThreshold.ToString("0.0")),
            ("infectivity", (data.Infectivity * 100).ToString("0")),
            ("damageWhenDead", data.DamageWhenDead.ToString("0.0")),
            ("mutationPoints", data.MutationPoints.ToString("0")),
            ("regenThreshold", data.RegenThreshold.ToString("0.0")),
            ("regenMutation", data.RegenMutationPoints.ToString("0.0")),
            ("multiPriceDeleteSymptom", data.MultiPriceDeleteSymptom.ToString("0.0")),
            ("defaultMedicineResistance", data.DefaultMedicineResistance.ToString("0.00")),
            ("medicine", medicineText),
            ("symptoms", symptomsText),
            ("bodies", bodyText));

        _paperSystem.SetContent((paper, paperComp), content);
    }

    private void EndScanDisease(Entity<DiseaseDiagnoserComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        SetStatus((ent, ent.Comp), DiseaseDiagnoserStatus.Successfully);

        if (!_container.TryGetContainer(ent, DnaContainerKey, out var dnaContainer))
            return;

        if (dnaContainer is not ContainerSlot slot)
            return;

        if (slot.ContainedEntity == null)
            return;

        if (!TryComp<DiseaseDataCollectorComponent>(slot.ContainedEntity, out var dataCol))
            return;

        if (dataCol.Data == null)
            return;

        if (ent.Comp.ConnectedConsole == null || !TryComp<DiseaseDiagnoserConsoleComponent>(ent.Comp.ConnectedConsole, out var console))
            return;

        if (!TryComp<DiseaseDiagnoserDataServerComponent>(console.DiseaseDiagnoserDataServer, out var server))
            return;

        _dataServer.SaveData((console.DiseaseDiagnoserDataServer.Value, server), dataCol.Data);

        _container.CleanContainer(dnaContainer);

        _console.UpdateUserInterface((ent.Comp.ConnectedConsole.Value, console));
    }

    private void EndGenerateDisease(Entity<DiseaseDiagnoserComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        SetStatus((ent, ent.Comp), DiseaseDiagnoserStatus.Successfully);

        if (ent.Comp.DiseaseDataCPU == null)
            return;

        if (!_container.TryGetContainer(ent, FlaskContainerKey, out var dnaContainer))
            return;

        if (dnaContainer is not ContainerSlot slot)
            return;

        if (slot.ContainedEntity == null)
            return;

        var ents = _container.EmptyContainer(dnaContainer);

        foreach (var flask in ents)
        {
            if (!_solutionContainer.TryGetDrawableSolution(flask, out Entity<SolutionComponent>? solutionEntity, out Solution? solution))
                continue;

            if (solutionEntity != null && solution != null)
            {
                _solutionContainer.TryAddReagent(solutionEntity.Value, Reagent, solution.MaxVolume, out _);

                for (var i = 0; i < solution.Contents.Count; i++)
                {
                    var reagent = solution.Contents[i];

                    if (reagent.Reagent.Prototype != Reagent)
                        continue;

                    var reagentData = reagent.Reagent.Data != null
                        ? new List<ReagentData>(reagent.Reagent.Data)
                        : new List<ReagentData>();

                    reagentData.RemoveAll(x => x is DiseaseData);
                    reagentData.Add(ent.Comp.DiseaseDataCPU);

                    solution.Contents[i] = new ReagentQuantity(new ReagentId(Reagent, reagentData), reagent.Quantity);
                }

                _solutionContainer.UpdateChemicals(solutionEntity.Value);
            }
        }

    }

    public void StartGenerateDisease(Entity<DiseaseDiagnoserComponent?> ent, DiseaseData? data = null)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (!CanGenerateDisease((ent, ent.Comp)) || data == null)
        {
            SetStatus((ent, ent.Comp), DiseaseDiagnoserStatus.Denial);
            return;
        }

        ent.Comp.DiseaseDataCPU = data;
        SetStatus((ent, ent.Comp), DiseaseDiagnoserStatus.GenerateDisease);
    }

    private void UpdateAppearance(Entity<DiseaseDiagnoserComponent> ent)
    {
        if (TryComp<AppearanceComponent>(ent, out var appearance))
            _appearance.SetData(ent, DiseaseDiagnoserVisuals.Status, ent.Comp.Status, appearance);
    }

    private void SetStatus(Entity<DiseaseDiagnoserComponent?> ent, DiseaseDiagnoserStatus newStatus)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (ent.Comp.Status == newStatus)
            return;

        if (newStatus != DiseaseDiagnoserStatus.On)
            QueueDel(ent.Comp.CurrentSoundEntity);

        ent.Comp.CurrentSoundEntity = null;

        switch (newStatus)
        {
            case DiseaseDiagnoserStatus.On:

                break;
            case DiseaseDiagnoserStatus.Off:
                break;
            case DiseaseDiagnoserStatus.Printing:
                _timedWindowSystem.Reset(ent.Comp.AnimationWindow);
                ent.Comp.CurrentSoundEntity = _audio.PlayPvs(ent.Comp.PrintingSound, ent)?.Entity;
                break;
            case DiseaseDiagnoserStatus.Scanning:
                ent.Comp.CurrentSoundEntity = _audio.PlayPvs(ent.Comp.ScanningSound, ent)?.Entity;
                break;
            case DiseaseDiagnoserStatus.Denial:
                ent.Comp.CurrentSoundEntity = _audio.PlayPvs(ent.Comp.DenialSound, ent)?.Entity;
                break;
            case DiseaseDiagnoserStatus.Successfully:
                ent.Comp.CurrentSoundEntity = _audio.PlayPvs(ent.Comp.SuccessfullySound, ent)?.Entity;
                break;
            case DiseaseDiagnoserStatus.GenerateDisease:
                ent.Comp.CurrentSoundEntity = _audio.PlayPvs(ent.Comp.GenerateDiseaseSound, ent)?.Entity;
                break;
            default:

                break;
        }

        ent.Comp.Status = newStatus;

        UpdateAppearance((ent, ent.Comp));
    }

    public bool CanScanning(Entity<DiseaseDiagnoserComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        if (ent.Comp.Status == DiseaseDiagnoserStatus.Off)
            return false;

        if (!_container.TryGetContainer(ent, DnaContainerKey, out var dnaContainer))
            return false;

        if (dnaContainer is not ContainerSlot slot)
            return false;

        if (slot.ContainedEntity == null)
            return false;

        return true;
    }

    public bool CanGenerateDisease(Entity<DiseaseDiagnoserComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        if (!_container.TryGetContainer(ent, FlaskContainerKey, out var container))
            return false;

        if (container is not ContainerSlot slot)
            return false;

        if (slot.ContainedEntity == null)
            return false;

        return true;
    }

}