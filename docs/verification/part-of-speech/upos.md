## Upos Verification

This document describes the unit-level verification strategy for the `Upos` unit (`Upos`, `UposExtensions`, `UposSet`).

### Verification Approach

The unit is verified by unit tests in `UposTests.cs` that call the public members directly. The unit has no dependencies,
so nothing is mocked or stubbed. The tests check the numbering and order of the 17 tags (because the binary formats depend
on them), the conversion to and from the Universal POS names, and the set operations with their boundary values (the empty
set, the full set, an undefined enumeration value).

### Test Environment

No setup beyond the standard test runner: xUnit v3 on the target frameworks of the test project, no files, no network.

### Acceptance Criteria

All tests pass on every target framework. A test fails when a tag has the wrong number or order, a name does not round
trip, a text that is not an exact upper-case name parses, an undefined value is spelled, or a set operation or its text
form differs from the expected value.

### Test Scenarios

- **Seventeen tags in alphabetical order**: `Upos_Values_AreSeventeenInAlphabeticalOrder` asserts 17 members whose
  numbers are 0 to 16 in alphabetical order of their names.
- **Names round trip**: `Upos_ToTagString_RoundTripsAllTags` converts every tag to its name and back.
- **Unknown text is refused**: `Upos_TryParse_UnknownText_ReturnsFalse` (a theory) asserts `false` for `null`, an empty
  string, a lower-case name, a name with a trailing space, an unknown name and a sentence sentinel.
- **Undefined value cannot be spelled**: `Upos_ToTagString_UndefinedValue_Throws` asserts `ArgumentOutOfRangeException`
  for a number outside 0 to 16.
- **Add and remove**: `UposSet_AddRemove_BehaveAsSet` asserts membership after adding and removing and that the original
  value is unchanged.
- **Union and intersection**: `UposSet_UnionIntersect_ComputeSetResults` asserts the union and intersection of two
  overlapping sets and that the intersection with the empty set is empty.
- **The full set**: `UposSet_All_ContainsSeventeen` asserts 17 members.
- **Equality by content**: `UposSet_Equality_IsByContent` asserts that sets built in different orders are equal.
- **Ascending enumeration**: `UposSet_Enumeration_IsAscending` asserts the order of enumeration.
- **Text form**: `UposSet_ToString_ListsTags` asserts the empty text for the empty set, `NOUN` and `NOUN|VERB`.
