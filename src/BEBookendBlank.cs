using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Bookends;

/// <summary>
/// A placed bookend blank: remembers the design the player chose to carve - it carves BOTH sides
/// (Calm, 2026-10-08: "can it make 2 bookends?"). The choice arrives from the client's carving
/// window as a block entity packet and is checked here, so a client can only ask for a design that
/// has a left and a right. Figurines' blank, for a design.
/// </summary>
public sealed class BEBookendBlank : BlockEntity
{
    public const int ChoosePacket = 1001;

    public string Design { get; private set; }

    public bool HasChoice => !string.IsNullOrEmpty(Design);

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(fromPlayer, packetid, data);
        if (packetid != ChoosePacket || data == null) return;
        if (!Api.World.Claims.TryAccess(fromPlayer, Pos, EnumBlockAccessFlags.Use)) return;

        string design = Encoding.UTF8.GetString(data);
        if (BookendDesigns.Design(design) == null) return;

        Design = design;
        MarkDirty(true);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (Design != null) tree.SetString("design", Design);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Design = tree.GetString("design");
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        dsc.AppendLine(HasChoice
            ? Lang.Get("bookends:blank-carving", BookendDesigns.DesignName(Design))
            : Lang.Get("bookends:blank-choose"));
    }
}
