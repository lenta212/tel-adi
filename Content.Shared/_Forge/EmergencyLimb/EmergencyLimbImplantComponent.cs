using Content.Shared.Damage;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.EmergencyLimb;

/// <summary>
/// An emergency cybernetic-limb implant item. While activated (red cauterizer lit, via ItemToggle),
/// using it on a humanoid that's missing an arm or leg welds a crude replacement limb into the empty
/// slot, dealing burn damage in the process. The replacement works but has worse stats than a real limb.
/// </summary>
[RegisterComponent]
public sealed partial class EmergencyLimbImplantComponent : Component
{
    /// <summary>
    /// Damage dealt to the patient when the limb is installed (the cauterization burn).
    /// </summary>
    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public EntProtoId LeftArm = "EmergencyLeftArm";

    [DataField]
    public EntProtoId RightArm = "EmergencyRightArm";

    [DataField]
    public EntProtoId LeftLeg = "EmergencyLeftLeg";

    [DataField]
    public EntProtoId RightLeg = "EmergencyRightLeg";

    /// <summary>
    /// Whether the implant is consumed (deleted) after a successful install. One-shot emergency tool.
    /// </summary>
    [DataField]
    public bool ConsumeOnUse = true;
}
