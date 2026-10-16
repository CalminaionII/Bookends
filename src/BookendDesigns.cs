using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Bookends;

/// <summary>
/// One bookend design from config/bookends/designs/*.json: its code and its two shapes. ADDING A
/// DESIGN TAKES ONE FILE (Calm, 2026-10-08) - any mod ships
/// <c>assets/&lt;its domain&gt;/config/bookends/designs/&lt;anything&gt;.json</c>:
/// <code>[ { "code": "owl", "left": "mymod:item/owl-left", "right": "mymod:item/owl-right" } ]</code>
/// and it is carvable, castable and in the creative tab - no item codes, no patches.
/// </summary>
public sealed class BookendDesignDefinition
{
    public string Code { get; set; }
    public string Left { get; set; }
    public string Right { get; set; }

    /// <summary>Set to false (in a later file, same code) to take a design out.</summary>
    public bool Enabled { get; set; } = true;

    public string Shape(string side) => side == "left" ? Left : Right;
}

/// <summary>
/// One material from config/bookends/materials/*.json: what the bookend is painted with, and whether
/// the fired mold takes it (Calm, 2026-10-08: other materials should be easy to add). Figurines uses
/// the same layout. The code of a castable metal must be the metal's own - the crucible's
/// ingot-&lt;code&gt;. A file loaded later may change an entry by repeating its code.
/// </summary>
public sealed class BookendMaterialDefinition
{
    public string Code { get; set; }
    public string Texture { get; set; }
    public bool Castable { get; set; }

    /// <summary>
    /// The mod the material comes from, when its texture is in the game's domain but the game does not have it (Pewter
    /// ships game:item/resource/plate/pewter). Left out = the texture's domain. Decides the creative tab (one stack per
    /// mod) and the handbook's "Can be made with" line.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// true = on the handbook pages but not in the creative tab - the game's less common pourable metals (Calm, 2026-10-16:
    /// "any metal that can be molten"), so the tab keeps its size. Casting is the same either way.
    /// </summary>
    public bool HandbookOnly { get; set; }

    /// <summary>The mod id this material counts as: <see cref="Source"/>, else the texture's domain.</summary>
    public string SourceMod => !string.IsNullOrEmpty(Source) ? Source
        : Texture != null && Texture.Contains(':') ? Texture.Substring(0, Texture.IndexOf(':')) : "game";
}

/// <summary>
/// A bookend's identity, as one <c>bookend</c> tree attribute on the stack - design, side, material -
/// on the ONE item <c>bookends:bookend</c> (Calm, 2026-10-08: "so they are all one code and any new
/// ones will get the one code"). Everything the client needs to draw it.
/// </summary>
public sealed class BookendData
{
    public const string Key = "bookend";

    /// <summary>
    /// The stack's "type": what Universal Display keys its measured size on, beside the item code
    /// (vssurvivalmod IDisplayableProps - clutter's way, as Figurines). Material is left out - it does
    /// not change the shape.
    /// </summary>
    public const string TypeAttribute = "type";

    private string design, side = "left", material = "wood", meshKey;

    public string Design { get => design; set { design = value; meshKey = null; } }
    public string Side { get => side; set { side = value; meshKey = null; } }
    public string Material { get => material; set { material = value; meshKey = null; } }

    /// <summary>The mesh cache key - worked out once, not on every ask (Calm, 2026-10-09: performance, asked every frame per bookend drawn).</summary>
    public string MeshKey => meshKey ??= Design + "|" + Side + "|" + Material;
    public string DisplayType => Design + "-" + Side;

    public static BookendData Of(ItemStack stack)
    {
        if (stack?.Collectible is not ItemBookend) return null;
        var tree = stack.Attributes?.GetTreeAttribute(Key);
        string design = tree?.GetString("design");
        if (string.IsNullOrEmpty(design)) return null;
        return new BookendData { Design = design, Side = tree.GetString("side", "left"), Material = tree.GetString("material", "wood") };
    }

    public void WriteTo(ItemStack stack) => WriteTo(stack.Attributes);

    public void WriteTo(ITreeAttribute stackAttributes)
    {
        var tree = stackAttributes.GetOrAddTreeAttribute(Key);
        tree.SetString("design", Design);
        tree.SetString("side", Side);
        tree.SetString("material", Material);
        stackAttributes.SetString(TypeAttribute, DisplayType);
    }
}

/// <summary>Lookups and stack-making over the loaded designs and materials.</summary>
public static class BookendDesigns
{
    public static readonly string[] Sides = ["left", "right"];
    public static readonly AssetLocation ItemCode = new("bookends", "bookend");

    private static BookendsModSystem System => BookendsModSystem.Instance;

    /// <summary>Every design, in load order (Bookends' own first).</summary>
    public static List<string> All(IWorldAccessor world) => System?.Designs.Select(d => d.Code).ToList() ?? [];

    public static BookendDesignDefinition Design(string code) => System?.Designs.FirstOrDefault(d => d.Code == code);

    public static BookendMaterialDefinition Material(string code) =>
        code != null && System?.Materials.TryGetValue(code, out var m) == true ? m : null;

    public static IEnumerable<BookendMaterialDefinition> Castable() => System?.Materials.Values.Where(m => m.Castable) ?? [];

    /// <summary>A bookend of this design and side in this material; null if any of them is unknown.</summary>
    public static ItemStack Make(IWorldAccessor world, string design, string side, string material)
    {
        if (Design(design) == null || Material(material) == null || !Sides.Contains(side)) return null;
        var item = world.GetItem(ItemCode);
        if (item == null) return null;
        var stack = new ItemStack(item);
        new BookendData { Design = design, Side = side, Material = material }.WriteTo(stack);
        return stack;
    }

    /// <summary>The design's name - its lang line, or its code capitalised for a design file that brought none.</summary>
    public static string DesignName(string design)
    {
        string key = "bookends:design-" + design;
        if (Lang.HasTranslation(key)) return Lang.Get(key);
        return Capitalise(design);
    }

    public static string SideName(string side) => Lang.Get("bookends:side-" + side);

    /// <summary>"Wooden", "Copper"... - ours, else the game's metal name, else the code.</summary>
    public static string MaterialName(string material)
    {
        if (Lang.HasTranslation("bookends:material-" + material)) return Lang.Get("bookends:material-" + material);
        if (Lang.HasTranslation("game:material-" + material)) return Lang.Get("game:material-" + material);
        return Capitalise(material);
    }

    public static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
