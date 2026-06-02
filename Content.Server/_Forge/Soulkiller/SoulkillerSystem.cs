using Content.Shared._CorvaxNext.Silicons.Borgs;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared._Forge.Soulkiller;
using Content.Shared.Actions;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server._Forge.Soulkiller;

/// <summary>
/// Implements the "Душегуб" mechanic: an IPC (КПБ) uses a wall connector to move their mind into a
/// Station-AI core, becoming a fully-functional station AI (eye, cameras, laws, jump-to-core and
/// borg remote control) — while their real body is frozen in place by the connector and can be
/// returned to at any time.
///
/// Uses real mind transfer (<see cref="SharedMindSystem.TransferTo"/>) rather than visiting, so the
/// station-AI borg-control flow (which itself transfers the mind) works natively. The operator's
/// original body is stored and the mind is transferred back on return. The connection also breaks
/// automatically if the core loses power, the core is destroyed, or the body dies / enters crit.
/// </summary>
public sealed class SoulkillerSystem : SharedSoulkillerSystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedAiRemoteControlSystem _aiRemote = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SoulkillerConnectorComponent, GetVerbsEvent<AlternativeVerb>>(OnConnectorVerbs);

        SubscribeLocalEvent<SoulkillerInhabitantComponent, SoulkillerReturnToBodyEvent>(OnReturnToBody);

        SubscribeLocalEvent<SoulkillerComponent, EntityTerminatingEvent>(OnCoreTerminating);
        SubscribeLocalEvent<SoulkillerComponent, PowerChangedEvent>(OnCorePowerChanged);

        SubscribeLocalEvent<SoulkillerTetheredBodyComponent, MobStateChangedEvent>(OnBodyMobStateChanged);

        // Borg controlled via a Soulkiller AI dies → kick the mind back up to the AI core.
        SubscribeLocalEvent<AiRemoteControllerComponent, MobStateChangedEvent>(OnControlledBorgMobState);
    }

    private void OnConnectorVerbs(Entity<SoulkillerConnectorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("soulkiller-connector-verb-connect"),
            Priority = 2,
            Act = () => TryConnect(ent, user),
        });
    }

    /// <summary>
    /// Spawns a brain into the connector's linked (or nearest free) core and moves the user's mind
    /// into it, freezing their body in place.
    /// </summary>
    private void TryConnect(Entity<SoulkillerConnectorComponent> connector, EntityUid user)
    {
        if (!_mind.TryGetMind(user, out var mindId, out var mind))
        {
            _popup.PopupEntity(Loc.GetString("soulkiller-connector-no-mind"), connector, user);
            return;
        }

        if (mind.VisitingEntity != null)
        {
            _popup.PopupEntity(Loc.GetString("soulkiller-connector-already-visiting"), connector, user);
            return;
        }

        if (!TryResolveSoulkiller(connector, out var core))
        {
            _popup.PopupEntity(Loc.GetString("soulkiller-connector-no-shell"), connector, user);
            return;
        }

        // КПБ-only: the operator must be of the required species (IPC).
        if (!TryComp<HumanoidAppearanceComponent>(user, out var humanoid)
            || humanoid.Species.Id != core.Comp.RequiredSpecies)
        {
            _popup.PopupEntity(Loc.GetString("soulkiller-connector-wrong-species"), connector, user);
            return;
        }

        var container = _container.EnsureContainer<ContainerSlot>(core, core.Comp.MindSlotContainerId);
        if (core.Comp.InhabitingMind != null || container.ContainedEntity != null)
        {
            _popup.PopupEntity(Loc.GetString("soulkiller-connector-occupied"), connector, user);
            return;
        }

        // Spawn a brain into the core — inserting it grants the AiHeld components (full station AI).
        var brain = Spawn(core.Comp.BrainProto, Transform(core).Coordinates);
        if (!_container.Insert(brain, container))
        {
            Del(brain);
            _popup.PopupEntity(Loc.GetString("soulkiller-connector-occupied"), connector, user);
            return;
        }

        var inhabitant = EnsureComp<SoulkillerInhabitantComponent>(brain);
        inhabitant.Core = core;

        core.Comp.SpawnedBrain = brain;
        core.Comp.InhabitingMind = mindId;
        core.Comp.TetheredBody = user;
        Dirty(core);

        // Freeze the body in place (by the connector) so it can't be pulled away while connected.
        TetherBody(user, core);

        // Move the mind into the AI brain (real transfer → borg control etc. work natively).
        _mind.TransferTo(mindId, brain, mind: mind);

        // Grant the "return to body" action on the brain so the inhabitant can leave.
        _actions.AddAction(brain, ref core.Comp.ReturnActionEntity, core.Comp.ReturnAction);

        _popup.PopupEntity(Loc.GetString("soulkiller-connector-connected"), core, user);
    }

    /// <summary>
    /// Freezes the body where it stands (anchors it) and tags it so we can track / release it.
    /// </summary>
    private void TetherBody(EntityUid body, Entity<SoulkillerComponent> core)
    {
        _xform.AnchorEntity(body);

        var tether = EnsureComp<SoulkillerTetheredBodyComponent>(body);
        tether.Core = core;
    }

    /// <summary>
    /// Releases a tethered body: unanchors it and removes the tag.
    /// </summary>
    private void ReleaseBody(EntityUid body)
    {
        if (!HasComp<SoulkillerTetheredBodyComponent>(body))
            return;

        RemComp<SoulkillerTetheredBodyComponent>(body);

        if (!Terminating(body))
            _xform.Unanchor(body);
    }

    private void OnReturnToBody(Entity<SoulkillerInhabitantComponent> ent, ref SoulkillerReturnToBodyEvent args)
    {
        args.Handled = true;

        if (TryComp<SoulkillerComponent>(ent.Comp.Core, out var core))
            Disconnect((ent.Comp.Core, core));
    }

    private void OnCoreTerminating(Entity<SoulkillerComponent> ent, ref EntityTerminatingEvent args)
    {
        Disconnect((ent, ent.Comp), coreTerminating: true);
    }

    /// <summary>
    /// Core lost power → break the connection.
    /// </summary>
    private void OnCorePowerChanged(Entity<SoulkillerComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered && ent.Comp.InhabitingMind != null)
            Disconnect((ent, ent.Comp));
    }

    /// <summary>
    /// Operator's real body died or entered crit → break the connection.
    /// </summary>
    private void OnBodyMobStateChanged(Entity<SoulkillerTetheredBodyComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.Dead))
            return;

        if (TryComp<SoulkillerComponent>(ent.Comp.Core, out var core))
            Disconnect((ent.Comp.Core, core));
    }

    /// <summary>
    /// A borg controlled through a Soulkiller AI died/crit → return the mind up one level to the AI
    /// core (not all the way home). Vanilla AI-controlled borgs are left untouched.
    /// </summary>
    private void OnControlledBorgMobState(Entity<AiRemoteControllerComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.Dead))
            return;

        if (ent.Comp.LinkedMind == null
            || ent.Comp.AiHolder is not { } holder
            || !HasComp<SoulkillerInhabitantComponent>(holder))
            return;

        _aiRemote.ReturnMindIntoAi(ent);
    }

    /// <summary>
    /// Ends a connection: returns the inhabiting mind to its real body, releases the body, and
    /// removes the spawned brain so the core empties. Handles the case where the mind is currently
    /// off in a remote-controlled borg by clearing that link first.
    /// </summary>
    private void Disconnect(Entity<SoulkillerComponent> core, bool coreTerminating = false)
    {
        var mindId = core.Comp.InhabitingMind;
        var body = core.Comp.TetheredBody;
        var brain = core.Comp.SpawnedBrain;

        if (mindId is { } mind)
        {
            // If the mind is currently controlling a borg (not sitting in the brain), clear that
            // borg's remote-control link so it isn't left half-possessed when we pull the mind home.
            if (TryComp<MindComponent>(mind, out var mindComp)
                && mindComp.CurrentEntity is { } current
                && current != brain
                && TryComp<AiRemoteControllerComponent>(current, out var remote))
            {
                remote.AiHolder = null;
                remote.LinkedMind = null;
            }

            // Return the mind to the operator's real body.
            if (body is { } bodyUid && !Deleted(bodyUid))
                _mind.TransferTo(mind, bodyUid, ghostCheckOverride: true);
        }

        if (body is { } b)
            ReleaseBody(b);

        if (brain is { } br && !Deleted(br))
            QueueDel(br);

        core.Comp.ReturnActionEntity = null;
        core.Comp.SpawnedBrain = null;
        core.Comp.InhabitingMind = null;
        core.Comp.TetheredBody = null;

        if (!coreTerminating && !Terminating(core))
            Dirty(core);
    }

    /// <summary>
    /// Resolves the core to use: the explicit link if valid, otherwise the nearest free core.
    /// </summary>
    private bool TryResolveSoulkiller(Entity<SoulkillerConnectorComponent> connector, out Entity<SoulkillerComponent> core)
    {
        core = default;

        if (connector.Comp.LinkedSoulkiller is { } linked
            && TryComp<SoulkillerComponent>(linked, out var linkedComp))
        {
            core = (linked, linkedComp);
            return true;
        }

        var origin = _xform.GetMapCoordinates(connector);
        if (origin.MapId == MapId.Nullspace)
            return false;

        Entity<SoulkillerComponent>? best = null;
        var bestDist = float.MaxValue;

        var query = EntityQueryEnumerator<SoulkillerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.InhabitingMind != null)
                continue;

            var coords = _xform.GetMapCoordinates(uid, xform);
            if (coords.MapId != origin.MapId)
                continue;

            var dist = (coords.Position - origin.Position).Length();
            if (dist > connector.Comp.LinkRange || dist >= bestDist)
                continue;

            bestDist = dist;
            best = (uid, comp);
        }

        if (best == null)
            return false;

        core = best.Value;
        return true;
    }
}
