# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Unity 6 project (6000.3.8f1) using the Universal Render Pipeline (URP). Currently a fresh template — `Assets/Scenes/SampleScene.unity` is the only scene and there are no custom gameplay scripts yet.

## Running and Building

This project must be opened and run through the **Unity Editor** (version 6000.3.8f1). There is no CLI build command used in normal development.

- Open the project by pointing the Unity Hub at this directory.
- Run tests via **Window → General → Test Runner** inside the Unity Editor (uses `com.unity.test-framework`).
- Build via **File → Build Profiles** in the Editor.

The solution file `My project.slnx` (Unity 6's new XML-based format) is used by Rider (`com.unity.ide.rider`) and Visual Studio (`com.unity.ide.visualstudio`) for IDE integration.

## Architecture

### Rendering (URP)
Two render pipeline asset pairs exist under `Assets/Settings/`:
- `PC_RPAsset` + `PC_Renderer` — desktop quality settings
- `Mobile_RPAsset` + `Mobile_Renderer` — mobile quality settings

Post-processing volumes: `DefaultVolumeProfile` (global) and `SampleSceneProfile` (per-scene).
URP global settings: `UniversalRenderPipelineGlobalSettings.asset`.

### Input System
Uses the **new Input System** (`com.unity.inputsystem` 1.18.0). The action map is defined in `Assets/InputSystem_Actions.inputactions`. Read or modify input bindings by editing this asset in the Unity Editor's Input Actions window (double-click the asset).

### Key Packages
| Package | Version | Purpose |
|---|---|---|
| `com.unity.render-pipelines.universal` | 17.3.0 | URP rendering |
| `com.unity.inputsystem` | 1.18.0 | New Input System |
| `com.unity.ai.navigation` | 2.0.10 | NavMesh/pathfinding |
| `com.unity.timeline` | 1.8.10 | Cutscene/sequencing |
| `com.unity.visualscripting` | 1.9.9 | Visual scripting |
| `com.unity.test-framework` | 1.6.0 | Edit/Play mode tests |

### Editor Utilities
`Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs` — a custom `[CustomEditor(typeof(Readme))]` that auto-selects the Readme asset on first Editor launch and can self-delete the tutorial folder. This is boilerplate and can be removed once the project is set up.
