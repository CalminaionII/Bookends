using System.Collections.Concurrent;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Bookends;

/// <summary>
/// THE bookend - one item, <c>bookends:bookend</c>, for every design, side and material (Calm,
/// 2026-10-08: "so they are all one code and any new ones will get the one code"); the stack's
/// <c>bookend</c> tree says which (BookendData). Its model comes from BookendMesher - the design's
/// shape painted in the material - for the inventory, the hand, the ground, shelves, display cases
/// and Universal Display. Figurines' ItemFigurine, for bookends.
/// </summary>
public sealed class ItemBookend : Item, IContainedMeshSource, IDisplayableProps
{
    public override string GetHeldItemName(ItemStack itemStack)
    {
        var data = BookendData.Of(itemStack);
        if (data == null) return base.GetHeldItemName(itemStack);
        return Lang.Get("bookends:bookend-name", BookendDesigns.MaterialName(data.Material), BookendDesigns.DesignName(data.Design), BookendDesigns.SideName(data.Side));
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        var data = BookendData.Of(inSlot.Itemstack);
        if (data == null) return;
        // The design's own line (Calm's flavour text), per side where the two differ.
        foreach (var key in new[] { "bookends:designdesc-" + data.Design + "-" + data.Side, "bookends:designdesc-" + data.Design })
        {
            if (!Lang.HasTranslation(key)) continue;
            dsc.AppendLine(Lang.Get(key));
            break;
        }
        dsc.AppendLine(data.Material == "wood"
            ? Lang.Get("bookends:bookend-carved")
            : Lang.Get("bookends:bookend-cast", BookendDesigns.MaterialName(data.Material)));
    }

    /// <summary>
    /// The bookend each stack's attributes describe, read once per attributes tree (Calm, 2026-10-09: performance - this
    /// runs every frame for every bookend drawn). A changed stack gets a new attributes tree, so it is read again.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Vintagestory.API.Datastructures.ITreeAttribute, BookendData> drawn = new();

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        var attributes = itemstack?.Attributes;
        BookendData data = null;
        if (attributes != null && !drawn.TryGetValue(attributes, out data))
        {
            data = BookendData.Of(itemstack);
            if (data != null) drawn.AddOrUpdate(attributes, data);
        }
        if (data != null && BookendMesher.GetItemRef(capi, data) is { } meshRef) renderinfo.ModelRef = meshRef;
        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
    }

    /// <summary>
    /// Ground storage, display cases, shelves and Universal Display ask here, often from the
    /// TESSELATION thread. Built here and now when the texture is already in the atlas (normally - every
    /// material texture is put there at load) so a display never sees a stand-in; Universal Display
    /// measures the first mesh it gets. Otherwise: queue a main-thread build and a redraw.
    /// </summary>
    public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        if (api is not ICoreClientAPI capi) return null;
        var data = BookendData.Of(slot.Itemstack);
        if (data == null) return null;

        BookendMesher.Get(capi, data);
        if (BookendMesher.IsDone(capi, data)) return BookendMesher.TryGet(capi, data)?.Clone();

        // ONE QUEUED BUILD PER MODEL (2026-10-10): every display asking meanwhile only adds itself to the list, and all
        // of them are redrawn when it is done (each ask used to queue its own build + redraw).
        var waiting = Pending.GetOrAdd(data.MeshKey, _ => new ConcurrentDictionary<BlockPos, byte>());
        if (atBlockPos != null) waiting[atBlockPos.Copy()] = 0;
        if (Queued.TryAdd(data.MeshKey, 0))
        {
            string key = data.MeshKey;
            capi.Event.EnqueueMainThreadTask(() =>
            {
                BookendMesher.Get(capi, data);
                Queued.TryRemove(key, out _);
                if (Pending.TryRemove(key, out var redraw))
                    foreach (var pos in redraw.Keys) Redraw(capi, pos);
            }, "bookends-mesh");
        }
        return null;
    }

    /// <summary>Models with a build queued, and the displays waiting on each - per session (Clear on world start).</summary>
    private static readonly ConcurrentDictionary<string, byte> Queued = new();
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<BlockPos, byte>> Pending = new();

    public static void ClearPending()
    {
        Queued.Clear();
        Pending.Clear();
    }

    /// <summary>
    /// The display caches its mesh under this key. While the mesh is unbuilt the key ends in "|wait",
    /// so the stand-in is cached under that and the real mesh is asked for again once it exists.
    /// </summary>
    public string GetMeshCacheKey(ItemSlot slot)
    {
        var data = BookendData.Of(slot.Itemstack);
        string key = Code + "|" + (data?.MeshKey ?? "");
        if (data == null || api is not ICoreClientAPI capi) return key;
        return BookendMesher.IsDone(capi, data) ? key : key + "|wait";
    }

    private static void Redraw(ICoreClientAPI capi, BlockPos pos)
    {
        if (pos == null) return;
        if (capi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityDisplay display) display.updateMeshes();
        capi.World.BlockAccessor.MarkBlockDirty(pos);
    }

    /// <summary>
    /// Nothing of its own - the "displayable" attributes still answer. Implemented only because
    /// Universal Display reads the stack's <c>type</c> (BookendData.DisplayType) when the item is
    /// IDisplayableProps, which gives every design + side its own measured footprint.
    /// </summary>
    public DisplayableAttributes GetDisplayableProps(ItemSlot inSlot, string displayType) => null;
}
