# Obfy UI Frontend Implementation Plan

## Overview

Build a WPF desktop application for Obfy using the same tech stack as Pulse:
- **WPF-UI v4.1.0** - Fluent Design System
- **CommunityToolkit.Mvvm v8.4.0** - MVVM with source generators
- **Autofac v9.0.0** - Dependency injection
- **R3 v1.3.0** - Reactive extensions

## Layout: Three-Panel Design

```
┌─────────────────────────────────────────────────────────────────┐
│  TitleBar + Toolbar (Obfuscate button, Progress, Menu)          │
├──────────────┬──────────────────────────────────────────────────┤
│              │                                                   │
│  Navigation  │  Files Panel (drag-drop zone, file list)         │
│  + Settings  │                                                   │
│              ├──────────────────────────────────────────────────┤
│  - Level     │  Results Panel (tree view: original → obfuscated)│
│  - String    │  Statistics summary                              │
│  - Control   │                                                   │
│  - Symbol    ├──────────────────────────────────────────────────┤
│  - Protect   │  Output Panel (logs with timestamps, colors)     │
│  - Metadata  │                                                   │
│              │                                                   │
└──────────────┴──────────────────────────────────────────────────┘
```

---

## Project Structure

```
Src/Obfy.UI/
├── App.xaml / App.xaml.cs
├── DependencyInjection/
│   └── AppModule.cs
├── Converters/
│   ├── BooleanToVisibilityConverter.cs
│   ├── InverseBooleanConverter.cs
│   ├── LevelToColorConverter.cs
│   └── SymbolTypeToIconConverter.cs
├── Models/
│   ├── AssemblyFile.cs          # File entry with status/progress
│   ├── SymbolTreeNode.cs        # Results tree node
│   └── LogEntry.cs              # Output log entry
├── Services/
│   ├── IFileDialogService.cs
│   ├── FileDialogService.cs
│   ├── ISettingsService.cs
│   └── SettingsService.cs
├── ViewModels/
│   ├── MainViewModel.cs         # Orchestrator
│   ├── SettingsViewModel.cs     # Settings panel
│   ├── FilesViewModel.cs        # Files panel
│   ├── OutputViewModel.cs       # Log panel
│   └── ResultsViewModel.cs      # Results tree
├── Views/
│   ├── MainWindow.xaml
│   ├── Controls/
│   │   ├── SettingsPanel.xaml
│   │   ├── FilesPanel.xaml
│   │   ├── OutputPanel.xaml
│   │   └── ResultsPanel.xaml
│   └── Dialogs/
│       └── AboutWindow.xaml
└── Images/
    ├── app.ico
    └── app.png
```

---

## NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| WPF-UI | 4.1.0 | FluentWindow, SymbolIcon, modern controls |
| WPF-UI.Abstractions | 4.1.0 | Abstractions |
| WPF-UI.DependencyInjection | 4.1.0 | DI integration |
| CommunityToolkit.Mvvm | 8.4.0 | [ObservableProperty], [RelayCommand] |
| Autofac | 9.0.0 | IoC container |
| R3 | 1.3.0 | Reactive extensions |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.135 | XAML behaviors |

---

## Key Features

### 1. Settings Panel (Left)
- **Level selector**: Minimal / Standard / Aggressive / Custom
- **Collapsible Expanders** for each technique:
  - String Encryption (Algorithm, MinLength)
  - Control Flow (Mode, Intensity slider)
  - Symbol Renaming (Mode, toggles for Types/Methods/Fields/etc.)
  - Protection (AntiDebug, AntiTamper, AntiDump)
  - Metadata (RemoveDebugInfo, RemoveAttributes, StripDocs)
  - Resource Encryption (Algorithm, patterns)
- Level presets auto-configure all settings

### 2. Files Panel (Center-Top)
- **Drag-drop zone** with visual overlay
- **File list** showing path, status icon, per-file progress
- **Output directory** selector
- Add/Remove/Clear buttons

### 3. Results Panel (Center-Middle)
- **Statistics summary**: Strings encrypted, Types renamed, etc.
- **Tree view**: Namespace → Type → Method with original → obfuscated names
- **Search/filter** symbols
- **Export symbol map** button

### 4. Output Panel (Bottom)
- **Real-time log** with timestamps
- **Color-coded** by level (Info=blue, Warning=orange, Error=red)
- Auto-scroll toggle, Clear, Copy buttons

---

## Core Integration

### Primary Service
```csharp
IObfuscationService.ObfuscateAsync(inputPath, outputPath, settings, cancellationToken)
```

### Settings Mapping
`SettingsViewModel.ToObfySettings()` converts UI state to `ObfySettings`

### Progress Strategy
- File-level progress: `(completedFiles / totalFiles) * 100%`
- Indeterminate within each file (pipeline doesn't expose granular progress)
- Statistics updated after each file completes

---

## Implementation Order

### Phase 1: Foundation
1. `Obfy.UI.csproj` - project file with packages
2. `App.xaml` / `App.xaml.cs` - theming, DI container setup
3. `DependencyInjection/AppModule.cs` - register all services/VMs

### Phase 2: Infrastructure
4. Converters (BooleanToVisibility, InverseBoolean, LevelToColor)
5. Models (AssemblyFile, SymbolTreeNode, LogEntry)
6. Services (FileDialogService, SettingsService)

### Phase 3: ViewModels
7. `OutputViewModel.cs` - log management
8. `ResultsViewModel.cs` - tree building, stats display
9. `SettingsViewModel.cs` - all obfuscation settings
10. `FilesViewModel.cs` - file management, drag-drop handling
11. `MainViewModel.cs` - orchestrate ObfuscateAsync, cancellation

### Phase 4: Views
12. `OutputPanel.xaml` - log list with colors
13. `ResultsPanel.xaml` - tree view, stats bar
14. `SettingsPanel.xaml` - expanders with all settings
15. `FilesPanel.xaml` - drag-drop, file list
16. `MainWindow.xaml` - three-panel layout, toolbar

### Phase 5: Polish
17. `AboutWindow.xaml`
18. App icon and images
19. Update `Obfy.sln`

---

## Critical Files to Reference

| File | Purpose |
|------|---------|
| `Src/Obfy.Core/Services/IObfuscationService.cs` | Main integration point |
| `Src/Obfy.Core/Models/ObfySettings.cs` | Settings model to bind |
| `Src/Obfy.Core/Models/ObfuscationResult.cs` | Result/statistics |
| `../Pulse/src/Pulse.UI/Views/MainWindow.xaml` | FluentWindow pattern |
| `../Pulse/src/Pulse.UI/ViewModels/MainViewModel.cs` | MVVM pattern |

---

## UI Patterns from Research

Based on commercial tools (Dotfuscator, .NET Reactor, ConfuserEx):

1. **Three-panel layout** - navigation, content, output (like Dotfuscator)
2. **Feature toggles with descriptions** - inline help reduces doc lookups
3. **Hierarchical rule editor** - tree-based exclusion management
4. **Real-time progress** - progress bar + status messages
5. **Results tree** - original → obfuscated name mapping
6. **Preset levels** - quick Normal/Aggressive shortcuts
