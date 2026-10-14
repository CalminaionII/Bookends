using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Bookends;

/// <summary>
/// Turns a bookend into a mesh: the design's own left or right shape, every face painted with the
/// material's texture - what Attribute Rendering Library did before (Calm, 2026-10-08: one item code,
/// ARL dropped), done the way Figurines does it. The shape is used as it is, so the item's transforms
/// in bookend.json (hand, inventory, ground, shelf, window) stay exactly as tuned.
///
/// The material texture has to be in the block atlas; inserting is GPU work, main thread only. Every
/// material texture is put there at LevelFinalize (BookendsModSystem), so builds normally happen on
/// any thread - the one a display asks from.
/// </summary>
public static class BookendMesher
{
    private const string MeshCacheKey = "bookends-meshes";
    private const string RefCacheKey = "bookends-item-refs";

    private static ConcurrentDictionary<string, MeshData> Meshes(ICoreClientAPI capi) =>
        ObjectCacheUtil.GetOrCreate(capi, MeshCacheKey, () => new ConcurrentDictionary<string, MeshData>());

    /// <summary>True once this bookend has been built, or has failed to build - either way, nothing left to wait for.</summary>
    public static bool IsDone(ICoreClientAPI capi, BookendData data) => Meshes(capi).ContainsKey(data.MeshKey);

    public static MeshData TryGet(ICoreClientAPI capi, BookendData data) =>
        Meshes(capi).TryGetValue(data.MeshKey, out var m) ? m : null;

    /// <summary>
    /// Any thread. Off the main thread it can only use a texture already in the block atlas; if it is
    /// not, it returns null WITHOUT caching, and the caller queues a main-thread build.
    /// </summary>
    public static MeshData Get(ICoreClientAPI capi, BookendData data)
    {
        var cache = Meshes(capi);
        if (cache.TryGetValue(data.MeshKey, out var cached)) return cached;
        MeshData mesh = null;
        try { mesh = Build(capi, data); }
        catch (NotReadyException) { return null; }
        catch (Exception e) { capi.Logger.Error("[Bookends] Cannot build the bookend {0}: {1}", data.MeshKey, e); }
        if (mesh is { VerticesCount: 0 }) mesh = null;
        cache[data.MeshKey] = mesh;
        return mesh;
    }

    /// <summary>The same bookend uploaded for inventory, hand and dropped item.</summary>
    public static MultiTextureMeshRef GetItemRef(ICoreClientAPI capi, BookendData data)
    {
        var refs = ObjectCacheUtil.GetOrCreate(capi, RefCacheKey, () => new Dictionary<string, MultiTextureMeshRef>());
        if (refs.TryGetValue(data.MeshKey, out var cached)) return cached;
        var source = Get(capi, data);
        if (source == null) return null;
        var meshRef = capi.Render.UploadMultiTextureMesh(source);
        refs[data.MeshKey] = meshRef;
        return meshRef;
    }

    public static void Clear(ICoreClientAPI capi)
    {
        var refs = ObjectCacheUtil.TryGet<Dictionary<string, MultiTextureMeshRef>>(capi, RefCacheKey);
        if (refs != null)
        {
            foreach (var r in refs.Values) r?.Dispose();
            refs.Clear();
        }
        ObjectCacheUtil.TryGet<ConcurrentDictionary<string, MeshData>>(capi, MeshCacheKey)?.Clear();
        Warned.Clear();
    }

    /// <summary>Main thread: put these textures in the block atlas now, so later builds need no insert.</summary>
    public static void PrepareTextures(ICoreClientAPI capi, IEnumerable<string> textures)
    {
        foreach (var texture in textures.Distinct()) _ = new MaterialSource(capi, texture);
    }

    /// <summary>Things already reported this session - a display asks again every frame.</summary>
    private static readonly ConcurrentDictionary<string, bool> Warned = new();

    private static void WarnOnce(ICoreClientAPI capi, string key, string format, params object[] args)
    {
        if (Warned.TryAdd(key, true)) capi.Logger.Warning("[Bookends] " + format, args);
    }

    private static MeshData Build(ICoreClientAPI capi, BookendData data)
    {
        var design = BookendDesigns.Design(data.Design);
        if (design == null) { WarnOnce(capi, "design|" + data.Design, "No design '{0}' - is the mod that adds it installed?", data.Design); return null; }
        var material = BookendDesigns.Material(data.Material) ?? BookendDesigns.Material("wood");
        if (material == null) { WarnOnce(capi, "material|" + data.Material, "No material '{0}' and no wood to fall back on", data.Material); return null; }

        var location = new AssetLocation(design.Shape(data.Side));
        var shape = Shape.TryGet(capi, location.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
        if (shape == null) { WarnOnce(capi, "shape|" + location, "No shape {0} (design {1}, {2})", location, data.Design, data.Side); return null; }

        capi.Tesselator.TesselateShape("bookends:" + data.MeshKey, shape, out var mesh, new MaterialSource(capi, material.Texture));
        return mesh;
    }

    /// <summary>Thrown off the main thread when a texture is not in the atlas yet - build on the main thread instead.</summary>
    private sealed class NotReadyException : Exception { }

    /// <summary>Every texture code the shape asks for gets the material's one texture.</summary>
    private sealed class MaterialSource : ITexPositionSource
    {
        private readonly ICoreClientAPI capi;
        private readonly TextureAtlasPosition position;

        public Size2i AtlasSize => capi.BlockTextureAtlas.Size;

        public MaterialSource(ICoreClientAPI capi, string texture)
        {
            this.capi = capi;
            var loc = new AssetLocation(texture);
            // Already in the atlas: usable from any thread.
            position = capi.BlockTextureAtlas[loc]
                ?? capi.BlockTextureAtlas[loc.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png")];
            if (position != null) return;
            // Inserting is GPU work - main thread only.
            if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId) throw new NotReadyException();
            capi.BlockTextureAtlas.GetOrInsertTexture(new CompositeTexture(loc), out _, out position);
            if (position == null) WarnOnce(capi, "texture|" + texture, "Texture {0} could not be put in the block atlas", texture);
        }

        public TextureAtlasPosition this[string textureCode] => position ?? capi.BlockTextureAtlas.UnknownTexturePosition;
    }
}
