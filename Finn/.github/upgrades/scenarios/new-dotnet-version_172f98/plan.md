# .NET 10 Upgrade Plan

## Table of Contents

- [Executive Summary](#executive-summary)
- [Migration Strategy](#migration-strategy)
- [Detailed Dependency Analysis](#detailed-dependency-analysis)
- [Project-by-Project Plans](#project-by-project-plans)
- [Package Update Reference](#package-update-reference)
- [Breaking Changes Catalog](#breaking-changes-catalog)
- [Risk Management](#risk-management)
- [Complexity and Effort Assessment](#complexity-and-effort-assessment)
- [Testing and Validation Strategy](#testing-and-validation-strategy)
- [Source Control Strategy](#source-control-strategy)
- [Success Criteria](#success-criteria)

---

## Executive Summary

### Scenario Description
Upgrade the Finn solution from .NET 8.0 to .NET 10.0 (Long Term Support).

### Scope
**Projects Affected**: 1 of 2 projects requires upgrade
- **Finn.csproj**: Already on net10.0 - no changes needed
- **Finn.Desktop.csproj**: net8.0 to net10.0 (desktop application with Avalonia UI)

**Current State**:
- Total Projects: 2
- Total NuGet Packages: 17
- Total Lines of Code: 7,277
- Dependency Structure: Single-level (Finn.Desktop depends on Finn)

### Target State
- All projects targeting .NET 10.0
- All packages compatible with .NET 10.0
- iTextSharp package incompatibility resolved

### Selected Strategy
**All-At-Once Strategy** - Single atomic upgrade of Finn.Desktop.csproj.

### Complexity Assessment
**Classification: Simple** - Minimal scope, straightforward changes, low risk.

---

## Migration Strategy

### Approach: All-At-Once (Atomic)

Since only Finn.Desktop.csproj needs upgrading and its sole dependency (Finn.csproj) is already on net10.0, this is a single atomic operation:

1. Update TargetFramework in Finn.Desktop.csproj (net8.0 to net10.0)
2. Resolve iTextSharp package incompatibility
3. Restore, build, and validate

### Why Not Incremental
- Only 1 project actually needs changes
- No complex dependency cycles
- No high-risk projects requiring isolation

---

## Detailed Dependency Analysis

### Dependency Graph

```
Finn.Desktop.csproj (net8.0 - needs upgrade)
    |__ Finn.csproj (net10.0 - already upgraded)
```

### Key Observations
- No circular dependencies
- Finn.csproj is the leaf node, already on target framework
- Finn.Desktop.csproj is the root application, sole project needing changes
- Upgrade order is trivial: Finn.Desktop is the only action item

---

## Project-by-Project Plans

### Finn.csproj

| Property | Value |
|----------|-------|
| Target Framework | net10.0 |
| Project Type | Class Library |
| Lines of Code | 7,254 |
| NuGet Packages | 15 (all compatible) |
| Action Required | **None** |

Already upgraded. Serves as dependency for Finn.Desktop.

---

### Finn.Desktop.csproj

| Property | Value |
|----------|-------|
| Current Target Framework | net8.0 |
| Target Framework | net10.0 |
| Project Type | Desktop Application (Avalonia) |
| Lines of Code | 23 |
| NuGet Packages | 3 |
| Action Required | **Framework update + package fix** |

#### Step 1: Update Target Framework

**File**: Finn.Desktop/Finn.Desktop.csproj

```xml
<!-- Before -->
<TargetFramework>net8.0</TargetFramework>

<!-- After -->
<TargetFramework>net10.0</TargetFramework>
```

#### Step 2: Resolve iTextSharp Incompatibility

**Issue**: iTextSharp 5.5.13.4 is incompatible with .NET 10.0

**Recommended Action**: Remove iTextSharp from Finn.Desktop.csproj

```xml
<!-- Remove this line -->
<PackageReference Include="iTextSharp" Version="5.5.13.4" />
```

**Rationale**:
- Finn.Desktop already references Finn.csproj, which uses modern iText 9.5.0
- iTextSharp (5.x) is a legacy version; the modern replacement is already available transitively
- Removing eliminates the incompatibility without losing functionality

**Contingency** (if iTextSharp is directly used in Finn.Desktop code):
- Migrate code from iTextSharp.text namespaces to iText.Kernel / iText.Layout
- The modern iText 9.5.0 API is already available via Finn.csproj dependency

#### Step 3: Build and Validate

1. Restore NuGet packages
2. Build solution
3. Verify 0 errors, 0 warnings
4. Launch application and verify startup

#### Expected Code Changes: None

Assessment results show:
- 0 binary incompatible APIs
- 0 source incompatible APIs
- 0 behavioral changes
- 14 compatible APIs

---

## Package Update Reference

### Finn.Desktop.csproj Packages

| Package | Current | Action | Target | Reason |
|---------|---------|--------|--------|--------|
| Avalonia.Controls.DataGrid | 11.3.11 | Keep | 11.3.11 | Compatible |
| Avalonia.Desktop | 11.3.11 | Keep | 11.3.11 | Compatible |
| iTextSharp | 5.5.13.4 | **Remove** | N/A | Incompatible with .NET 10 |

### Finn.csproj Packages
All 15 packages are already compatible with .NET 10.0. No changes needed.

---

## Breaking Changes Catalog

**No breaking changes identified.**

The assessment found 0 binary-incompatible, 0 source-incompatible, and 0 behavioral changes across both projects. The only issue is a package-level incompatibility (iTextSharp), not an API breaking change.

---

## Risk Management

### Overall Risk Level: Low

| Risk Factor | Level | Description | Mitigation |
|-------------|-------|-------------|------------|
| Solution Size | Low | 2 projects, 7,277 LOC | Minimal surface area |
| Dependency Complexity | Low | Linear chain, no cycles | Trivial upgrade order |
| API Breaking Changes | Low | 0 identified | No code modifications needed |
| Package Compatibility | Medium | 1 incompatible package | Remove redundant dependency |
| Test Coverage | Unknown | No test projects found | Manual validation required |
| Framework Jump | Low | .NET 8 to 10 (LTS to LTS) | Well-supported path |

### Contingency Plans

**If build fails**: Check Avalonia 11.3.11 compatibility; consult .NET 10 breaking changes docs; revert to net8.0.

**If iTextSharp removal breaks code**: Identify usage, migrate to iText 9.5.0 API (already available via Finn.csproj).

**If runtime issues appear**: Review .NET 10 behavioral changes; validate COM interop; consider Avalonia package update.

---

## Complexity and Effort Assessment

**Overall: Low**

| Project | Complexity | Effort |
|---------|-----------|--------|
| Finn.csproj | N/A | Already upgraded |
| Finn.Desktop.csproj | Low | TFM change + package removal |

---

## Testing and Validation Strategy

### Build Verification
- Solution restores without errors
- Solution builds with 0 errors
- No package dependency conflicts

### Functional Testing (Manual)
- Application launches successfully
- Main window renders correctly
- Avalonia UI controls function properly
- PDF functionality works (via Finn.csproj iText 9.5.0)
- No runtime exceptions on startup

### Rollback Criteria
- Build fails with unresolvable errors
- Critical runtime failures
- Loss of core functionality

---

## Source Control Strategy

**Single Commit**: All changes in one atomic commit with message: Upgrade Finn.Desktop to .NET 10.0

---

## Success Criteria

- All projects target .NET 10.0
- Solution builds with 0 errors
- No package compatibility issues
- No security vulnerabilities
- Application starts and runs correctly
- Core functionality validated
