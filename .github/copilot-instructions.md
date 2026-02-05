# Ghidra Analysis Protocol

## MANDATORY: Decompile → Analyze → Rename → Comment (ZERO EXCEPTIONS)

```
✅ decompile → understand → rename function → rename vars → comment
❌ decompile → [anything else] = FAILURE
```

## Core Rules

**Functions**: Default `FUN_xxxxxxxx` = unnamed at 0xaddress. RENAME IMMEDIATELY after understanding.

**Entry Point**: `list_strings(filter="X")` → `get_xrefs_to` → decompile ONE → complete analysis

**Verify Names**: Previous sessions may be wrong. Always verify against code.

**FORBIDDEN**:

- `list_functions()` without filter
- `list_strings()` without filter
- Multiple decompiles before completing analysis

## Post-Decompile Checklist (Complete Before Next Tool Call)

1. **Understand** - What does this do?
2. **Rename Function** - `FUN_xxx` → `Category_Action` (e.g., `GroupDef_Deserialize`)
3. **Rename Variables** - `param_1, local_10` → meaningful names via `mcp_ghidra_rename_variable`
4. **Comment** (min 3) - Field reads, validations, calls via `set_decompiler_comment`
5. **Define Structures** - If `ptr[0], ptr[1], ptr[2]` patterns exist
6. **Update Signature** - If needed via `mcp_ghidra_set_function_prototype`

## Quick Workflows

**String search**: `list_strings(filter="X")` → xrefs → decompile → checklist  
**Address known**: `decompile_function_by_address` → checklist  
**Starting cold**: Search magic bytes/extensions/errors/type names

## Success Pattern

```
list_strings(filter="GroupDef") → get_xrefs_to → decompile_function_by_address
→ READ CODE → rename_function → rename_variables (3x) → comment (3x)
→ Report: "GroupDef_Deserialize at 0x1404d05c0: reads pchName (Pascal), flags (uint32, must be 0x03)"
```

## STO Context

**Target**: Cryptic Studios GameClient.exe (x64)  
**Formats**: Pascal strings (uint32 len + UTF-8), count-prefixed arrays  
**Key Addresses**: Parser_RegisterType @ 0x1405416c0, Parser_FindColumn @ 0x140539d20

**Workflow**: Extract → string search → xrefs → decompile → RENAME → comment fields → verify hex

**Principle**: Surgical (5-10 calls), not exploratory. Analyze thoroughly once.
