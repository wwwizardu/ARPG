# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity-based ARPG (Action Role-Playing Game) project using Unity 6000.0.42f1. The project is configured for 2D game development with the Universal Render Pipeline (URP) and includes comprehensive input system setup for multiple platforms.

## Key Architecture Components

### Unity Configuration
- **Unity Version**: 6000.0.42f1 (LTS)
- **Render Pipeline**: Universal Render Pipeline (URP) 17.0.4
- **Project Template**: 2D with URP setup
- **Input System**: Unity Input System 1.13.1 with comprehensive action mappings

### Core Systems
- **Input Management**: Configured via `Assets/InputSystem_Actions.inputactions` with two main action maps:
  - **Player Actions**: Move, Look, Attack, Interact, Crouch, Jump, Sprint, Previous/Next
  - **UI Actions**: Navigate, Submit, Cancel, Point, Click, RightClick, MiddleClick, ScrollWheel, TrackedDevice controls
- **Multi-platform Support**: Keyboard & Mouse, Gamepad, Touch, Joystick, and XR control schemes

### Project Structure
```
Assets/
├── Scenes/                    # Unity scenes
│   └── SampleScene.unity     # Main scene
├── Settings/                  # Project settings and templates
│   ├── UniversalRP.asset     # URP renderer settings
│   ├── Renderer2D.asset      # 2D renderer configuration
│   └── Scenes/               # Scene templates
├── InputSystem_Actions.inputactions  # Input action mappings
└── DefaultVolumeProfile.asset        # Post-processing volume settings

Packages/
├── manifest.json             # Package dependencies
└── packages-lock.json        # Locked package versions

ProjectSettings/              # Unity project settings
```

### Key Unity Packages
- `com.unity.feature.2d`: Complete 2D development package suite
- `com.unity.render-pipelines.universal`: URP for enhanced 2D rendering
- `com.unity.inputsystem`: Modern input handling system
- `com.unity.2d.animation`: 2D animation tools
- `com.unity.2d.spriteshape`: Advanced 2D shape creation
- `com.unity.timeline`: Cutscene and animation sequencing
- `com.unity.test-framework`: Unit testing framework

## Development Commands

### Unity Editor Operations
```bash
# Open project in Unity (assumes Unity Hub CLI is installed)
unity-hub --headless launch -p . 

# Build project (requires Unity command line tools)
unity -batchmode -quit -projectPath . -buildTarget StandaloneOSX -buildPath ./Builds/ARPG.app

# Run tests
unity -batchmode -quit -projectPath . -runTests -testResults ./TestResults.xml
```

### Version Control
```bash
# Unity-specific git operations
git add Assets/ ProjectSettings/ Packages/manifest.json
git commit -m "Unity project changes"
```

## Input System Configuration

The project uses Unity's Input System with comprehensive action mappings:

### Player Controls
- **Movement**: WASD/Arrow keys, gamepad left stick, touch controls
- **Camera**: Mouse delta, gamepad right stick
- **Actions**: Attack (left click/gamepad button), Jump (space/gamepad button), Sprint (left shift)
- **Interaction**: E key for interact with hold interaction
- **Utility**: Number keys 1/2 for Previous/Next actions

### Development Notes
- Input actions are configured in `InputSystem_Actions.inputactions`
- All control schemes support cross-platform input (Keyboard/Mouse, Gamepad, Touch, XR)
- UI navigation is fully mapped for accessibility across all input methods

## Best Practices for This Project

- Always test input changes across multiple control schemes (keyboard, gamepad, touch)
- Use Unity's Package Manager for dependency management rather than importing assets directly
- Follow Unity's 2D best practices when working with sprites and animations
- Maintain URP compatibility when adding new rendering features
- Keep scenes organized in the Assets/Scenes directory with proper .meta files

## Testing and Quality Assurance

- Use Unity Test Framework for unit testing (`com.unity.test-framework`)
- Test across different build targets (the project supports multiple platforms)
- Validate input system functionality across all supported control schemes
- Ensure URP compatibility for any custom shaders or rendering features