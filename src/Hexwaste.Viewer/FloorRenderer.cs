using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Hexwaste.Viewer;

/// <summary>
/// Per-vertex lit floor pass. The engine renders each floor square as a
/// 10-vertex fan with corner intensities interpolated across spans in
/// software (tile.cc:147-176, 1598-1697, with a flat fast path when all
/// corners agree — which is what the old SpriteBatch tint was). Here the GPU
/// interpolates instead: one quad per square, corner colors from the light
/// grid, batched per floor texture through a BasicEffect.
/// </summary>
public sealed class FloorRenderer(GraphicsDevice device)
{
    private BasicEffect? _effect;
    private readonly Dictionary<Texture2D, List<VertexPositionColorTexture>> _batches = [];

    public void Begin(int viewportWidth, int viewportHeight, Matrix? world = null)
    {
        _effect ??= new BasicEffect(device)
        {
            TextureEnabled = true,
            VertexColorEnabled = true,
            LightingEnabled = false,
        };
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0, viewportWidth, viewportHeight, 0, 0, 1);
        _effect.View = Matrix.Identity;
        // P85: the world-layer zoom transform (identity at 1×) — the floor quads scale with the sprites.
        _effect.World = world ?? Matrix.Identity;

        foreach (List<VertexPositionColorTexture> list in _batches.Values)
            list.Clear();
    }

    public void Add(Texture2D texture, int x, int y,
        Color topLeft, Color topRight, Color bottomLeft, Color bottomRight)
    {
        if (!_batches.TryGetValue(texture, out List<VertexPositionColorTexture>? list))
        {
            list = [];
            _batches[texture] = list;
        }

        int w = texture.Width;
        int h = texture.Height;
        var v0 = new VertexPositionColorTexture(new Vector3(x, y, 0), topLeft, new Vector2(0, 0));
        var v1 = new VertexPositionColorTexture(new Vector3(x + w, y, 0), topRight, new Vector2(1, 0));
        var v2 = new VertexPositionColorTexture(new Vector3(x, y + h, 0), bottomLeft, new Vector2(0, 1));
        var v3 = new VertexPositionColorTexture(new Vector3(x + w, y + h, 0), bottomRight, new Vector2(1, 1));
        list.Add(v0);
        list.Add(v1);
        list.Add(v2);
        list.Add(v1);
        list.Add(v3);
        list.Add(v2);
    }

    public void End()
    {
        device.BlendState = BlendState.AlphaBlend;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        device.SamplerStates[0] = SamplerState.PointClamp;

        foreach ((Texture2D texture, List<VertexPositionColorTexture> vertices) in _batches)
        {
            if (vertices.Count == 0)
                continue;
            _effect!.Texture = texture;
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList,
                    vertices.ToArray(), 0, vertices.Count / 3);
            }
        }
    }
}
