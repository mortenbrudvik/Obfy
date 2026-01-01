# Obfy Product Roadmap & Backlog

A strategic development plan based on competitive analysis against commercial and open-source .NET obfuscators.

---

## Vision

**Make Obfy the go-to open-source .NET obfuscator** by closing the feature gap with commercial tools while maintaining simplicity, modern .NET support, and excellent developer experience.

**Target:** Cover 90% of typical obfuscation needs (currently at ~70%)

---

## Product Backlog

### Priority Definitions

| Priority | Definition | Target |
|----------|------------|--------|
| **P0** | Critical - Core competitive features | Next release |
| **P1** | High - Strong user demand | Near-term |
| **P2** | Medium - Nice to have | Mid-term |
| **P3** | Low - Future consideration | Long-term |

---

### Protection Features

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| PF-01 | Resource Encryption | P0 | Low | Medium | ✅ Done |
| PF-02 | Constant Encryption | P0 | Low | Medium | ✅ Done |
| PF-03 | Anti-Tamper Detection | P1 | Medium | High | ✅ Done |
| PF-04 | Anti-Decompiler | P1 | Medium | Medium | ✅ Done |
| PF-05 | Anti-Dump Protection | P2 | Medium | Medium | Backlog |
| PF-06 | Watermarking | P2 | Low | Low | Backlog |
| PF-07 | MSIL Encryption | P2 | High | High | Backlog |
| PF-08 | Code Virtualization | P3 | Very High | High | Future |
| PF-09 | Native Code Generation | P3 | Very High | Medium | Future |

#### Feature Details

**PF-01: Resource Encryption**
- Encrypt embedded resources (images, configs, data files)
- Decrypt at runtime on first access
- Support for selective resource encryption via patterns
- *Competitive parity: All commercial tools have this*

**PF-02: Constant Encryption**
- Encrypt numeric literals (int, long, float, double)
- Replace with decryption calls at runtime
- Configurable via settings (enable/disable per type)
- *Closes gap with: .NET Reactor, Babel, Eazfuscator*

**PF-03: Anti-Tamper Detection**
- Compute hash of critical assembly sections
- Verify integrity at runtime
- Configurable response (exit, exception, custom handler)
- *Closes gap with: Dotfuscator, SmartAssembly, .NET Reactor, Babel*

**PF-04: Anti-Decompiler**
- Insert invalid metadata that breaks decompilers
- Add junk methods/types that confuse analysis
- Target: dnSpy, ILSpy, dotPeek
- *Inspired by: BitMono's approach*

**PF-05: Anti-Dump Protection**
- Detect memory dumping attempts
- Clear sensitive data from memory after use
- Hook common dump techniques
- *Closes gap with: .NET Reactor, Babel*

**PF-06: Watermarking**
- Embed invisible tracking data in assemblies
- Unique identifier per build/customer
- Useful for license tracking and leak detection
- *Closes gap with: Dotfuscator, .NET Reactor*

**PF-07: MSIL Encryption**
- Encrypt method bodies
- Decrypt on first execution
- Store encrypted IL in resources or custom sections
- *Closes gap with: .NET Reactor, Babel*

**PF-08: Code Virtualization**
- Convert IL to custom virtual machine bytecode
- Execute via embedded VM at runtime
- Major undertaking - research phase first
- *Closes gap with: .NET Reactor, Babel, Eazfuscator*

**PF-09: Native Code Generation**
- Generate native launcher/loader
- Load managed assembly from encrypted payload
- Platform-specific (Windows initially)
- *Unique to: .NET Reactor*

---

### Developer Experience

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| DX-01 | WPF Desktop Application | P1 | Medium | High | ✅ Done |
| DX-02 | Visual Studio Extension | P1 | Medium | High | Planned |
| DX-03 | VS Code Extension | P2 | Low | Medium | Backlog |
| DX-04 | JetBrains Rider Plugin | P2 | Low | Medium | Backlog |
| DX-05 | Configuration Wizard | P2 | Low | Medium | Backlog |
| DX-06 | Real-time Preview | P3 | High | Medium | Future |

#### Feature Details

**DX-01: WPF Desktop Application** ✅
- Windows desktop application with Fluent Design (WPF-UI)
- Visual configuration editor with level presets
- Drag-drop file selection
- Real-time progress and color-coded output logs
- Export obfuscation reports (HTML/JSON)
- *Why WPF: Windows-first approach, mature ecosystem, Fluent Design support*

**DX-02: Visual Studio Extension**
- Right-click "Obfuscate" in Solution Explorer
- Build integration (post-build obfuscation)
- Configuration UI in project properties
- Output window logging
- *High demand from enterprise users*

**DX-03: VS Code Extension**
- Task integration for obfuscation
- Configuration file intellisense (obfy.json schema)
- Problem matcher for obfuscation errors
- Command palette integration

**DX-04: JetBrains Rider Plugin**
- Similar to VS extension functionality
- Run configuration integration
- Tool window for obfuscation output

**DX-05: Configuration Wizard**
- Interactive CLI wizard for generating config
- Questions about use case, platform, protection level
- Generates optimized obfy.json
- `obfy config wizard` command

---

### Platform Support

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| PS-01 | Unity Support | P2 | Medium | High | Backlog |
| PS-02 | Xamarin/MAUI Support | P2 | Medium | Medium | Backlog |
| PS-03 | Blazor WebAssembly | P2 | Low | Medium | Backlog |
| PS-04 | AOT Compilation | P2 | High | Medium | Backlog |

#### Feature Details

**PS-01: Unity Support**
- Handle Unity's IL2CPP and Mono backends
- Compatible with Unity's assembly structure
- Test with common Unity projects
- Documentation for Unity integration
- *High demand: Unity devs need protection*

**PS-02: Xamarin/MAUI Support**
- Test with Android and iOS targets
- Handle platform-specific assemblies
- Linker compatibility
- *Moderate demand for mobile apps*

**PS-03: Blazor WebAssembly Support**
- Obfuscate Blazor WASM assemblies
- Maintain runtime compatibility
- Test with common Blazor patterns

**PS-04: AOT Compilation Compatibility**
- Ensure obfuscated code works with NativeAOT
- Handle AOT-specific metadata requirements
- Test with `PublishAot=true`

---

### Utility Features

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| UF-01 | Assembly Merging | P1 | Medium | Medium | Planned |
| UF-02 | Dependency Embedding | P2 | Medium | Medium | Backlog |
| UF-03 | Assembly Signing | P2 | Low | Medium | Backlog |
| UF-04 | Obfuscation Report | P1 | Low | Medium | ✅ Done |
| UF-05 | Incremental Obfuscation | P3 | High | Medium | Future |

#### Feature Details

**UF-01: Assembly Merging**
- Merge multiple assemblies into one
- ILMerge/ILRepack-style functionality
- Configurable via CLI and config file
- *Closes gap with: SmartAssembly, .NET Reactor*

**UF-02: Dependency Embedding**
- Embed dependencies as resources
- Load at runtime via custom resolver
- Reduce distribution footprint
- *Closes gap with: SmartAssembly, .NET Reactor*

**UF-03: Assembly Signing**
- Re-sign assembly after obfuscation
- Support for strong naming
- SNK file configuration

**UF-04: Obfuscation Report**
- Generate detailed HTML/JSON report
- Statistics: symbols renamed, strings encrypted, etc.
- Before/after size comparison
- Warnings and recommendations

**UF-05: Incremental Obfuscation**
- Cache obfuscation results
- Only re-obfuscate changed assemblies
- Speed up CI/CD pipelines

---

## Roadmap

### Phase 1: Foundation Enhancement (v1.1) ✅
**Theme:** Quick wins to close obvious gaps

| Feature | Type | Notes | Status |
|---------|------|-------|--------|
| Resource Encryption (PF-01) | Protection | Encrypt embedded resources | ✅ Done |
| Constant Encryption (PF-02) | Protection | Encrypt numeric literals | ✅ Done |
| Obfuscation Report (UF-04) | Utility | Detailed statistics output | ✅ Done |
| WPF Desktop Application (DX-01) | Tooling | Visual configuration UI | ✅ Done |

**Success Metrics:**
- All commercial competitors' basic features covered
- 80% feature parity with Obfuscar + more

**Completed:** January 2026

---

### Phase 2: Protection Plus (v1.2)
**Theme:** Stronger runtime protection

| Feature | Type | Notes | Status |
|---------|------|-------|--------|
| Anti-Tamper Detection (PF-03) | Protection | Runtime integrity checks | ✅ Done |
| Anti-Decompiler (PF-04) | Protection | Break decompiler tools | ✅ Done |
| Assembly Merging (UF-01) | Utility | Merge multiple DLLs | Planned |

**Success Metrics:**
- Protection comparable to budget commercial tools
- Decompilers fail on obfuscated output

**Target:** Q2 2026

---

### Phase 3: Developer Experience (v1.3)
**Theme:** IDE integration and tooling

| Feature | Type | Notes |
|---------|------|-------|
| Visual Studio Extension (DX-02) | Tooling | IDE integration |
| Configuration Wizard (DX-05) | Tooling | Interactive setup |

**Success Metrics:**
- Visual Studio market presence
- Streamlined configuration experience

**Target:** Q3 2026

*Note: Desktop UI (DX-01) was completed ahead of schedule in v1.1*

---

### Phase 4: Platform Expansion (v1.4)
**Theme:** Broaden platform support

| Feature | Type | Notes |
|---------|------|-------|
| Unity Support (PS-01) | Platform | Game developer market |
| VS Code Extension (DX-03) | Tooling | Modern editor support |
| Dependency Embedding (UF-02) | Utility | Single-file deployment |

**Success Metrics:**
- Unity developers can use Obfy
- Cross-editor support complete

**Target:** Q4 2026

---

### Phase 5: Advanced Protection (v2.0)
**Theme:** Premium-tier features

| Feature | Type | Notes |
|---------|------|-------|
| MSIL Encryption (PF-07) | Protection | Encrypted method bodies |
| Anti-Dump (PF-05) | Protection | Memory protection |
| Watermarking (PF-06) | Protection | Build tracking |

**Success Metrics:**
- Feature parity with mid-tier commercial tools
- Resistance to common deobfuscation tools

**Target:** 2027

---

### Future Consideration (v3.0+)
**Theme:** Enterprise-grade features

| Feature | Type | Notes |
|---------|------|-------|
| Code Virtualization (PF-08) | Protection | Custom VM |
| Native Code Generation (PF-09) | Protection | Native launchers |
| Incremental Obfuscation (UF-05) | Performance | Build caching |

**Notes:**
- Virtualization requires significant R&D
- Native code is platform-specific
- Consider community demand before investing

---

## Dependencies & Technical Considerations

### Library Dependencies

| Feature | Depends On | Notes |
|---------|------------|-------|
| Resource Encryption | dnlib | Modify embedded resources |
| Constant Encryption | dnlib | IL instruction rewriting |
| Anti-Tamper | dnlib | Hash computation injection |
| Anti-Decompiler | dnlib | Junk type/method injection |
| Assembly Merging | ILRepack or custom | Consider existing libraries |
| GUI | WPF-UI | Windows Fluent Design framework (completed) |
| VS Extension | VSIX SDK | Visual Studio extensibility |

### Technical Risks

| Risk | Mitigation |
|------|------------|
| Virtualization complexity | Start with research spike; may defer to v3.0+ |
| Unity compatibility | Partner with Unity developers for testing |
| Native code maintenance | Limit to Windows initially |
| AOT breaking changes | Continuous testing with .NET previews |

---

## Success Metrics by Phase

| Phase | KPI | Target |
|-------|-----|--------|
| v1.1 | Feature coverage | 75% of commercial tools |
| v1.2 | Decompiler resistance | Break dnSpy, ILSpy |
| v1.3 | Non-CLI adoption | 30% users via GUI/VS |
| v1.4 | Platform coverage | Unity, VS Code working |
| v2.0 | Commercial parity | Match .NET Reactor core |

---

## Community Input

### Requested Features (Prioritize Based on Demand)

Track feature requests via GitHub Issues. Consider:
- Number of upvotes/reactions
- Use case validity
- Implementation feasibility
- Alignment with roadmap

### Contribution Opportunities

| Feature | Good for Contributors | Notes |
|---------|----------------------|-------|
| VS Code Extension | Yes | Standalone project |
| Rider Plugin | Yes | Standalone project |
| Documentation | Yes | Always welcome |
| Test Cases | Yes | Critical for stability |
| Resource Encryption | Maybe | Core feature, needs review |
| Virtualization | No | Complex, needs architecture |

---

## Changelog Alignment

When releasing features, update:
1. `CHANGELOG.md` - User-facing changes
2. `docs/Techniques.md` - New protection documentation
3. `docs/Configuration.md` - New settings
4. `docs/CLI.md` - New commands/options
5. `README.md` - Feature highlights

---

## Review Schedule

- **Monthly:** Review backlog priorities based on feedback
- **Quarterly:** Assess roadmap progress, adjust timelines
- **Per Release:** Retrospective on delivered features

---

*Last updated: January 2026*
*Based on: Competitive Analysis v1.0*
