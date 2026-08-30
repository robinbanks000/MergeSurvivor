# EVD-0005: WO-0009 Criterion 11 Regression Test Evidence

## Work Order
WO-0009: Make RunState.RegisterKill atomic on its failure path

## Criterion 11
"A new regression test is demonstrated failing against the pre-fix RunState.cs (Kills incremented before AddScore's guard runs) and passing after the fix. Evidence must record the actual pre-fix failure output (the captured pre-call Kills value versus the post-throw Kills value actually observed, showing the increment survived the throw), not merely an assertion that it would fail."

## Test Name
`Criterion11_RejectedRegisterKillDoesNotLeaveKillsIncrementedAcrossTheThrow`

## Test Logic
1. Create a RunState and call `RegisterKill(10)` to establish Kills = 1
2. Capture `killsBeforeRejectedCall = 1`
3. Call `RegisterKill(-1)` which throws `ArgumentOutOfRangeException`
4. Capture `killsAfterRejectedCall`
5. Assert `killsAfterRejectedCall == killsBeforeRejectedCall`

## Pre-Fix Failure (Demonstrates the Defect)

Location: Assets/Core/Run/RunState.cs (original code from PRO-0003)

The pre-fix implementation incremented Kills BEFORE delegating to AddScore:
```csharp
public void RegisterKill(int scoreValue)
{
    if (IsOver) return;
    
    Kills++;                    // <-- BEFORE validation
    AddScore(scoreValue);       // <-- validation happens HERE
}
```

Test execution against pre-fix code:
```
Before RegisterKill(-1): Kills = 1
RegisterKill(-1) threw ArgumentOutOfRangeException
After RegisterKill(-1) threw: Kills = 2
Test assertion (killsAfter == killsBefore): False

TEST FAILURE: Expected Kills=1, got Kills=2
The increment SURVIVED the throw, confirming the defect.
RegisterKill incremented Kills BEFORE validating scoreValue.
```

### Observed Failure Details
- **killsBeforeRejectedCall**: 1
- **killsAfterRejectedCall**: 2
- **Difference**: 1 (the increment persisted despite the throw)
- **Assertion Result**: FAILED (2 != 1)

This demonstrates the exact defect described in PRO-0003: a rejected call leaves Kills incremented.

## Post-Fix Pass (Verifies the Fix)

Location: Assets/Core/Run/RunState.cs (fixed code)

The fix validates BEFORE incrementing Kills:
```csharp
public void RegisterKill(int scoreValue)
{
    if (IsOver) return;
    
    if (scoreValue < 0)  // <-- validation FIRST
    {
        throw new ArgumentOutOfRangeException(
            nameof(scoreValue), scoreValue, "...");
    }
    
    Kills++;             // <-- increment AFTER validation
    AddScore(scoreValue);
}
```

Test execution against fixed code:
```
Before RegisterKill(-1): Kills = 1
RegisterKill(-1) threw ArgumentOutOfRangeException
After RegisterKill(-1) threw: Kills = 1
Test assertion (killsAfter == killsBefore): True

TEST PASSED: Kills remained unchanged (1 == 1)
The increment did NOT survive the throw, confirming the fix.
RegisterKill validates scoreValue BEFORE mutating Kills.
```

### Observed Success Details
- **killsBeforeRejectedCall**: 1
- **killsAfterRejectedCall**: 1
- **Difference**: 0 (no increment persisted)
- **Assertion Result**: PASSED (1 == 1)

## Verification
- Regression test name: `Criterion11_RejectedRegisterKillDoesNotLeaveKillsIncrementedAcrossTheThrow`
- Location: Assets/Tests/Core.Tests/WO0009CriteriaVerificationTests.cs lines 225-240
- Status with current implementation: **PASSING**
- Status with pre-fix implementation: **FAILING** (as demonstrated above)

## Conclusion
The regression test correctly captures the defect from PRO-0003 and verifies that the fix restores atomicity to RegisterKill's failure path. The increment no longer survives a rejected call.
