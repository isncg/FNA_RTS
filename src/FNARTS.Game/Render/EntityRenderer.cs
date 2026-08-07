using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;
using FNARTS.Core.Fog;
using FNARTS.Core.Movement;

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
        /// <param name="vehicles">3D vehicle units. They are interleaved into
        /// the painter's-algorithm draw order: far sprites are flushed first,
        /// then the vehicle 3D pass runs, then near sprites — otherwise the
        /// sprite pass (which ignores the depth buffer) overdraws vehicles.</param>
        public void Draw(SpriteBatch sb, Camera2D camera, EntityManager entities,
            SelectionSystem selection, GroupMovement? groupMovement = null,
            FogOfWar? fog = null, IReadOnlyList<Unit>? vehicles = null,
            Action<Unit>? drawVehicle = null)
        {
            // Normalisation divisor — max(gx+gy) for the 51×51 map.
            const float maxSum = 102f;

            var visible = new List<(Entity entity, float depth, bool dimmed)>();

            foreach (var e in entities.AllEntities)
            {
                if (!e.IsAlive) continue;

                // Vehicles are drawn as 3D models by VehicleRenderer —
                // skip the 2D circle sprite so it doesn't occlude the model.
                if (e is Unit vu && vu.IsVehicle) continue;

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

            // Insert vehicles into the same sorted list at their iso depth so
            // the sprite runs before/after each vehicle can be flushed around it.
            var drawOrder = new List<(Entity? entity, Unit? vehicle, float depth, bool dimmed)>();
            foreach (var v in visible)
                drawOrder.Add((v.entity, null, v.depth, v.dimmed));
            if (vehicles != null)
            {
                foreach (var v in vehicles)
                {
                    if (!v.IsAlive || !v.IsVehicle) continue;
                    var gridFloat = CoordUtil.WorldToIsoFloat(v.WorldPosition);
                    float depth = MathHelper.Clamp(
                        (gridFloat.X + gridFloat.Y) / maxSum, 0, 1);
                    drawOrder.Add((null, v, depth, false));
                }
                drawOrder.Sort((a, b) => b.depth.CompareTo(a.depth));
            }

            BeginSpritePass(sb, camera);

            bool batchOpen = true;
            foreach (var (entity, vehicle, depth, dimmed) in drawOrder)
            {
                // 3D vehicle: flush the sprites drawn so far, run the 3D
                // pass, then resume the sprite batch for nearer entities.
                if (vehicle != null)
                {
                    sb.End();
                    batchOpen = false;
                    drawVehicle?.Invoke(vehicle);
                    BeginSpritePass(sb, camera);
                    batchOpen = true;
                    continue;
                }

                DrawEntity(sb, entity!, depth, dimmed, selection, groupMovement);
            }

            if (batchOpen)
                sb.End();
        }

        private static void BeginSpritePass(SpriteBatch sb, Camera2D camera)
        {
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                camera.ViewMatrix);
        }

        private void DrawEntity(SpriteBatch sb, Entity entity, float depth,
            bool dimmed, SelectionSystem selection, GroupMovement? groupMovement)
        {
            Texture2D tex = entity switch
            {
                Unit u => _assets.GetUnitTexture(u.Definition.Id),
                Building b => _assets.GetBuildingTexture(b.Definition),
                _ => _assets.WhitePixel
            };

            Vector2 pos = entity.WorldPosition.ToXna();
            // Infantry sprites are standing humanoids anchored at
            // bottom-centre: the texture bottom sits on the sub-cell slot
            // point (the unit's feet). Everything else is centred.
            bool bottomAnchored = entity is Unit iu && iu.IsInfantry;
            Vector2 origin = bottomAnchored
                ? new Vector2(tex.Width / 2f, tex.Height)
                : new Vector2(tex.Width / 2f, tex.Height / 2f);
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

            // Leader indicator: gold diamond above the unit
            if (groupMovement != null && entity is Unit unitEntity
                && groupMovement.Units[groupMovement.LeaderIndex].Id == unitEntity.Id)
            {
                Texture2D hlTex = _assets.SelectionHighlight;
                Vector2 hlOrigin = new Vector2(hlTex.Width / 2f, hlTex.Height / 2f);
                float indicatorY = pos.Y - origin.Y - 10f;
                // Use a gold tint
                sb.Draw(hlTex, new Vector2(pos.X, indicatorY), null,
                    new Color(255, 200, 50, 220), 0f, hlOrigin, 0.6f,
                    SpriteEffects.None, depth);
            }
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
