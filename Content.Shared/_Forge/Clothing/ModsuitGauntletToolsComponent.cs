using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Clothing;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModsuitGauntletToolsComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId UrkProto = "ShipRepairDevice";

    [DataField, AutoNetworkedField]
    public EntProtoId OmnitoolProto = "OmnitoolModsuitGauntlet";

    [DataField, AutoNetworkedField]
    public EntProtoId WelderProto = "WelderExperimental";

    [DataField, AutoNetworkedField]
    public EntProtoId NaniteApplicatorProto = "NaniteApplicatorExperimental";

    [DataField, AutoNetworkedField]
    public EntityUid? UrkEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? OmnitoolEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? WelderEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? NaniteApplicatorEntity;

    [DataField, AutoNetworkedField]
    public bool UrkInHand;

    [DataField, AutoNetworkedField]
    public bool OmnitoolInHand;

    [DataField, AutoNetworkedField]
    public bool WelderInHand;

    [DataField, AutoNetworkedField]
    public bool NaniteApplicatorInHand;

    // Forge-change-start: extra Tel-Adi gauntlet tools.
    // Nullable so gauntlets that don't configure them (e.g. Omnissia) never spawn these.
    [DataField, AutoNetworkedField]
    public EntProtoId? RcdProto;

    [DataField, AutoNetworkedField]
    public EntProtoId? MultitoolProto;

    [DataField, AutoNetworkedField]
    public EntProtoId? SprayNozzleProto;

    [DataField, AutoNetworkedField]
    public EntityUid? RcdEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? MultitoolEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? SprayNozzleEntity;

    [DataField, AutoNetworkedField]
    public bool RcdInHand;

    [DataField, AutoNetworkedField]
    public bool MultitoolInHand;

    [DataField, AutoNetworkedField]
    public bool SprayNozzleInHand;
    // Forge-change-end
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class ModsuitGauntletToolComponent : Component
{
    [DataField]
    public EntityUid Gauntlets;

    /// <summary>
    /// Tool is stowed in nullspace; client hides the sprite while this is true.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool StoredHidden;
}
