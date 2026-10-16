using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Bookends;

/// <summary>
/// What is pressed into a pair mold: one design, and which of its sides are in. Calm, 2026-10-08:
/// "you need to make 2 bookends and put both of them into the clay to make a left and right mold"
/// - and "left bear and right bear no mixing them". Only a mold with both sides fires.
/// </summary>
public sealed class MoldDesign
{
    public const string Key = "bookendmold";

    public string Design { get; set; }
    public bool Left { get; set; }
    public bool Right { get; set; }

    public bool Complete => Design != null && Left && Right;

    public bool Has(string side) => side == "left" ? Left : Right;

    public MoldDesign With(string side) => new() { Design = Design, Left = Left || side == "left", Right = Right || side == "right" };

    public void WriteTo(ITreeAttribute tree)
    {
        tree.SetString("design", Design);
        tree.SetBool("left", Left);
        tree.SetBool("right", Right);
    }

    public static MoldDesign From(ITreeAttribute tree)
    {
        string design = tree?.GetString("design");
        if (string.IsNullOrEmpty(design)) return null;
        return new MoldDesign { Design = design, Left = tree.GetBool("left"), Right = tree.GetBool("right") };
    }

    public static MoldDesign Of(ItemStack stack) => From(stack?.Attributes?.GetTreeAttribute(Key));
}

/// <summary>
/// The bookend PAIR mold - Figurines' mold made wide enough for two (Calm: "should be wider and make
/// sense for the 2 bookends"). Clay-formed raw; press a wooden left and a wooden right of ONE design
/// into it (both kept); fire it; pour 200 units and it gives the left and the right in that metal.
/// The design travels on the stack and in the block entity - pick it up, put it down, fire it: it is
/// never lost.
/// </summary>
internal static class PairMold
{
    public static ItemStack StackWith(Block block, MoldDesign design)
    {
        var stack = new ItemStack(block);
        design?.WriteTo(stack.Attributes.GetOrAddTreeAttribute(MoldDesign.Key));
        return stack;
    }

    public static string Name(Block block, MoldDesign design)
    {
        string baseName = Lang.Get("bookends:block-" + block.Code.Path);
        return design == null ? baseName : Lang.Get("bookends:mold-name-with", baseName, BookendDesigns.DesignName(design.Design));
    }

    public static void Info(StringBuilder dsc, MoldDesign design, bool raw)
    {
        if (design == null)
        {
            if (raw) dsc.AppendLine(Lang.Get("bookends:mold-blank"));
            return;
        }
        dsc.AppendLine(Lang.Get("bookends:mold-design", BookendDesigns.DesignName(design.Design)));
        if (!raw) return;
        if (design.Complete) dsc.AppendLine(Lang.Get("bookends:mold-ready"));
        else dsc.AppendLine(Lang.Get("bookends:mold-needs", BookendDesigns.SideName(design.Left ? "right" : "left")));
    }

    /// <summary>Into the player's hands, or at their feet.</summary>
    public static void Give(IWorldAccessor world, IPlayer player, ItemStack stack, BlockPos pos)
    {
        if (!player.InventoryManager.TryGiveItemstack(stack))
            world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.2, 0.5));
    }

    public static float FacingPlayer(IPlayer player, BlockSelection sel)
    {
        var target = sel.DidOffset ? sel.Position.AddCopy(sel.Face.Opposite) : sel.Position;
        double dx = player.Entity.Pos.X - (target.X + sel.HitPosition.X);
        double dz = player.Entity.Pos.Z - (target.Z + sel.HitPosition.Z);
        return (float)System.Math.Round(System.Math.Atan2(dx, dz) / GameMath.PIHALF) * GameMath.PIHALF;
    }

    public static string MetalName(string metal) => BookendDesigns.MaterialName(metal);
}

/// <summary>The raw (unfired) pair mold. Plain right-click places it; sneak + right-click puts it in ground storage for a pit kiln.</summary>
public sealed class BlockBookendMoldRaw : Block
{
    private static readonly AssetLocation ClaySound = new("game:sounds/effect/clayform");

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BEBookendMoldRaw be) return false;
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;
        var held = byPlayer.InventoryManager.ActiveHotbarSlot;
        var capi = world.Api as ICoreClientAPI;

        // A WOODEN bookend presses its side in; the bookend is kept.
        if (BookendData.Of(held.Itemstack) is { } bookend)
        {
            string design = bookend.Design, side = bookend.Side, material = bookend.Material;
            if (material != "wood") { capi?.TriggerIngameError(this, "notwood", Lang.Get("bookends:mold-needs-wood")); return false; }
            if (be.Design != null && be.Design.Design != design) { capi?.TriggerIngameError(this, "otherdesign", Lang.Get("bookends:mold-other-design", BookendDesigns.DesignName(be.Design.Design))); return false; }
            if (be.Design != null && be.Design.Has(side)) { capi?.TriggerIngameError(this, "sidein", Lang.Get("bookends:mold-side-in", BookendDesigns.SideName(side))); return false; }
            world.PlaySoundAt(ClaySound, blockSel.Position, 0, byPlayer);
            if (world.Side == EnumAppSide.Server) be.SetDesign((be.Design ?? new MoldDesign { Design = design }).With(side));
            return true;
        }

        // DRY GRASS on the placed mold starts a pit kiln round it - as Figurines' mold: the game only
        // builds a kiln on ground storage, and a placed mold is our own block.
        if (held.Itemstack?.Collectible is ItemDryGrass)
        {
            if (be.Design is not { Complete: true }) { capi?.TriggerIngameError(this, "notfireable", Lang.Get("bookends:mold-kiln-unfinished")); return true; }
            StartKiln(world, byPlayer, blockSel, be.Design);
            return true;
        }

        // Pick up with an empty hand, keeping what is pressed in.
        if (held.Empty)
        {
            if (world.Side == EnumAppSide.Server)
            {
                PairMold.Give(world, byPlayer, PairMold.StackWith(this, be.Design), blockSel.Position);
                world.BlockAccessor.SetBlock(0, blockSel.Position);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Swap the placed mold for ground storage holding it, then let the game build its pit kiln there
    /// (vssurvivalmod BlockPitkiln.TryCreateKiln - the same call ItemDryGrass makes, with all its checks
    /// and messages). Both sides, as ItemDryGrass does. If the kiln cannot be built, the mold goes back.
    /// </summary>
    private void StartKiln(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, MoldDesign design)
    {
        var pos = blockSel.Position;
        if (world.GetBlock(new AssetLocation("groundstorage")) is not BlockGroundStorage storage
            || world.GetBlock(new AssetLocation("pitkiln")) is not BlockPitkiln kiln) return;
        var props = GetBehavior<CollectibleBehaviorGroundStorable>()?.StorageProps;
        if (props == null) return;

        world.BlockAccessor.SetBlock(storage.BlockId, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityGroundStorage beg)
        {
            beg.ForceStorageProps(props);
            beg.MeshAngle = PairMold.FacingPlayer(byPlayer, blockSel);
            beg.Inventory[0].Itemstack = PairMold.StackWith(this, design);
            beg.Inventory[0].MarkDirty();
            if (kiln.TryCreateKiln(world, byPlayer, pos)) return;
        }

        world.BlockAccessor.SetBlock(BlockId, pos);
        (world.BlockAccessor.GetBlockEntity(pos) as BEBookendMoldRaw)?.SetDesign(design);
    }

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        bool placed = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);
        if (placed && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEBookendMoldRaw be) be.SetDesign(MoldDesign.Of(byItemStack));
        return placed;
    }

    private static MoldDesign DesignAt(IWorldAccessor world, BlockPos pos) => (world.BlockAccessor.GetBlockEntity(pos) as BEBookendMoldRaw)?.Design;

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1) =>
        [PairMold.StackWith(this, DesignAt(world, pos))];

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => PairMold.StackWith(this, DesignAt(world, pos));

    public override string GetHeldItemName(ItemStack itemStack) => PairMold.Name(this, MoldDesign.Of(itemStack));

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) => PairMold.Name(this, DesignAt(world, pos));

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        PairMold.Info(dsc, MoldDesign.Of(inSlot.Itemstack), true);
    }

    /// <summary>
    /// What the kiln fires it into. Both kilns ask THIS with the stack in hand and clone the answer's
    /// smelted stack (vssurvivalmod BEPitKiln.OnFired, BEBeeHiveKiln.ConvertItemToBurned) - so the
    /// fired mold comes out carrying the design. Without both sides there is nothing to fire into:
    /// the kiln says "not fireable".
    /// </summary>
    public override CombustibleProperties GetCombustibleProperties(IWorldAccessor world, ItemStack stack, BlockPos pos)
    {
        var props = base.GetCombustibleProperties(world, stack, pos);
        if (stack == null) return props;
        var design = MoldDesign.Of(stack);
        if (design is not { Complete: true } || props?.SmeltedStack?.ResolvedItemstack == null) return null;

        var fired = props.Clone();
        var firedStack = props.SmeltedStack.ResolvedItemstack.Clone();
        design.WriteTo(firedStack.Attributes.GetOrAddTreeAttribute(MoldDesign.Key));
        fired.SmeltedStack.ResolvedItemstack = firedStack;
        return fired;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        var design = DesignAt(world, selection.Position);
        if (design is not { Complete: true })
        {
            var stacks = design == null
                ? BookendDesigns.All(world).Take(1).SelectMany(d => BookendDesigns.Sides.Select(s => BookendDesigns.Make(world, d, s, "wood")))
                : [BookendDesigns.Make(world, design.Design, design.Left ? "right" : "left", "wood")];
            help.Add(new WorldInteraction { ActionLangCode = "bookends:mold-help-press", MouseButton = EnumMouseButton.Right, Itemstacks = stacks.Where(s => s != null).ToArray() });
        }
        else if (world.GetItem(new AssetLocation("drygrass")) is { } grass)
        {
            help.Add(new WorldInteraction { ActionLangCode = "bookends:mold-help-kiln", MouseButton = EnumMouseButton.Right, Itemstacks = [new ItemStack(grass)] });
        }
        help.Add(new WorldInteraction { ActionLangCode = "bookends:mold-help-pickup", MouseButton = EnumMouseButton.Right, RequireFreeHand = true });
        return help.Concat(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)).ToArray();
    }
}

public sealed class BEBookendMoldRaw : BlockEntity
{
    public MoldDesign Design { get; private set; }

    public void SetDesign(MoldDesign design)
    {
        Design = design;
        MarkDirty(true);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        Design?.WriteTo(tree.GetOrAddTreeAttribute(MoldDesign.Key));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Design = MoldDesign.From(tree.GetTreeAttribute(MoldDesign.Key));
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        PairMold.Info(dsc, Design, true);
    }
}

/// <summary>
/// The fired pair mold: the game's own tool mold (pour from a crucible, wait for it to cool,
/// right-click to take them out), with the design kept and the PAIR given in the poured metal.
/// </summary>
public sealed class BlockBookendMoldFired : BlockToolMold
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        // The game's pick-up of an EMPTY mold makes a fresh stack and would lose the design
        // (vssurvivalmod BEToolMold.OnPlayerInteract, not overridable) - so take that case here.
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEBookendMoldFired be
            && !byPlayer.Entity.Controls.ShiftKey && be.FillLevel == 0 && !be.Shattered
            && byPlayer.Entity.Controls.HandUse == EnumHandInteract.None
            && (byPlayer.InventoryManager.ActiveHotbarSlot.Empty || byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Collectible is BlockToolMold))
        {
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;
            if (world.Side == EnumAppSide.Server)
            {
                PairMold.Give(world, byPlayer, PairMold.StackWith(this, be.Design), blockSel.Position);
                world.BlockAccessor.SetBlock(0, blockSel.Position);
                if (Sounds?.Place != null) world.PlaySoundAt(Sounds.Place, blockSel.Position, -0.5, byPlayer);
            }
            return true;
        }
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        bool placed = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);
        if (placed && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEBookendMoldFired be) be.SetDesign(MoldDesign.Of(byItemStack));
        return placed;
    }

    /// <summary>The mold with its design, and the pair if it has hardened in it.</summary>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BEBookendMoldFired be) return [new ItemStack(this)];
        var drops = new List<ItemStack>();
        if (!be.Shattered) drops.Add(PairMold.StackWith(this, be.Design));
        drops.AddRange(be.CastPair());
        return drops.ToArray();
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) =>
        PairMold.StackWith(this, (world.BlockAccessor.GetBlockEntity(pos) as BEBookendMoldFired)?.Design);

    public override string GetHeldItemName(ItemStack itemStack) => PairMold.Name(this, MoldDesign.Of(itemStack));

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        PairMold.Name(this, (world.BlockAccessor.GetBlockEntity(pos) as BEBookendMoldFired)?.Design);

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        PairMold.Info(dsc, MoldDesign.Of(inSlot.Itemstack), false);
        // the metals are on its handbook page (Calm, 2026-10-16: tooltips do not need to be long winded)
    }

    /// <summary>The metals this mold takes: every material marked <c>castable</c> in the materials files.</summary>
    public static string[] CastableMetals(Block block) => BookendDesigns.Castable().Select(m => m.Code).ToArray();
}

/// <summary>
/// The game's tool mold block entity with the design added. The crucible talks to it through
/// ILiquidMetalSink (vssurvivalmod BlockSmeltedContainer), so re-implementing that interface here
/// lets CanReceive refuse metals a bookend is not cast in - the game's own CanReceive is not
/// virtual. TryTakeContents IS virtual, and is where the pair is handed out.
/// </summary>
public sealed class BEBookendMoldFired : BlockEntityToolMold, ILiquidMetalSink
{
    public MoldDesign Design { get; private set; }

    public void SetDesign(MoldDesign design)
    {
        Design = design;
        MarkDirty(true);
    }

    /// <summary>The metal in it: "copper" from ingot-copper.</summary>
    private string Metal => MetalContent?.Collectible.Variant["metal"] ?? MetalContent?.Collectible.LastCodePart();

    // The game's own check (vssurvivalmod BlockEntityToolMold.CanReceive) with our metals list in place of its
    // GetMoldedStacks: that reads the blocktype's "drop", which is GONE - the handbook built a "Can be used to mold" row
    // from it, the same design-less item over and over (Calm's screenshot, 2026-10-16). The metals are on the page instead.
    public new bool CanReceive(ItemStack metal)
    {
        if (Design is not { Complete: true } || metal == null || Shattered) return false;
        string code = metal.Collectible.Variant["metal"] ?? metal.Collectible.LastCodePart();
        if (!BlockBookendMoldFired.CastableMetals(Block).Contains(code)) return false;
        return MetalContent == null || (MetalContent.Collectible.Equals(MetalContent, metal, GlobalConstants.IgnoredStackAttributes) && !IsFull);
    }

    /// <summary>The finished left and right, if the mold is full and the metal hard; empty otherwise.</summary>
    public List<ItemStack> CastPair()
    {
        if (Design is not { Complete: true } || Shattered || MetalContent == null || !IsFull || !IsHardened) return [];
        return BookendDesigns.Sides.Select(side => BookendDesigns.Make(Api.World, Design.Design, side, Metal)).Where(s => s != null).ToList();
    }

    protected override bool TryTakeContents(IPlayer byPlayer)
    {
        if (Shattered || MetalContent == null || FillLevel == 0) return false;
        if (!IsFull || !IsHardened) return false;

        // BOTH BOOKENDS OR NOTHING (2026-10-10): with its design or metal gone (a mod changed or removed since it was
        // poured) the pair cannot be made - the metal used to be emptied out anyway and lost. Now it stays in the mold.
        var pair = CastPair();
        if (pair.Count != BookendDesigns.Sides.Length)
        {
            (Api as ICoreClientAPI)?.TriggerIngameError(this, "cannotcast", Lang.Get("bookends:mold-cannot-cast"));
            return false;
        }

        Api.World.PlaySoundAt(new AssetLocation("sounds/block/ingot"), Pos, -0.5, byPlayer, false);
        if (Api.Side == EnumAppSide.Server)
        {
            foreach (var bookend in pair) PairMold.Give(Api.World, byPlayer, bookend, Pos);
            MetalContent = null;
            FillLevel = 0;
            MarkDirty(true);
        }
        UpdateRenderer();
        return true;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        Design?.WriteTo(tree.GetOrAddTreeAttribute(MoldDesign.Key));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolve)
    {
        Design = MoldDesign.From(tree.GetTreeAttribute(MoldDesign.Key));
        base.FromTreeAttributes(tree, worldForResolve);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        PairMold.Info(dsc, Design, false);
        // the game's shattered line reads the molded stacks from "drop", which these molds no longer have
        if (Shattered) { dsc.AppendLine(Lang.Get("bookends:mold-shattered")); return; }
        base.GetBlockInfo(forPlayer, dsc);
    }
}
