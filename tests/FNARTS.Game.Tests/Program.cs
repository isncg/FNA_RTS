using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNARTS.Core;
using FNA.Test;

namespace FNARTS.Game.Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var game = new GameTest();
            game.Run();
        }
    }

    class GameTest : Microsoft.Xna.Framework.Game
    {
        private GraphicsDeviceManager _gdm;
        private SpriteBatch _sb;
        private Camera2D _camera;
        private List<string> _failures = new();

        public GameTest()
        {
            _gdm = new GraphicsDeviceManager(this);
            _gdm.PreferredBackBufferWidth = 640;
            _gdm.PreferredBackBufferHeight = 480;
            IsFixedTimeStep = true;
            TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
            IsMouseVisible = false;
        }

        protected override void Initialize()
        {
            _camera = new Camera2D(640, 480);
            _camera.Zoom = 1.0f;
            _camera.Position = Vector2.Zero;
            _camera.RebuildMatrices();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _sb = new SpriteBatch(GraphicsDevice);
        }

        protected override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Run all tests early (frame 4) then exit
            TestHarness.Tick(this, 4, () =>
            {
                TestCameraRoundTrip();
                TestCameraZoomRoundTrip();
                TestVectorConvert();
                TestIsoCoordRoundTrip();
                TestHarness.Report("FNARTS.Game.Tests", _failures.Count);
            });

            base.Update(gameTime);
        }

        // ── Camera round-trip test ──────────────────────────────────────

        private void TestCameraRoundTrip()
        {
            _camera.Zoom = 1.0f;
            _camera.Position = new Vector2(100, 50);
            _camera.RebuildMatrices();

            var screenCenter = new Vector2(320, 240);
            var worldCenter = _camera.ScreenToWorld(screenCenter);
            AssertNear(worldCenter, new Vector2(100, 50), 0.01f, "Screen center -> world");

            var back = _camera.WorldToScreen(worldCenter);
            AssertNear(back, screenCenter, 0.01f, "World -> screen round-trip");
        }

        // ── Zoom round-trip test ────────────────────────────────────────

        private void TestCameraZoomRoundTrip()
        {
            _camera.Zoom = 2.0f;
            _camera.Position = new Vector2(200, 150);
            _camera.RebuildMatrices();

            var screenPoint = new Vector2(100, 100);
            var worldPoint = _camera.ScreenToWorld(screenPoint);
            var backToScreen = _camera.WorldToScreen(worldPoint);
            AssertNear(backToScreen, screenPoint, 0.01f, "Screen->world->screen z2");

            var worldOrigin = Vector2.Zero;
            var onScreen = _camera.WorldToScreen(worldOrigin);
            var backToWorld = _camera.ScreenToWorld(onScreen);
            AssertNear(backToWorld, worldOrigin, 0.01f, "World->screen->world z2");
        }

        // ── VectorConvert test ──────────────────────────────────────────

        private void TestVectorConvert()
        {
            var sysVec = new System.Numerics.Vector2(3.5f, 7.2f);
            var xnaVec = sysVec.ToXna();
            AssertEqual(xnaVec.X, 3.5f, "ToXna.X");
            AssertEqual(xnaVec.Y, 7.2f, "ToXna.Y");

            var back = xnaVec.ToNumerics();
            AssertEqual(back.X, 3.5f, "ToNumerics.X");
            AssertEqual(back.Y, 7.2f, "ToNumerics.Y");
            if (back != sysVec)
                _failures.Add($"VectorConvert: round-trip mismatch {back} != {sysVec}");
        }

        // ── IsoCoord center round-trip test ─────────────────────────────

        private void TestIsoCoordRoundTrip()
        {
            for (int x = 0; x < 10; x++)
            for (int y = 0; y < 10; y++)
            {
                var original = new IsoCoord(x, y);
                var worldCenter = CoordUtil.IsoToWorldCenter(original);
                var back = CoordUtil.WorldToIso(worldCenter);
                if (!original.Equals(back))
                    _failures.Add($"IsoRoundTrip: ({x},{y}) -> ({back.X},{back.Y})");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private void AssertNear(Vector2 actual, Vector2 expected, float tol, string label)
        {
            if (Math.Abs(actual.X - expected.X) > tol ||
                Math.Abs(actual.Y - expected.Y) > tol)
                _failures.Add($"{label}: ({actual.X:F3},{actual.Y:F3}) != ({expected.X:F3},{expected.Y:F3})");
        }

        private void AssertEqual(float actual, float expected, string label)
        {
            if (Math.Abs(actual - expected) > 0.0001f)
                _failures.Add($"{label}: {actual} != {expected}");
        }

        // ── Draw ────────────────────────────────────────────────────────

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);
            base.Draw(gameTime);
        }
    }
}
