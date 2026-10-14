# Bookends - guide for modders

The full modder reference for Bookends. Questions and reports: the Bookends page on mods.vintagestory.at.

## For Modders

Add your own designs and metals with JSON - no code, no item codes, and no dependency entry.

### Quick Start

**1.** **Make the two models**

A left and a right bookend shape, in your own mod. Texture them however you like - every face is painted with the chosen material's texture.

**2.** **List the design**

Make a file at `assets/<yourmod>/config/bookends/designs/<anything>.json` - any file name will do - and copy the example below into it, with your design's code and your two shapes.

**3.** **Load a world**

Your design is in the carving window at the bookend blank, in the creative tab and in the molds, in every material. If something in your file is wrong, the log names the file and the problem (`[Bookends] Design ... skipped: ...`).

**Example** - that is all a design needs:

```json
[
  {
    "code": "owl",
    "left": "yourmod:item/bookend/owl-left",
    "right": "yourmod:item/bookend/owl-right"
  }
]
```

**Nothing else to make.** Every bookend is one item, so your design needs no item, recipe or creative entry of its own - carving, casting and the creative tab all follow from the file.

### Field Reference

| **Required** |  |
|---|---|
| `code` | Your design's id. It is saved on every bookend, so **never change it after release.** Codes are shared by every mod, so pick one that will not clash. |
| `left` | The left bookend's shape, domain included. |
| `right` | The right bookend's shape, domain included. |
| **Optional** |  |
| `enabled` | False hides a design. |

- The name comes from the lang key `bookends:design-<code>`. Put it in your own mod's lang file with the domain written in - `"bookends:design-owl": "Owl"` - or leave it out and the code is shown with a capital letter (`owl` is "Owl").

- **A later file with the same code replaces the earlier one.** Bookends' own files load first, then every other mod's in order, so a file of yours can change one of our designs - or turn it off with `"enabled": false` - without a patch.

### Making The Models

- Each side is its own shape: the left and right halves of the pair, as they stand on a shelf.

- Every face is painted with the material's texture - wood, copper, gold and so on - so what you put in the shape's textures is only for Model Creator. The texture is laid at the same density on every bookend, so keep your model at the size of ours.

- Look at Bookends' own shapes in `bookends:shapes/item/bookend/` for the size and where they stand.

### Adding A Metal

Materials live in `config/bookends/materials.json`. Each has:

| `code` | The material's code, saved on every bookend. For a castable metal it must be the metal's own, as in its ingot (`ingot-<code>`). |
|---|---|
| `texture` | The texture painted on every face. |
| `castable` | True lets the fired mold take this metal. |

The name comes from `bookends:material-<code>`, else the game's own `material-<code>` (every vanilla metal has one), else the code. Add a material with a patch onto ours, or ship a file at the same path in your own mod - a later file with the same code changes that material. This is the pewter patch Bookends ships:

```json
[
  {
    "op": "addeach",
    "path": "/-",
    "file": "bookends:config/bookends/materials.json",
    "dependsOn": [ { "modid": "pewter" } ],
    "value": [
      { "code": "pewter", "texture": "game:item/resource/plate/pewter", "castable": true }
    ]
  }
]
```

### Shipping It With Your Mod

**Do not add Bookends to your dependencies.** Ship the design file unconditionally. The game never reads a config file nothing asks for, so for anyone who does not have Bookends your file is inert and silent - no warnings, no errors. A dependency entry would do the opposite and stop your mod loading without it.

**Patches onto Bookends' files are the exception**: they name Bookends' own files, so give each one `"dependsOn": [ { "modid": "bookends" } ]`. The patch then only applies when Bookends is installed, and is skipped silently when it is not.
