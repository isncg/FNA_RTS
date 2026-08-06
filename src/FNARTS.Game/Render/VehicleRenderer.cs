using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNARTS.Core;

namespace FNARTS.Game.Render
{
    /// <summary>
    /// Renders 3D vehicle units (tank prototype) using a pass-through
    /// FEB shader with CPU-side MVP transformation.
    ///
    /// Workflow per frame:
    ///   1. For each vehicle, compute body + turret world matrices
    ///   2. Transform vertices from local space to NDC on CPU
    ///   3. Upload to vertex buffer
    ///   4. Draw with pass-through FEB effect
    /// </summary>
    public class VehicleRenderer : IDisposable
    {
        private readonly GraphicsDevice _device;
        private readonly Effect _effect;

        // Shared index buffer (36 indices = 6 faces × 2 triangles × 3 indices)
        private readonly IndexBuffer _indexBuffer;

        // Dynamic vertex buffer — uploaded each frame from CPU-transformed data
        private readonly DynamicVertexBuffer _vertexBuffer;

        // Scratch buffers for CPU-side transform
        private VertexPositionColor[] _scratchVerts;
        private int _scratchCapacity;

        // Viewport dimensions for NDC conversion
        private int _viewportW;
        private int _viewportH;

        // ── Vehicle geometry constants ────────────────────────────────
        private const int BodyVerts = 24;    // 6 faces × 4 verts
        private const int BodyIndices = 36;  // 6 faces × 6 indices
        private const int TurretVerts = 24;
        private const int TurretIndices = 36;
        private const int BarrelVerts = 24;
        private const int BarrelIndices = 36;
        private const int PlateVerts = 24;
        private const int PlateIndices = 36;

        // Body box: forward = +X (East at rotation 0), half-extents:
        // half-length (X), half-width (Y), half-height (Z)
        private static readonly Vector3 BodyHalf = new(12f, 8f, 4f);

        // ── Real-MBT layout: turret ring forward (engine aft), bustle aft ──
        // Turret ring (turret rotation pivot) offset from the hull centre
        // along the body forward axis. The rear of the hull is the engine bay.
        private const float TurretRingOffsetX = 4f;
        // Turret extents from the ring: short fighting compartment forward,
        // long ammo bustle aft to counterbalance the gun.
        private const float TurretFrontExtent = 6f;
        private const float TurretBustleExtent = 9f;
        private const float TurretHalfWidth = 5f;
        private const float TurretHalfHeight = 3f;
        // Geometric centre offset from the ring in turret space (negative = aft)
        private const float TurretCenterOffsetX =
            (TurretFrontExtent - TurretBustleExtent) * 0.5f;
        private static readonly Vector3 TurretHalf =
            new((TurretFrontExtent + TurretBustleExtent) * 0.5f,
                TurretHalfWidth, TurretHalfHeight);

        // Engine-deck plate on the hull rear (sits on the body top)
        private static readonly Vector3 PlateHalf = new(5f, 7f, 1f);
        private static readonly float PlateCenterX = -(BodyHalf.X - PlateHalf.X); // X ∈ [-12, -2]
        private static readonly Vector3 PlateCenterZ =
            new(0f, 0f, 2f * BodyHalf.Z + PlateHalf.Z);

        // Barrel: thin box protruding forward (+X) from the turret
        private static readonly Vector3 BarrelHalf = new(7f, 1.5f, 1.5f);
        // Barrel offset from the ring along the turret forward axis
        private static readonly float BarrelOffsetX =
            TurretFrontExtent + BarrelHalf.X;

        // Colors
        private static readonly Color BodyColor = new(120, 140, 100);
        private static readonly Color BodyFrontColor = new(180, 200, 150);
        private static readonly Color BodyDarkColor = new(80, 100, 60);
        private static readonly Color TurretColor = new(160, 130, 100);
        private static readonly Color TurretDarkColor = new(120, 100, 70);
        private static readonly Color BarrelColor = new(90, 90, 95);
        private static readonly Color PlateColor = new(100, 115, 78);
        private static readonly Color PlateTopColor = new(90, 105, 68);

        public VehicleRenderer(GraphicsDevice device)
        {
            _device = device;

            // Load the custom FEB shader from embedded resource
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(
                "FNARTS.Game.Shaders.VertexColor.feb");
            if (stream == null)
                throw new InvalidOperationException(
                    "Missing embedded resource: FNARTS.Game.Shaders.VertexColor.feb");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            _effect = new Effect(device, ms.ToArray());

            // Build shared index buffer (16-bit indices, 2 triangles per face)
            int maxIndices = Math.Max(BodyIndices, TurretIndices);
            _indexBuffer = new IndexBuffer(device, IndexElementSize.SixteenBits,
                maxIndices, BufferUsage.WriteOnly);
            var indices = new ushort[maxIndices];
            BuildBoxIndices(indices, 0);
            _indexBuffer.SetData(indices);

            // Dynamic vertex buffer — large enough for ~64 vehicles (body+plate+turret+barrel)
            _scratchCapacity = (BodyVerts + PlateVerts + TurretVerts + BarrelVerts) * 64;
            _scratchVerts = new VertexPositionColor[_scratchCapacity];
            _vertexBuffer = new DynamicVertexBuffer(device,
                VertexPositionColor.VertexDeclaration, _scratchCapacity,
                BufferUsage.WriteOnly);

            _viewportW = device.Viewport.Width;
            _viewportH = device.Viewport.Height;
        }

        /// <summary>
        /// Draw 3D vehicles. Interleaved into the entity sprite pass by
        /// iso depth (see EntityRenderer.Draw), since SpriteBatch ignores
        /// the depth buffer and would otherwise overdraw the models.
        /// </summary>
        /// <param name="camera">The 2D isometric camera (used for view/projection).</param>
        /// <param name="vehicles">The set of units to render as 3D vehicles.</param>
        public void Draw(Camera2D camera, IEnumerable<Unit> vehicles)
        {
            // Update viewport dimensions
            var vp = _device.Viewport;
            _viewportW = vp.Width;
            _viewportH = vp.Height;

            // Set up 3D rendering state. Depth testing resolves box-face
            // visibility; culling is disabled so winding order can't hide faces.
            _device.BlendState = BlendState.AlphaBlend;
            _device.DepthStencilState = DepthStencilState.Default;
            _device.RasterizerState = RasterizerState.CullNone;

            // Apply the pass-through effect
            _effect.CurrentTechnique.Passes[0].Apply();
            _device.Indices = _indexBuffer;

            // Pre-compute isometric scale factors from camera
            float halfTileW = CoordUtil.HALF_TILE_W;
            float halfTileH = CoordUtil.HALF_TILE_H;

            // Camera view matrix for world→screen transform
            Matrix viewMatrix = camera.ViewMatrix;

            int vertOffset = 0;
            int vehicleCount = 0;

            foreach (var vehicle in vehicles)
            {
                if (!vehicle.IsAlive || !vehicle.IsVehicle)
                    continue;

                // ── Body transform ────────────────────────────────────
                float bodyAngle = vehicle.BodyRotation;
                float cosB = MathF.Cos(bodyAngle);
                float sinB = MathF.Sin(bodyAngle);

                float vx = vehicle.VehiclePosition3D.X;
                float vy = vehicle.VehiclePosition3D.Y;
                float vz = vehicle.VehiclePosition3D.Z;

                // Per-vehicle base depth. The backend clip space is z ∈ [0,1]
                // (0 = closest), so all depths must stay inside that range —
                // negative z gets clipped away entirely. Base is anchored to
                // the vehicle's screen position; per-vertex deltas are small.
                var centerScreen = Vector2.Transform(
                    new Vector2(vx, vy), viewMatrix);
                float baseDepth = MathHelper.Clamp(
                    0.5f - (centerScreen.Y / _viewportH - 0.5f) * 0.3f,
                    0.05f, 0.95f);

                // Transform body vertices: local → isometric world → screen → NDC
                for (int i = 0; i < BodyVerts; i++)
                {
                    ref var src = ref _bodyLocalVerts[i];
                    // Rotate around Z
                    float rx = src.Position.X * cosB - src.Position.Y * sinB;
                    float ry = src.Position.X * sinB + src.Position.Y * cosB;
                    // Lift the box by its half-height: local verts span
                    // z ∈ [-hz, +hz], but the vehicle position is ground
                    // level — the chassis bottom must rest ON the tile,
                    // not be centred inside it.
                    float rz = src.Position.Z + BodyHalf.Z;

                    // 3D world → isometric world position
                    float isoWx = (rx - ry) * halfTileW / 32f + vx;
                    float isoWy = -(rx + ry) * halfTileH / 32f - rz * 1.5f + vy;

                    // Apply camera view transform → screen coords
                    var screenPos = Vector2.Transform(
                        new Vector2(isoWx, isoWy), viewMatrix);

                    // Screen → NDC. Depth encodes distance along the
                    // isometric view direction: larger screen Y (lower on
                    // screen) and higher world Z are both closer to the
                    // camera. Height-only depth made opposite side faces
                    // Z-fight and exposed the box interior.
                    float ndx = (screenPos.X / _viewportW) * 2f - 1f;
                    float ndy = 1f - (screenPos.Y / _viewportH) * 2f;
                    float ndz = MathHelper.Clamp(
                        baseDepth - (screenPos.Y - centerScreen.Y) * 0.001f
                        - rz * 0.03f, 0f, 1f);

                    _scratchVerts[vertOffset + i] = new VertexPositionColor(
                        new Vector3(ndx, ndy, ndz), src.Color);
                }

                vertOffset += BodyVerts;

                // ── Turret ring position ──────────────────────────────
                // The ring is fixed on the hull, forward of the hull centre
                // (engine bay aft). Rotate the forward offset by the body
                // yaw, then project with the same iso transform used for
                // vertices: x=(ox-oy)·hw/32, y=-(ox+oy)·hh/32.
                float ringOx = TurretRingOffsetX * cosB;
                float ringOy = TurretRingOffsetX * sinB;
                float tox = vx + (ringOx - ringOy) * halfTileW / 32f;
                float toy = vy - (ringOx + ringOy) * halfTileH / 32f;
                // Body verts are lifted by BodyHalf.Z (bottom on ground), so
                // the body top is at vz + 2×BodyHalf.Z. Sink the turret 1
                // unit into it: exactly coplanar would Z-fight in the overlap.
                float toz = vz + 2f * BodyHalf.Z + TurretHalf.Z - 1f;

                // ── Turret transform (rotates about the ring) ─────────
                float turretAngle = vehicle.TurretRotation;
                float cosT = MathF.Cos(turretAngle);
                float sinT = MathF.Sin(turretAngle);

                for (int i = 0; i < TurretVerts; i++)
                {
                    ref var src = ref _turretLocalVerts[i];
                    // Local verts are centred on the geometric centre; shift
                    // them so the ring sits between the fighting compartment
                    // and the bustle, then rotate about the ring.
                    float lx = src.Position.X + TurretCenterOffsetX;
                    float ly = src.Position.Y;
                    float rx = lx * cosT - ly * sinT;
                    float ry = lx * sinT + ly * cosT;
                    float rz = src.Position.Z;

                    float isoWx = (rx - ry) * halfTileW / 32f + tox;
                    float isoWy = -(rx + ry) * halfTileH / 32f
                                  - (toz + rz) * 1.5f + toy;

                    var screenPos = Vector2.Transform(
                        new Vector2(isoWx, isoWy), viewMatrix);

                    float ndx = (screenPos.X / _viewportW) * 2f - 1f;
                    float ndy = 1f - (screenPos.Y / _viewportH) * 2f;
                    float ndz = MathHelper.Clamp(
                        baseDepth - (screenPos.Y - centerScreen.Y) * 0.001f
                        - (toz + rz - vz) * 0.03f, 0f, 1f);

                    _scratchVerts[vertOffset + i] = new VertexPositionColor(
                        new Vector3(ndx, ndy, ndz), src.Color);
                }

                vertOffset += TurretVerts;

                // ── Barrel transform (offset forward in turret space) ─
                for (int i = 0; i < BarrelVerts; i++)
                {
                    ref var src = ref _barrelLocalVerts[i];
                    // Offset along turret forward (+X), then rotate
                    float lx = src.Position.X + BarrelOffsetX;
                    float ly = src.Position.Y;
                    float rx = lx * cosT - ly * sinT;
                    float ry = lx * sinT + ly * cosT;
                    float rz = src.Position.Z;

                    float isoWx = (rx - ry) * halfTileW / 32f + tox;
                    float isoWy = -(rx + ry) * halfTileH / 32f
                                  - (toz + rz) * 1.5f + toy;

                    var screenPos = Vector2.Transform(
                        new Vector2(isoWx, isoWy), viewMatrix);

                    float ndx = (screenPos.X / _viewportW) * 2f - 1f;
                    float ndy = 1f - (screenPos.Y / _viewportH) * 2f;
                    float ndz = MathHelper.Clamp(
                        baseDepth - (screenPos.Y - centerScreen.Y) * 0.001f
                        - (toz + rz - vz) * 0.03f, 0f, 1f);

                    _scratchVerts[vertOffset + i] = new VertexPositionColor(
                        new Vector3(ndx, ndy, ndz), src.Color);
                }

                vertOffset += BarrelVerts;

                // ── Engine deck plate (fixed to the hull rear) ────────
                for (int i = 0; i < PlateVerts; i++)
                {
                    ref var src = ref _plateLocalVerts[i];
                    // Rotate around Z with the body, offset to the hull rear
                    float lx = src.Position.X + PlateCenterX;
                    float ly = src.Position.Y;
                    float rx = lx * cosB - ly * sinB;
                    float ry = lx * sinB + ly * cosB;
                    float rz = src.Position.Z + PlateCenterZ.Z;

                    float isoWx = (rx - ry) * halfTileW / 32f + vx;
                    float isoWy = -(rx + ry) * halfTileH / 32f - rz * 1.5f + vy;

                    var screenPos = Vector2.Transform(
                        new Vector2(isoWx, isoWy), viewMatrix);

                    float ndx = (screenPos.X / _viewportW) * 2f - 1f;
                    float ndy = 1f - (screenPos.Y / _viewportH) * 2f;
                    float ndz = MathHelper.Clamp(
                        baseDepth - (screenPos.Y - centerScreen.Y) * 0.001f
                        - rz * 0.03f, 0f, 1f);

                    _scratchVerts[vertOffset + i] = new VertexPositionColor(
                        new Vector3(ndx, ndy, ndz), src.Color);
                }

                vertOffset += PlateVerts;
                vehicleCount++;

                // Flush if scratch buffer is nearly full
                if (vertOffset + BodyVerts + TurretVerts + BarrelVerts + PlateVerts > _scratchCapacity)
                {
                    // Upload and draw what we have so far
                    if (vertOffset > 0)
                        FlushBuffer(vertOffset);
                    vertOffset = 0;
                }
            }

            // Final flush
            if (vertOffset > 0)
                FlushBuffer(vertOffset);
        }

        // Reusable single-element list for DrawSingle (avoids per-frame alloc)
        private readonly Unit[] _singleVehicle = new Unit[1];

        /// <summary>
        /// Draw a single 3D vehicle — used by the interleaved sprite/3D pass.
        /// </summary>
        public void DrawSingle(Camera2D camera, Unit vehicle)
        {
            _singleVehicle[0] = vehicle;
            Draw(camera, _singleVehicle);
            _singleVehicle[0] = null!;
        }

        /// <summary>
        /// Upload the transformed vertex data to the GPU and draw all batches.
        /// Each batch is a single DrawIndexedPrimitives call per vehicle.
        /// </summary>
        private void FlushBuffer(int vertexCount)
        {
            // Upload scratch data to the dynamic vertex buffer
            _vertexBuffer.SetData(_scratchVerts, 0, vertexCount, SetDataOptions.Discard);
            _device.SetVertexBuffer(_vertexBuffer);

            // Draw each vehicle's body, turret, barrel and engine-deck plate
            // as separate indexed draw calls. The shared index buffer has
            // indices 0-35 for a 24-vertex box; vertexOffset is added to
            // every index.
            int vo = 0;
            int perVehicle = BodyVerts + TurretVerts + BarrelVerts + PlateVerts;
            while (vo < vertexCount)
            {
                // Body: 24 vertices, 36 indices (12 triangles)
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    vo, 0, BodyVerts, 0, BodyIndices / 3);

                // Turret: 24 vertices, 36 indices (12 triangles)
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    vo + BodyVerts, 0, TurretVerts, 0, TurretIndices / 3);

                // Barrel: 24 vertices, 36 indices (12 triangles)
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    vo + BodyVerts + TurretVerts, 0, BarrelVerts, 0, BarrelIndices / 3);

                // Engine deck plate: 24 vertices, 36 indices (12 triangles)
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    vo + BodyVerts + TurretVerts + BarrelVerts, 0,
                    PlateVerts, 0, PlateIndices / 3);

                vo += perVehicle;
            }
        }

        // ── Box geometry builders ─────────────────────────────────────

        private static VertexPositionColor[] BuildBoxVerts(
            Vector3 half, Color front, Color back, Color left, Color right,
            Color top, Color bottom)
        {
            float hx = half.X, hy = half.Y, hz = half.Z;
            return new VertexPositionColor[]
            {
                // Front face (+X) — front color (forward = +X at rotation 0)
                new(new Vector3( hx, -hy, -hz), front),
                new(new Vector3( hx,  hy, -hz), front),
                new(new Vector3( hx,  hy,  hz), front),
                new(new Vector3( hx, -hy,  hz), front),
                // Back face (-X) — back color
                new(new Vector3(-hx, -hy, -hz), back),
                new(new Vector3(-hx,  hy, -hz), back),
                new(new Vector3(-hx,  hy,  hz), back),
                new(new Vector3(-hx, -hy,  hz), back),
                // Left face (+Y) — left color
                new(new Vector3(-hx,  hy, -hz), left),
                new(new Vector3( hx,  hy, -hz), left),
                new(new Vector3( hx,  hy,  hz), left),
                new(new Vector3(-hx,  hy,  hz), left),
                // Right face (-Y) — right color
                new(new Vector3(-hx, -hy, -hz), right),
                new(new Vector3( hx, -hy, -hz), right),
                new(new Vector3( hx, -hy,  hz), right),
                new(new Vector3(-hx, -hy,  hz), right),
                // Top face (+Z) — top color
                new(new Vector3(-hx, -hy,  hz), top),
                new(new Vector3( hx, -hy,  hz), top),
                new(new Vector3( hx,  hy,  hz), top),
                new(new Vector3(-hx,  hy,  hz), top),
                // Bottom face (-Z) — bottom color
                new(new Vector3(-hx, -hy, -hz), bottom),
                new(new Vector3( hx, -hy, -hz), bottom),
                new(new Vector3( hx,  hy, -hz), bottom),
                new(new Vector3(-hx,  hy, -hz), bottom),
            };
        }

        private static void BuildBoxIndices(ushort[] indices, int offset)
        {
            // 6 faces, 6 indices per face (2 triangles)
            for (int f = 0; f < 6; f++)
            {
                int baseV = f * 4;
                int ioff = f * 6;
                // Triangle 1: 0-1-2
                indices[offset + ioff + 0] = (ushort)(baseV + 0);
                indices[offset + ioff + 1] = (ushort)(baseV + 1);
                indices[offset + ioff + 2] = (ushort)(baseV + 2);
                // Triangle 2: 0-2-3
                indices[offset + ioff + 3] = (ushort)(baseV + 0);
                indices[offset + ioff + 4] = (ushort)(baseV + 2);
                indices[offset + ioff + 5] = (ushort)(baseV + 3);
            }
        }

        // ── Pre-built local-space vertices ────────────────────────────

        private static readonly VertexPositionColor[] _bodyLocalVerts;
        private static readonly VertexPositionColor[] _turretLocalVerts;
        private static readonly VertexPositionColor[] _barrelLocalVerts;
        private static readonly VertexPositionColor[] _plateLocalVerts;

        static VehicleRenderer()
        {
            _bodyLocalVerts = BuildBoxVerts(
                BodyHalf,
                front: BodyFrontColor,    // +X (forward)
                back: BodyDarkColor,      // -X
                left: BodyColor,          // +Y
                right: BodyColor,         // -Y
                top: new Color(140, 160, 120),   // +Z
                bottom: BodyDarkColor     // -Z
            );

            _turretLocalVerts = BuildBoxVerts(
                TurretHalf,
                front: TurretColor,       // +X (forward)
                back: TurretDarkColor,    // -X
                left: TurretColor,        // +Y
                right: TurretColor,       // -Y
                top: new Color(180, 150, 120),  // +Z
                bottom: TurretDarkColor   // -Z
            );

            _barrelLocalVerts = BuildBoxVerts(
                BarrelHalf,
                front: BarrelColor, back: BarrelColor,
                left: BarrelColor, right: BarrelColor,
                top: new Color(110, 110, 115), bottom: BarrelColor
            );

            _plateLocalVerts = BuildBoxVerts(
                PlateHalf,
                front: PlateColor, back: BodyDarkColor,
                left: PlateColor, right: PlateColor,
                top: PlateTopColor, bottom: BodyDarkColor
            );
        }

        public void Dispose()
        {
            _effect?.Dispose();
            _indexBuffer?.Dispose();
            _vertexBuffer?.Dispose();
        }
    }
}