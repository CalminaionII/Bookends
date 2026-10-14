using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Bookends;

/// <summary>
/// "Bookend Carving" - Figurines' carving window (Calm's layout of 2026-10-08), for cohesion: a
/// search box, filter tabs (All, then one per mod whose designs are in - Bookends' own first), a
/// page of cards each showing the wooden bookend and its name, page arrows, and underneath the
/// selected design large - its LEFT and RIGHT side by side, as the blank carves both - and a
/// "Select design" button. The pick goes
/// to the server as a block entity packet; it is checked there.
///
/// The window is rebuilt on every filter / page / selection change; the search text is put back and
/// the search box takes focus again, so typing is not broken.
/// </summary>
public sealed class GuiDialogCarve : GuiDialogGeneric
{
    private const int Cols = 4, Rows = 3, PerPage = Cols * Rows;
    private const double CardW = 205, CardH = 100, Gap = 6, IconSize = 68;

    private readonly BlockPos pos;
    private readonly List<(string design, string name, string domain, ItemStack stack)> all = [];
    /// <summary>The filter tabs after All: one per mod whose designs are in - (domain, the mod's name).</summary>
    private readonly List<(string domain, string name)> modTabs;
    private string search = "";
    private int filter, page;
    private string selected;
    private bool composing;

    public override string ToggleKeyCombinationCode => null;

    public GuiDialogCarve(ICoreClientAPI capi, BlockPos pos) : base(Lang.Get("bookends:carve-title"), capi)
    {
        this.pos = pos.Copy();
        foreach (var design in BookendDesigns.All(capi.World))
        {
            var stack = BookendDesigns.Make(capi.World, design, "left", "wood");
            // Which mod a design belongs to: where its shape comes from (a design file's own mod).
            string domain = new AssetLocation(BookendDesigns.Design(design).Left).Domain;
            if (stack != null) all.Add((design, BookendDesigns.DesignName(design), domain, stack));
        }
        modTabs = all.Select(c => c.domain).Distinct()
            .Select(d => (d, capi.ModLoader.GetMod(d)?.Info?.Name ?? Capitalise(d)))
            .OrderBy(t => t.d == "bookends" ? 0 : 1).ThenBy(t => t.Item2).ToList();
        selected = all.FirstOrDefault().design;
        Compose();
    }

    private List<(string design, string name, string domain, ItemStack stack)> Shown() => all.Where(c =>
        (filter == 0 || c.domain == modTabs[filter - 1].domain)
        && (search.Length == 0 || c.name.Contains(search, StringComparison.OrdinalIgnoreCase) || c.design.Contains(search, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    private void Compose()
    {
        composing = true;
        var shown = Shown();
        int pages = Math.Max(1, (int)Math.Ceiling(shown.Count / (double)PerPage));
        page = GameMath.Clamp(page, 0, pages - 1);
        double width = Cols * CardW + (Cols - 1) * Gap;

        var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bg.BothSizing = ElementSizing.FitToChildren;
        var c = capi.Gui.CreateCompo("bookends-carve", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(bg)
            .AddDialogTitleBar(DialogTitle, () => TryClose())
            .BeginChildElements(bg);

        // Search, then the filter tabs.
        c.AddTextInput(ElementBounds.Fixed(0, 35, width, 30), OnSearch, CairoFont.WhiteSmallText(), "search");
        var names = new[] { Lang.Get("bookends:carve-tab-all") }.Concat(modTabs.Select(m => m.name));
        var tabs = names.Select((t, i) => new GuiTab { Name = t, DataInt = i, Active = i == filter }).ToArray();
        c.AddHorizontalTabs(tabs, ElementBounds.Fixed(0, 75, width, 25), i => Later(() => { filter = i; page = 0; }), CairoFont.WhiteSmallText(), CairoFont.WhiteSmallText().WithColor(GuiStyle.ActiveButtonTextColor), "filters");

        // The cards.
        double top = 110;
        var onPage = shown.Skip(page * PerPage).Take(PerPage).ToList();
        if (onPage.Count == 0)
            c.AddStaticText(Lang.Get("bookends:carve-nothing"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, top + 10, width, 30));
        for (int i = 0; i < onPage.Count; i++)
        {
            var card = onPage[i];
            double x = i % Cols * (CardW + Gap), y = top + i / Cols * (CardH + Gap);
            string design = card.design;
            c.AddInset(ElementBounds.Fixed(x, y, CardW, CardH), 2, design == selected ? 0.95f : 0.6f);
            c.AddRichtext([new ItemstackTextComponent(capi, card.stack, IconSize, 0, EnumFloat.Left, _ => Later(() => selected = design))],
                ElementBounds.Fixed(x + (CardW - IconSize) / 2, y + 2, IconSize + 4, IconSize + 2));
            c.AddSmallButton(card.name, () => { Later(() => selected = design); return true; }, ElementBounds.Fixed(x + 4, y + CardH - 26, CardW - 8, 22));
        }

        // Page arrows.
        double pager = top + Rows * (CardH + Gap) + 4;
        c.AddSmallButton("<", () => { Later(() => page--); return true; }, ElementBounds.Fixed(width / 2 - 90, pager, 30, 24));
        c.AddStaticText(Lang.Get("bookends:carve-page", page + 1, pages), CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(width / 2 - 55, pager + 3, 110, 24));
        c.AddSmallButton(">", () => { Later(() => page++); return true; }, ElementBounds.Fixed(width / 2 + 60, pager, 30, 24));

        // The selected design, large: its left and right side by side; the button that confirms it.
        double details = pager + 40;
        var sel = all.FirstOrDefault(x => x.design == selected);
        c.AddInset(ElementBounds.Fixed(0, details, 220, 110), 3, 0.7f);
        if (sel.design != null)
        {
            for (int i = 0; i < BookendDesigns.Sides.Length; i++)
            {
                var preview = BookendDesigns.Make(capi.World, sel.design, BookendDesigns.Sides[i], "wood");
                if (preview != null) c.AddRichtext([new ItemstackTextComponent(capi, preview, 100)], ElementBounds.Fixed(5 + i * 108, details + 5, 104, 104));
            }
            c.AddStaticText(Lang.Get("bookends:carve-selected", sel.name), CairoFont.WhiteSmallishText(), ElementBounds.Fixed(235, details + 2, width - 235, 30));
            c.AddStaticText(Lang.Get("bookends:carve-material"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(235, details + 32, width - 235, 24));
            c.AddSmallButton(Lang.Get("bookends:carve-select"), OnConfirm, ElementBounds.Fixed(width - 150, details + 86, 150, 28));
        }

        SingleComposer = c.EndChildElements().Compose();
        var input = SingleComposer.GetTextInput("search");
        input.SetPlaceHolderText(Lang.Get("bookends:carve-search"));
        input.SetValue(search);
        SingleComposer.GetHorizontalTabs("filters").SetValue(filter, false);
        composing = false;
    }

    private void OnSearch(string text)
    {
        if (composing || text == search) return;
        search = text ?? "";
        Later(() => page = 0);
    }

    /// <summary>Change something, then rebuild - after the current click or keypress has finished, never inside the old window's handler.</summary>
    private void Later(Action change)
    {
        capi.Event.EnqueueMainThreadTask(() =>
        {
            change();
            if (IsOpened()) Compose();
        }, "bookends-carve");
    }

    private bool OnConfirm()
    {
        if (selected == null) return true;
        capi.Network.SendBlockEntityPacket(pos, BEBookendBlank.ChoosePacket, Encoding.UTF8.GetBytes(selected));
        TryClose();
        return true;
    }

    private static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
