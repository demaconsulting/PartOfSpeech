### TokenConstraint Verification

This document describes the unit-level verification strategy for the `TokenConstraint` unit.

#### Verification Approach

The unit is verified by unit tests in `Tagging/TokenConstraintTests.cs` that call the public factories and operators
directly. The struct has no dependencies except `UposSet`, which is real, so nothing is mocked. The effect of a constraint
on tags is verified in the `TagDecoder` unit tests and in the Tagging and system tests; the tests here prove the value
semantics: creation rules, neutrality of the default value, combination, immutability and equality.

#### Test Environment

No setup beyond the standard test runner: no files and no network.

#### Acceptance Criteria

All tests pass. A test fails when an invalid constraint is created, the default value is not neutral, a combination
does not intersect the allowed sets and add the adjustments, a contradictory or overflowing combination is accepted,
a combination that adjusts nothing is reported as a constraint, a created
constraint changes when its source changes, or equal constraints are reported as different.

#### Test Scenarios

- **Factories refuse invalid input**: `TokenConstraint_Only_EmptySet_Throws` (an empty allowed set and invalid boosts or
  penalties) and `TokenConstraint_Prefer_NonFiniteOrNonPositive_Throws` (a boost or penalty must be finite and positive,
  for a defined tag).
- **None**: `TokenConstraint_Default_IsNone` (the default value restricts nothing and adjusts nothing).
- **Combination**: `TokenConstraint_And_IntersectsAndSumsBoosts` (intersects the allowed sets, adds the adjustments, and
  rejects disjoint sets), `TokenConstraint_And_CancelingAdjustments_EqualsNone` (a boost and a penalty that cancel
  combine to a constraint that `IsNone`, equals `None` with the same hash code and formats as `None`) and
  `TokenConstraint_And_NonFiniteSum_Throws` (two boosts of `double.MaxValue` on one tag overflow and are rejected; the
  same value on different tags, a single factory call and a cancelling pair are accepted).
- **Immutability**: `TokenConstraint_Boosts_AreCopied` (combining creates new adjustment storage, so earlier constraints
  never change).
- **Equality**: `TokenConstraint_Equals_ByContent` (equality is by content and equal constraints have equal hash codes)
  and `TokenConstraint_And_CancelingAdjustments_EqualsNone` (all observations agree on a canceled combination).
