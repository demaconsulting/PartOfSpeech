### PerceptronModel Verification

This document describes the unit-level verification strategy for the `PerceptronModel` unit (reader, scoring and the
writer).

#### Verification Approach

The unit is verified by unit tests in `ModelStorage/PerceptronModelTests.cs`. Weights files are produced by the real
`PerceptronModelWriter` from hand-set rows (so that the writer and the reader are verified against each other and every
expected score is known), and corrupted variants are produced by changing bytes, then correcting the checksum where the
test must prove that a structural rule, not only the checksum, is enforced. Scores are checked through `AddScores` with
bucket numbers chosen at the boundaries of the occupancy bit set (first and last bucket, buckets in the same 64-bit word
as an occupied one). Nothing is mocked.

#### Test Environment

No setup beyond the standard test runner: no files and no network.

#### Acceptance Criteria

All tests pass. A test fails when a written model does not load with its header values and sizes, the Deflate and raw
forms differ, a weight is added to the wrong tag or bucket, a bucket without a row adds anything, or any invalid file (of
the kinds listed below) is accepted.

#### Test Scenarios

- **Round trip**: `PerceptronModel_Load_RoundTripsWriterOutput` and `PerceptronModel_Load_Compressed_And_Uncompressed_Equal`.
- **Score lookup**: `PerceptronModel_AddScores_SumsRowWeights` (weights of a row go to the matching tags and accumulate
  over buckets), `PerceptronModel_AddScores_EmptyBucket_AddsNothing` and
  `PerceptronModel_AddScores_FirstAndLastBucket_AreFound`.
- **Temperature**: `PerceptronModel_Temperature_IsReadFromHeader` (exposed unchanged).
- **Validation**: `PerceptronModel_Load_BadMagic_Throws`, `PerceptronModel_Load_UnsupportedVersion_Throws` (the message
  has the number), `PerceptronModel_Load_FeatureSetMismatch_Throws`, `PerceptronModel_Load_InvalidTemperatureOrScale_Throws`
  (on load and on write), `PerceptronModel_Load_ChecksumMismatch_Throws` (also in a Deflate file),
  `PerceptronModel_Load_TruncatedOrTrailingBytes_Throws`, `PerceptronModel_Load_UnsortedTagsOrBuckets_Throws` (with a valid
  checksum), `PerceptronModel_Load_ReservedNotZero_Throws` (and unknown flag bits),
  `PerceptronModel_Load_PayloadLengthAboveMaximum_Throws` (a declared payload length above 256 MiB is refused before
  inflation) and
  `PerceptronModelWriter_Compile_InvalidRows_Throw` (wrong vector length, the weight -32768 and a repeated bucket).
