# Repository Guidelines

## Project Structure & Module Organization
- Unity project root with `Assets/`, `Packages/`, `ProjectSettings/`, and build artefact folders. Source scripts live under `Assets/Shogo0x2e/HokuyoUam05lpForUnity` (split into `Runtime/` and `Examples/`).
- Reusable runtime code (sensor I/O, visualizers, internal helpers) belongs in `Runtime/`. Sample scenes, prefabs, and scripts demonstrating usage belong in `Examples/`.
- Keep editor tooling under `Assets/Shogo0x2e/HokuyoUam05lpForUnity/Editor`. Avoid modifying `Library/`, `Temp/`, or `Logs/`.

## Build, Test, and Development Commands
- `open HokuyoUam05lpForUnity.sln` – open the solution in an IDE for intellisense and C# analysis.
- `dotnet build HokuyoUam05lpForUnity.sln` – quick compile check of runtime assemblies outside the Unity editor.
- Unity batch build (CI or local scripting):
  - `Unity -quit -batchmode -projectPath "$(pwd)" -executeMethod BuildScripts.PerformBuild`
  - Adjust the execute method when a build script is added; keep the command documented in `BuildScripts`.

## Coding Style & Naming Conventions
- C# code uses 4-space indentation, `PascalCase` for public members/types, `camelCase` for locals and private fields, and explicit access modifiers.
- Place each type in its own file under a matching folder (e.g., `Runtime/Internal/UamClient.cs`). Avoid nested namespaces; use `Shogo0x2e.HokuyoUam05lpForUnity` variants.
- Prefer `UnityEngine.Debug.Log` only behind feature flags (e.g., `VerboseLogging`). Use `readonly` fields and `#nullable enable` in new files to match existing patterns.

## Testing Guidelines
- Runtime logic currently validated through Unity Play Mode (manual) and `dotnet build` static checks. When adding automated tests, place EditMode tests in `Assets/Tests/EditMode` and PlayMode tests in `Assets/Tests/PlayMode` following Unity Test Framework conventions.
- Name tests `ClassName_ShouldDoThing` and keep fixtures small. Document any new test commands in this guide.

## Commit & Pull Request Guidelines
- Follow conventional, informative commit messages (e.g., `feat: add Uam sensor visualizer` or `fix: prevent buffer recycle crash`).
- Pull requests should include: concise summary, testing notes (`dotnet build`, Unity version used), screenshots or GIFs for visual changes, and linked issue IDs when tracking tasks.
- Ensure scenes and prefabs that are modified are saved and verified in the target Unity version before submitting.
