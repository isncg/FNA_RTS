# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an **RTS game** built on the **FNA HLSL fork** — a modified FNA (XNA 4.0 reimplementation) that replaces MojoShader with DXC, compiling HLSL to SPIR-V for Vulkan-only rendering through SDL_GPU. The game lives in this directory; the framework it depends on lives in sibling directories.

## Key Documents

- `docs/DEVELOPMENT_PLAN.md` — 整体开发路线 (3 phases: MVP → Combat → Factions+Networking)
- `docs/PHASE1_DEVELOPMENT_PLAN.md` — 第一阶段详细开发文档 (MVP: 等距地图 + 建筑 + 单位 + 选择 + 移动)
- `docs/PATHFINDING_REDESIGN.md` — 寻路系统 as-built 架构（含单位进出格仲裁 MovementSystem 集成说明）

## Repository Map

```
FNA_RTS/                   ← you are here (the RTS game)
../FNA/                    ← FNA C# library (HLSL fork, branch: hlsl)
../FNA/lib/FNA3D/          ← FNA3D_HLSL graphics backend (C library, CMake, SDL_GPU/Vulkan)
../FNA_Test/               ← C# integration test harness (reference for project setup)
../FNA3D_HLSL_Test/        ← Native C rendering tests (reference for FEB/shader authoring)
```

Key sibling references:
- **FNA3D_HLSL architecture & build**: `../FNA/lib/FNA3D/CLAUDE.md`
- **FNA test patterns & constraints**: `../FNA_Test/CLAUDE.md`
- **HLSL→FEB pipeline**: `../FNA3D_HLSL_Test/CLAUDE.md` and `../FNA3D_HLSL_Test/README.md`
- **Upstream diff & migration**: `../FNA/docs/UPSTREAM-DIFF.md`

## Build Commands

### Prerequisites

- .NET SDK (10.0 for modern, 8.0 minimum)
- CMake ≥ 3.10 + Ninja (or Unix Makefiles)
- SDL3 ≥ 3.2.0
- DXC (DirectX Shader Compiler) on `PATH`
- Python 3 (for FEB builder tools)
- Vulkan driver (radv/AMDVLK, or llvmpipe/lavapipe for headless CI)

### Build the native graphics backend (FNA3D_HLSL)

```bash
cd ../FNA/lib/FNA3D
cmake -B build -G Ninja . -DCMAKE_BUILD_TYPE=Release
ninja -C build
# Output: build/libFNA3D.so.27.0.0
```

Build options:
- `-DBUILD_SHARED_LIBS=OFF` — static library
- `-DFNA3D_IMGUI=OFF` — disable Dear ImGui integration (pure C build)

### Build the FNA C# library

```bash
cd ../FNA
dotnet build FNA.Core.csproj   # → bin/Debug/net10.0/FNA.dll
```

### Build and run the RTS game

```bash
# Build
dotnet build

# Ensure native library symlinks exist in the output directory:
#   libFNA3D.so   → ../FNA/lib/FNA3D/build/libFNA3D.so.27.0.0
#   libFNA3D.so.0 → ../FNA/lib/FNA3D/build/libFNA3D.so.27.0.0

# Run
dotnet run
```

See `../FNA_Test/run_tests.sh` for an automated example of the full pipeline (build FNA3D → build FEBs → build FNA → set up symlinks → run).

### Build custom effect shaders

```bash
python3 ../FNA/tools/feb_builder.py path/to/MyEffect.feb.json
```

### Run validation tests (sibling project)

```bash
cd ../FNA_Test && ./run_tests.sh
```

## Architecture

### Shader Pipeline

```
HLSL source (.hlsl, SM 6.0 profiles: vs_6_0 / ps_6_0 / cs_6_0)
  → DXC -spirv -T <profile>
    → SPIR-V binary (.spv)
      → feb_builder.py (reads .feb.json manifest)
        → .feb binary (FNA3D Effect Binary)
          → FNA3D_CreateEffect() at runtime
            → SDL_GPU creates shaders → bind pipeline → draw (Vulkan)
```

There is **no runtime shader compilation**. SPIR-V is consumed natively by SDL_GPU. The `.feb` format (magic `0x42414E46` = "FNAB") is a self-contained binary with header + string table + parameter/technique/pass/shader sections + raw SPIR-V blobs.

### Driver Dispatch Pattern (FNA3D_HLSL)

FNA3D uses a vtable dispatch pattern:
- `include/FNA3D.h` — public API (all functions take `FNA3D_Device*`)
- `src/FNA3D_Driver.h` — `FNA3D_Device` is a vtable of function pointers + opaque `driverData`
- `src/FNA3D.c` — every public function is a thin wrapper calling through `device->FunctionName(device->driverData, ...)`
- `src/FNA3D_Driver_SDL.c` — the sole backend (~4500 lines, SDL_GPU/Vulkan)

### C# to Native Bridge

FNA's `Graphics/FNA3D.cs` uses P/Invoke to call into `libFNA3D.so`. The FNA3D public API mirrors XNA's `GraphicsDevice` operations (create buffers, set render states, draw, present).

### Effect System

Effects are the XNA shader abstraction. Each effect contains techniques → passes → shaders + render states. Stock effects (BasicEffect, AlphaTestEffect, SkinnedEffect, etc.) in `../FNA/src/Graphics/Effect/StockEffects/` use multi-technique FEBs where **one technique = one vertex input layout**.

Effect parameters (uniforms) are baked into the FEB at build time as default values. **FNA3D_HLSL does not yet implement uniform/constant buffer update APIs** — dynamic parameter changes are not possible at runtime.

## Key Constraints

### Vulkan-Only, Linux-Only

- No D3D11, OpenGL, or Metal backends. SPIR-V is the only shader format.
- macOS/iOS are unsupported.
- Headless rendering works via llvmpipe/lavapipe (software Vulkan).

### HLSL Vertex Conventions (C1–C5)

All custom HLSL shaders must follow these conventions (see `../FNA_Test/CLAUDE.md` for full details):

1. **C1 — Sequential Match**: VS_INPUT field order must equal vertex declaration element order. Both sides assign locations sequentially.
2. **C2 — Exact Declaration**: VS_INPUT must declare only the attributes the vertex layout actually provides — no superset declarations.
3. **C3 — Technique = Input Signature**: Effect flags that affect input signature (VertexColorEnabled, TextureEnabled, LightingEnabled) → one technique per layout. Non-signature switches stay as uniform branches.
4. **C4 — Layout Standard**: Use FNA's `IVertexType` element order as authoritative (`../FNA/src/Graphics/Vertices/`).
5. **C5 — Numeric Category Match**: VS_INPUT field types must match vertex format (float ↔ Vector*/Color, uint ↔ Byte4). COLOR format uses BGRA byte order (XNA convention); shader receives normalized RGBA float4.

### COLOR Byte Order

`FNA3D_VERTEXELEMENTFORMAT_COLOR` uses BGRA byte order in memory (XNA convention). HLSL struct fields should be `b, g, r, a`.

### DXC Vertex Attribute Location

DXC assigns SPIR-V locations in HLSL parameter declaration order (0, 1, 2...), not by `usage*16+index`. Vertex declarations in C/C# must match the HLSL struct field order.

### No Runtime Uniform Updates

Shaders must work with default parameter values baked into the FEB, or be pass-through (NDC position + vertex color only). Keep `paramCount = 0` in FEB manifests when possible — the parameter parser has a known stride bug.

## FEB Manifest Format

Example `.feb.json`:

```json
{
  "techniques": [{
    "name": "MainTechnique",
    "passes": [{
      "name": "P0",
      "vertexShader": {"source": "../shaders/my_vs.hlsl", "entry": "VSMain"},
      "pixelShader": {"source": "../shaders/my_ps.hlsl", "entry": "PSMain"},
      "renderStates": [],
      "samplerStates": []
    }]
  }],
  "parameters": []
}
```

`source` paths are relative to the manifest file. Use `vs_6_0` / `ps_6_0` / `cs_6_0` profiles.

## FNA C# Project Setup

A minimal FNA game `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../FNA/FNA.Core.csproj" />
  </ItemGroup>
</Project>
```

Minimal `Program.cs`:

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using var game = new MyGame();
game.Run();

class MyGame : Game
{
    private GraphicsDeviceManager _gdm;

    public MyGame()
    {
        _gdm = new GraphicsDeviceManager(this);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        base.Draw(gameTime);
    }
}
```

## Code Conventions (FNA3D_HLSL C library)

When modifying the native backend:
- C dialect: `-std=gnu99` with `-Wall -Wno-strict-aliasing -pedantic`
- Formatting: tabs (tabstop=8), no spaces for indentation
- Naming: `FNA3D_` prefix for all public types/functions
- Memory: use `SDL_malloc`/`SDL_free`/`SDL_calloc` — never call `malloc`/`free` directly
- Resource disposal: functions named `AddDispose*` (disposal may be deferred from the rendering thread)
- Version: `FNA3D_ABI_VERSION=1`, `FNA3D_MAJOR_VERSION=27`

## Git / Version Control

This project is not currently a git repository. The sibling projects track these branches:
- `../FNA/` — branch `hlsl` (fork from `FNA-XNA/FNA`)
- `../FNA/lib/FNA3D/` — branch `hlsl` (fork from `FNA-XNA/FNA3D` at `isncg/FNA3D`)
