# BUG-049: Script priority list parsing crashes on malformed data
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review — SKO-Nanobot-BuildAndRepair-System-Script/Script.cs

## Description

`WeldPriorityList()` (line 1122), `GrindPriorityList()` (line 1202), and `ComponentClassList()` (line 1282) split the mod-returned string on `';'` and immediately access `values[1]` without checking the array length.

```csharp
var values = item.Split(';');
if (Enum.TryParse<BlockClass>(values[0], out blockClass) &&
   bool.TryParse(values[1], out enabled))  // IndexOutOfRangeException if no ';'
```

If the mod returns a string without a semicolon (version mismatch, corrupted data, empty string), `values[1]` throws `IndexOutOfRangeException` and the PB script crashes.

## Fix

Add length check before accessing `values[1]`:
```csharp
var values = item.Split(';');
if (values.Length >= 2 &&
    Enum.TryParse<BlockClass>(values[0], out blockClass) &&
    bool.TryParse(values[1], out enabled))
```

Apply at all three locations (lines 1122, 1202, 1282).
