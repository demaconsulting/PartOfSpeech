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
as the wrong defect, compression loses data, accepts a wrong declared size (too large or too small), accepts a Deflate
stream that is cut off (at any point, including only its last byte) or accepts bytes after the Deflate data inside
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
- **Compression**: `BinaryFormat_Deflate_RoundTrips` (round trip, and a wrong declared size is rejected),
  `BinaryFormat_Decompress_TrailingBytesInStoredRegion_Throws` (for repetitive and incompressible data of several
  sizes, the exact region is accepted and one to 9000 extra bytes of several values behind the Deflate data, inside the
  stored region, are rejected as trailing bytes),
  `BinaryFormat_Decompress_CutOffStream_Throws` (for repetitive and incompressible data of several sizes, the region
  without its last byte is rejected with a "cut off" message, and every other cut point from the empty region to the
  middle of the data is rejected with `InvalidDataException`),
  `BinaryFormat_Decompress_DeclaredLengthWrong_Throws` (a declared length too large by 1 or 1000 is "shorter", one too
  small by 1 or 1000 is "longer", for both kinds of data),
  `BinaryFormat_Compress_EmptyPayload_StoresCompleteStream` (an empty payload is a non-empty complete stream that
  round trips, and an empty or one-byte-declared region is rejected) and
  `BinaryFormat_Decompress_GarbageRegion_Throws` (a reserved block type, all ones and random bytes give
  `InvalidDataException` and no other exception type). These tests run on every target framework including .NET
  Framework, which is what makes the read-behavior-based end-of-stream check credible: the inflaters of .NET Framework
  and of the zlib-based runtimes all behave as the design relies on. Non-vacuity was checked by disabling the
  end-of-stream check, which made the cut-off tests of all three units fail.
- **Population count**: `BinaryFormat_PopCount_MatchesReference`.
- **Byte comparison** (no requirement; supports the lexicon search): `BinaryFormat_Compare_OrdersUnsignedBytes`.
