using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Bookends;

/// <summary>
/// Bookends, rebuilt on Figurines' system (Calm, 2026-10-08): one item for every bookend, designs and
/// materials as data files, carved in wood from a blank, cast in metal from a pair mold. A full break
/// from the old trader / box versions.
/// </summary>
public sealed class BookendsModSystem : ModSystem
{
    public IReadOnlyList<BookendDesignDefinition> Designs { get; private set; } = [];
    public IReadOnlyDictionary<string, BookendMaterialDefinition> Materials { get; private set; } = new Dictionary<string, BookendMaterialDefinition>();

    /// <summary>For static helpers (names, stack-making) that have no api to hand. Both sides load the same files.</summary>
    public static BookendsModSystem Instance { get; private set; }

    public override void Start(ICoreAPI api)
    {
        Instance = this;
        api.RegisterItemClass("BookendsBookend", typeof(ItemBookend));
        api.RegisterBlockClass("BookendsBlank", typeof(BlockBookendBlank));
        api.RegisterBlockEntityClass("BookendsBlank", typeof(BEBookendBlank));
        api.RegisterBlockClass("BookendsMoldRaw", typeof(BlockBookendMoldRaw));
        api.RegisterBlockEntityClass("BookendsMoldRaw", typeof(BEBookendMoldRaw));
        api.RegisterBlockClass("BookendsMoldFired", typeof(BlockBookendMoldFired));
        api.RegisterBlockEntityClass("BookendsMoldFired", typeof(BEBookendMoldFired));
    }

    public override void StartClientSide(ICoreClientAPI capi)
    {
        // Atlas positions and uploaded meshes belong to the world being left.
        capi.Event.LeaveWorld += () => { BookendMesher.Clear(capi); ItemBookend.ClearPending(); };
        // The material textures go into the block atlas once - textures only, no models - so every
        // bookend can be built on any thread the first time a display asks for it.
        capi.Event.LevelFinalize += () => BookendMesher.PrepareTextures(capi, Materials.Values.Select(m => m.Texture));
    }

    /// <summary>
    /// Definitions come from assets, so this cannot happen in Start() - assets are not there yet and
    /// GetMany returns nothing, silently. Files load in path order; a later file may change or
    /// disable an entry by repeating its code. The counts are logged on purpose.
    /// </summary>
    public override void AssetsLoaded(ICoreAPI api)
    {
        var designs = new Dictionary<string, BookendDesignDefinition>();
        var order = new List<string>();
        foreach (var asset in api.Assets.GetMany("config/bookends/designs/").OrderBy(a => a.Location.Domain == "bookends" ? 0 : 1).ThenBy(a => a.Location.ToString()))
        {
            try
            {
                foreach (var def in asset.ToObject<BookendDesignDefinition[]>() ?? [])
                {
                    if (string.IsNullOrWhiteSpace(def.Code) || string.IsNullOrWhiteSpace(def.Left) || string.IsNullOrWhiteSpace(def.Right))
                    {
                        api.Logger.Error("[Bookends] Design '{0}' in {1} skipped: it needs a code, a left and a right shape", def.Code, asset.Location);
                        continue;
                    }
                    if (!designs.ContainsKey(def.Code)) order.Add(def.Code);
                    designs[def.Code] = def;
                }
            }
            catch (Exception e) { api.Logger.Error("[Bookends] Cannot read {0}: {1}", asset.Location, e.Message); }
        }
        Designs = order.Select(c => designs[c]).Where(d => d.Enabled).ToArray();

        var materials = new Dictionary<string, BookendMaterialDefinition>();
        foreach (var asset in api.Assets.GetMany("config/bookends/materials").OrderBy(a => a.Location.Domain == "bookends" ? 0 : 1).ThenBy(a => a.Location.ToString()))
        {
            try
            {
                foreach (var m in asset.ToObject<BookendMaterialDefinition[]>() ?? [])
                    if (!string.IsNullOrWhiteSpace(m.Code) && !string.IsNullOrWhiteSpace(m.Texture)) materials[m.Code] = m;
            }
            catch (Exception e) { api.Logger.Error("[Bookends] Cannot read {0}: {1}", asset.Location, e.Message); }
        }
        Materials = materials;

        api.Logger.Event("[Bookends] {0} design(s), {1} material(s) loaded", Designs.Count, Materials.Count);
    }

    /// <summary>
    /// The creative tab, built from the design and material files - so a design or metal another mod
    /// adds shows up with no itemtype or patch of its own. Per design: the pair in wood, then in each
    /// castable metal.
    /// </summary>
    public override void AssetsFinalize(ICoreAPI api)
    {
        var item = api.World.GetItem(BookendDesigns.ItemCode);
        if (item == null) return;
        var materials = Materials.Values.Where(m => m.Code == "wood").Concat(Materials.Values.Where(m => m.Castable)).ToList();
        var stacks = new List<JsonItemStack>();
        foreach (var design in Designs)
            foreach (var material in materials)
                foreach (var side in BookendDesigns.Sides)
                    stacks.Add(Stack(item, new BookendData { Design = design.Code, Side = side, Material = material.Code }));
        item.CreativeInventoryStacks = [new CreativeTabAndStackList { Tabs = ["general", "decorative", "bookends"], Stacks = stacks.ToArray() }];
    }

    private static JsonItemStack Stack(Item item, BookendData data)
    {
        var tree = new TreeAttribute();
        data.WriteTo(tree);
        var js = new JsonItemStack { Type = EnumItemClass.Item, Code = item.Code, StackSize = 1, Attributes = new JsonObject(JToken.Parse(tree.ToJsonToken())) };
        js.ResolvedItemstack = new ItemStack(item);
        data.WriteTo(js.ResolvedItemstack);
        return js;
    }
}
