using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;
using FNARTS.Core.Fog;

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

        /// <param name="fog">Optional fog-of-war — entities on Unexplored tiles
        /// are hidden, entities on Explored tiles are dimmed.</param>
        public void Draw(SpriteBatch sb, Camera2D camera, EntityManager entities,
            SelectionSystem selection, FogOfWar fog = null)
        {
            // Normalisation divisor — max(gx+gy) for the 51×51 map.
            const float maxSum = 102f;

            var visible = new List<(Entity entity, float depth, bool dimmed)>();

            foreach (var e in entities.AllEntities)
            {
                if (!e.IsAlive) continue;

                Vector2 screenPos = camera.WorldToScreen(e.WorldPosition.ToXna());
                if (screenPos.X < -100 || screenPos.X > 2000 ||
                    screenPos.Y < -100 || screenPos.Y > 2000)
                    continue;

                // Fog-of-war filtering
                bool dimmed = false;
                if (fog != null)
                {
                    var gridPos = CoordUtil.WorldToIso(e.WorldPosition);
                    var fogState = fog[gridPos];
                    if (fogState == FogCell.Unexplored)
                        continue;           // hidden — skip entirely
                    if (fogState == FogCell.Explored)
                        dimmed = true;      // visible but dimmed
                }

                float depth;
                if (e is Building b)
                {
                    // Use the *centre* of the footprint for depth.
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

                visible.Add((e, depth, dimmed));
            }

            // Sort far-to-near
            visible.Sort((a, b) => b.depth.CompareTo(a.depth));

            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                camera.ViewMatrix);

            foreach (var (entity, depth, dimmed) in visible)
            {
                Texture2D tex = entity switch
                {
                    Unit u => _assets.GetUnitTexture(u.Definition.Id),
                    Building b => _assets.GetBuildingTexture(b.Definition),
                    _ => _assets.WhitePixel
                };

                Vector2 pos = entity.WorldPosition.ToXna();
                Vector2 origin = new Vector2(tex.Width / 2f, tex.Height / 2f);
                bool isSelected = selection.SelectedEntityIds.Contains(entity.Id);

                // Selection highlight ring — drawn below the entity sprite.
                // Skip highlight for dimmed (out-of-vision) entities.
                if (isSelected && !dimmed)
                {
                    Texture2D hlTex = _assets.SelectionHighlight;
                    Vector2 hlOrigin = new Vector2(hlTex.Width / 2f, hlTex.Height / 2f);
                    sb.Draw(hlTex, pos, null, Color.White, 0f, hlOrigin, 1f,
                        SpriteEffects.None, depth);
                }

                // Tint: green for selected, grey for fog-dimmed.
                Color tint = Color.White;
                if (dimmed)
                    tint = new Color(80, 80, 80, 180);
                else if (isSelected)
                    tint = new Color(180, 255, 180);

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
