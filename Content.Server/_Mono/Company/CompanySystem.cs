using Content.Shared._Mono.Company;
using Content.Shared.Access.Components;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Server.Database; // Forge-Change: company whitelist
using System.Threading.Tasks; // Forge-Change: company whitelist
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Containers;

namespace Content.Server._Mono.Company;

/// <summary>
/// This system handles assigning a company to players when they join.
/// TODO: remove hardcoded slop.
/// whoever hardcoded ts is getting slimed out no joke.
/// </summary>
public sealed partial class CompanySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly CompanyManager _manager = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;

    // Dictionary to store original company preferences for players
    private readonly Dictionary<string, string> _playerOriginalCompanies = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        _playerOriginalCompanies.Remove(args.Player.UserId.ToString());
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var companyComp = EnsureComp<CompanyComponent>(args.Mob);

        var playerId = args.Player.UserId.ToString();
        var profileCompany = args.Profile.Company;

        if (!_playerOriginalCompanies.ContainsKey(playerId))
        {
            _playerOriginalCompanies[playerId] = profileCompany;
        }

        var assigned = false;
        if (args.JobId != null)
        {
            var job = _prototypeManager.Index<JobPrototype>(args.JobId);
            companyComp.CompanyName = job.AssignedCompany;
            assigned = companyComp.CompanyName != "None";
        }
        if (!assigned)
        {
            bool loginFound = false;

            if (string.IsNullOrEmpty(profileCompany))
            {
                foreach (var companyProto in _prototypeManager.EnumeratePrototypes<CompanyPrototype>())
                {
                    if (_manager.IsAllowed(args.Player, companyProto))
                    {
                        companyComp.CompanyName = companyProto.ID;
                        loginFound = true;
                        break;
                    }
                }
            }

            if (!loginFound)
            {
                if (string.IsNullOrEmpty(profileCompany))
                    profileCompany = "None";

                companyComp.CompanyName = profileCompany;
            }
        }

        // Forge-change-start
        if (_prototypeManager.TryIndex<CompanyPrototype>(companyComp.CompanyName, out var proto))
        {
            foreach (var special in proto.Special)
            {
                special.AfterEquip(args.Mob);
            }
        }
        // Forge-change-end

        Dirty(args.Mob, companyComp);

        UpdateIdCardCompany(args.Mob, companyComp.CompanyName);
    }

    private void UpdateIdCardCompany(EntityUid playerEntity, string companyName)
    {
        if (!_inventorySystem.TryGetSlotEntity(playerEntity, "id", out var idUid))
            return;

        var cardId = idUid.Value;
        EntityUid? pdaUid = null;

        // Check if it's a PDA
        if (TryComp<PdaComponent>(idUid, out var pdaComponent) && pdaComponent.ContainedId != null)
        {
            pdaUid = idUid.Value;
            cardId = pdaComponent.ContainedId.Value;
        }

        // Update company name on existing card
        if (TryComp<IdCardComponent>(cardId, out var idCard))
        {
            _idCardSystem.TryChangeCompanyName(cardId, companyName, idCard);
        }

        if (companyName == "TelAdi")
        {
            if (pdaUid != null)
            {
                ReplacePdaWithTelAdi(playerEntity, pdaUid.Value, cardId);
            }
            else
            {
                SpawnTelAdiPdaWithCard(playerEntity, cardId);
            }
        }
    }

    /// <summary>
    /// Replaces the player's existing PDA with TelAdiPDA,
    /// applying name/job/access to the card that's already inside it.
    /// </summary>
    private void ReplacePdaWithTelAdi(EntityUid playerEntity, EntityUid oldPdaUid, EntityUid oldCardId)
    {
        // Read data from old card
        string? fullName = null;
        string? jobTitle = null;

        if (TryComp<IdCardComponent>(oldCardId, out var oldCard))
        {
            fullName = oldCard.FullName;
            jobTitle = oldCard.LocalizedJobTitle;
        }

        // Copy old access tags
        HashSet<ProtoId<AccessLevelPrototype>> oldTags = new();
        if (TryComp<AccessComponent>(oldCardId, out var oldAccess))
            oldTags = new HashSet<ProtoId<AccessLevelPrototype>>(oldAccess.Tags);

        // Unequip old PDA and delete it (and its card)
        _inventorySystem.TryUnequip(playerEntity, "id", force: true);
        QueueDel(oldCardId);
        QueueDel(oldPdaUid);

        // Spawn new TelAdi PDA — card already exists inside from the prototype
        var newPda = Spawn("TelAdiPDA", Transform(playerEntity).Coordinates);

        // Apply data to the card that already exists inside the new PDA
        if (TryComp<PdaComponent>(newPda, out var newPdaComp) && newPdaComp.ContainedId != null)
        {
            ApplyCardData(newPdaComp.ContainedId.Value, fullName, jobTitle, oldTags);
        }

        // Equip new PDA
        _inventorySystem.TryEquip(playerEntity, newPda, "id", force: true);
    }

    /// <summary>
    /// Spawns a fresh TelAdiPDA when the player had no PDA (bare card),
    /// applying name/job/access to the card already inside it.
    /// </summary>
    private void SpawnTelAdiPdaWithCard(EntityUid playerEntity, EntityUid oldCardId)
    {
        // Read data from old card
        string? fullName = null;
        string? jobTitle = null;

        if (TryComp<IdCardComponent>(oldCardId, out var oldCard))
        {
            fullName = oldCard.FullName;
            jobTitle = oldCard.LocalizedJobTitle;
        }

        // Copy old access tags
        HashSet<ProtoId<AccessLevelPrototype>> oldTags = new();
        if (TryComp<AccessComponent>(oldCardId, out var oldAccess))
            oldTags = new HashSet<ProtoId<AccessLevelPrototype>>(oldAccess.Tags);

        // Unequip and delete old bare card
        _inventorySystem.TryUnequip(playerEntity, "id", force: true);
        QueueDel(oldCardId);

        // Spawn new TelAdi PDA — card already exists inside from the prototype
        var newPda = Spawn("TelAdiPDA", Transform(playerEntity).Coordinates);

        // Apply data to the card that already exists inside the new PDA
        if (TryComp<PdaComponent>(newPda, out var newPdaComp) && newPdaComp.ContainedId != null)
        {
            ApplyCardData(newPdaComp.ContainedId.Value, fullName, jobTitle, oldTags);
        }

        // Equip new PDA
        _inventorySystem.TryEquip(playerEntity, newPda, "id", force: true);
    }

    /// <summary>
    /// Applies name, job title, company name, and access tags (+ TelAdi) to a card entity.
    /// </summary>
    private void ApplyCardData(
        EntityUid cardUid,
        string? fullName,
        string? jobTitle,
        HashSet<ProtoId<AccessLevelPrototype>> extraTags)
    {
        if (TryComp<IdCardComponent>(cardUid, out var idCard))
        {
            _idCardSystem.TryChangeFullName(cardUid, fullName, idCard);
            _idCardSystem.TryChangeJobTitle(cardUid, jobTitle, idCard);
            _idCardSystem.TryChangeCompanyName(cardUid, "TelAdi", idCard);
        }

        if (TryComp<AccessComponent>(cardUid, out var access))
        {
            foreach (var tag in extraTags)
                access.Tags.Add(tag);
            access.Tags.Add("TelAdi");
            Dirty(cardUid, access);
        }
    }
}
