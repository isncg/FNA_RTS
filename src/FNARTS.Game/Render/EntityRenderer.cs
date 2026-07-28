using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;

namespace FNARTS.Game
{
    /// <summary>
    /// Renders entities with isometric depth sorting and selection highlights.
    /// </summary>
    public class EntityRenderer : IDisposable
    {
        private readonly IAssetProvider _assets;

        public EntityRenderer(IAssetProvider assets)
        {
            _assets = assets;
        }

        public void Draw(SpriteBatch sb, Camera2D camera, EntityManager entities,
            SelectionSystem selection)
        {
            // Normalisation divisor — max(gx+gy) for the 51×51 map.
            const float maxSum = 102f;

            var visible = new List<(Entity entity, float depth)>();

            foreach (var e in entities.AllEntities)
            {
                if (!e.IsAlive) continue;

                Vector2 screenPos = camera.WorldToScreen(e.WorldPosition.ToXna());
                if (screenPos.X < -100 || screenPos.X > 2000 ||
                    screenPos.Y < -100 || screenPos.Y > 2000)
                    continue;

                float depth;
                if (e is Building b)
                {
                    // Use the *centre* of the footprint for depth.
                    // A building occupies a range of depths (SW=nearest →
                    // NE=farthest) but must be drawn as a single sprite.
                    // The centre is the best compromise:
                    //   - Units on the south/west (near) side sort in front  ✓
                    //   - Units on the north/east (far) side sort behind   ✓
                    //   - Building-vs-building comparisons are fair         ✓
                    float centerGx = b.PlacementOrigin.X + b.SizeX * 0.5f;
                    float centerGy = b.PlacementOrigin.Y + b.SizeY * 0.5f;
                    depth = MathHelper.Clamp((centerGx + centerGy) / maxSum, 0, 1);
                }
                else
                {
                    var gridFloat = CoordUtil.WorldToIsoFloat(e.WorldPosition);
                    depth = MathHelper.Clamp(
                        (gridFloat.X + gridFloat.Y) / maxSum, 0, 1);
                }

                visible.Add((e, depth));
            }

            // Sort far-to-near
            visible.Sort((a, b) => b.depth.CompareTo(a.depth));

            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                camera.ViewMatrix);

            foreach (var (entity, depth) in visible)
            {
                Texture2D tex = entity switch
                {
                    Unit u => _assets.GetUnitTexture(u.Definition.Id),
                    Building b => _assets.GetBuildingTexture(b.Definition),
                    _ => _assets.WhitePixel
                };

                Vector2 pos = entity.WorldPosition.ToXna();
                Vector2 origin = new Vector2(tex.Width / 2f, tex.Height / 2f);
                Color tint = selection.SelectedEntityIds.Contains(entity.Id)
                    ? new Color(150, 255, 150) : Color.White;

                sb.Draw(tex, pos, null, tint, 0f, origin, 1f,
                    SpriteEffects.None, depth);
            }

            sb.End();
        }

        public void Dispose() { }

        /// <summary>Draw a semi-transparent placement ghost at a grid position.</summary>
        public void DrawGhost(SpriteBatch sb, Camera2D camera,
            BuildingDef def, IsoCoord gridPos, bool isValid)
        {
            Vector2 pos = CoordUtil.BuildingWorldOrigin(gridPos, def.SizeX, def.SizeY).ToXna();

            // Depth: center of footprint (same as real buildings).
            const float maxSum = 102f;
            float centerGx = gridPos.X + def.SizeX * 0.5f;
            float centerGy = gridPos.Y + def.SizeY * 0.5f;
            float depth = MathHelper.Clamp((centerGx + centerGy) / maxSum, 0, 1);

            Texture2D tex = _assets.GetBuildingTexture(def);
            Vector2 origin = new Vector2(tex.Width / 2f, tex.Height / 2f);
            Color tint = isValid
                ? new Color(0, 255, 0, 100)   // green, ~40% alpha
                : new Color(255, 0, 0, 100);  // red, ~40% alpha

            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                camera.ViewMatrix);

            sb.Draw(tex, pos, null, tint, 0f, origin, 1f, SpriteEffects.None, depth);

            sb.End();
        }
    }
}
