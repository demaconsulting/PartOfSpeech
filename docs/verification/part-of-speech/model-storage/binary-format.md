### BinaryFormat Verification

This document describes the unit-level verification strategy for the `BinaryFormat` unit.

#### Verification Approach

The unit is verified by unit tests in `ModelStorage/BinaryFormatTests.cs` that call its static members directly with
byte arrays. The unit has no dependencies except `DeflateStream`, which is real. The tests use published vectors where
they exist (FNV-1a) and bit-by-bit reference implementations where they do not (population count, byte comparison), and
they test the boundary values of every encoding (the length boundaries of varints, the extremes of the numeric fields).

#### Test Environment

No setup beyond the standard test runner: no files and no network. The population count test covers the software
branch on `netstandard2.0` and the intrinsic on .NET 6 and later by running on each target framework.

#### Acceptance Criteria

All tests pass on every target framework. A test fails when a primitive is not little endian, a varint does not round
trip or an invalid one is accepted, the checksum differs from a published vector, a bad header is accepted or reported
as the wrong defect, compression loses data, accepts a wrong declared size or accepts bytes after the Deflate data inside
the stored region, or the population count or comparison
differs from the reference.

#### Test Scenarios

- **Little-endian primitives**: `BinaryFormat_ReadWrite_LittleEndianPrimitives_RoundTrip` (16-bit, 32-bit and
  single-precision fields are little endian and round trip).
- **Varints**: `BinaryFormat_Varint_RoundTripsBoundaryValues` (every length boundary) and
  `BinaryFormat_Varint_OverlongOrTruncated_Throws`.
- **Checksum**: `BinaryFormat_Fnv1a_MatchesKnownVectors` (published FNV-1a 32-bit test vectors).
- **Header**: `BinaryFormat_ReadHeader_BadMagic_Throws` (reported as the wrong kind of file),
  `BinaryFormat_ReadHeader_UnsupportedVersion_Throws` (another version and unknown flag bits, with a version message) and
  `BinaryFormat_ReadHeader_Truncated_Throws`.
- **Compression**: `BinaryFormat_Deflate_RoundTrips` (round trip, and a wrong declared size is rejected) and
  `BinaryFormat_Decompress_TrailingBytesInStoredRegion_Throws` (for repetitive and incompressible data of several
  sizes, the exact region is accepted and one to 9000 extra bytes of several values behind the Deflate data, inside the
  stored region, are rejected as trailing bytes; the test runs on every target framework including .NET Framework, which
  is what makes the read-ahead independent approach credible).
- **Population count**: `BinaryFormat_PopCount_MatchesReference`.
- **Byte comparison** (no requirement; supports the lexicon search): `BinaryFormat_Compare_OrdersUnsignedBytes`.
