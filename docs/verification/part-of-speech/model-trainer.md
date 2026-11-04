## ModelTrainer Verification

This document describes the unit-level verification strategy for the `ModelTrainer` unit (the tool project
`tools/DemaConsulting.PartOfSpeech.Trainer`: its training engine, its evaluation and its check logic).

### Verification Approach

The unit is verified by unit tests in `ModelTrainer/ModelTrainerTests.cs` and `ModelTrainer/HeldOutEvaluationTests.cs` (the
held-out gate, the confidence curve and the held-out evaluation). The engine files of the tool (everything under
`Training/` and `Evaluation/` and `RunLog.cs`) use only the base class library and are compiled into the test project by
source links, so the tests run the real trainer code on every target framework of the test project, including .NET
Framework; only `Program.cs` (the console) is not linked, and `ConfigFile.cs` and `OperatingPointsFile.cs`
(System.Text.Json, which .NET Framework does not have in the box) are linked and tested on .NET 8 and later. Nothing
is mocked. The tests use four kinds of method:

- **Small real runs.** The trainer is run for real on 300 sentences of the open training corpus (three epochs, 18
  hash bits, the smallest bucket space the format allows), which takes a fraction of a second. Two runs with the same
  inputs must give the same uncompressed image, another seed another image, and the SHA-256 of the image of a reference
  run (once without and once with the lexicon restriction) is pinned in the test as a cross-platform golden value: the
  test runs on every target framework and, in the CI matrix, on Windows, Linux and macOS, so the same value is verified
  on every system (this is the evidence of the determinism contract of the design). If the extractor, the lexicon or the
  open training corpus changes, the pinned values must be updated; a
  failing test prints the actual hash.
- **Parity with the runtime.** The trainer's predictor is compared token by token with the real `TagDecoder` for the same
  quantized weights, once restricted with the embedded lexicon and once unrestricted with unknown words, so that the
  trainer cannot train for conditions that the tagger does not have.
- **Oracles and fixtures.** The integer averaging and quantization is compared with a `decimal` oracle on random update
  sequences and on hand-made ties and clamps; the corpus reader, the masks, the development-set parser, the metrics and
  the bootstrap are checked against small fixtures with known answers; the pseudo-random generator is checked against its
  published reference sequence.
- **Guards by scanning.** The rule that no trainer source other than the guard names a held-out data set is verified by
  reading the C# files of the tool and asserting that none of them contains the names.
- **Command-line safety.** The rule that an experiment (overrides or calibration) must name an output file is code in
  `OutputGuard`, which is linked into the test project; the tests call it directly with and without an output.
- **Held-out evaluation on fixtures only.** Every code path of the held-out evaluation (the gate, the guarded readers, the
  parsers, the tallies, the curve, the rule, the bootstrap, the judging, the report writers and the stop conditions) is
  tested with tiny fixtures: a temporary fake repository with a manifest for the guarded reader, hand-written probe and
  treebank rows, hand-computed outcomes for the curve and the rule, and the tiny trained model for the judging. No test,
  build step or CI job reads a real held-out file through this code, so CI never looks at held-out data. The real held-out
  run is a documented, once-only procedure (the protocol section of the run log), not a test: it would be wrong for a
  test to run it, because its value is that it happens once, after the selection is committed. Its output is a committed,
  generated report; the system-level tests that tie it to the shipped model and to the selection accompany the report.

The development evaluations and the training of the shipped model are not unit tests (they would take much longer and
depend on the data): the shipped weights are verified at system level (the embedded weights match `training.json`, the
configuration names only committed training corpora) and by the build step `train --check`, which retrains the model from
the committed corpora and compares it with the committed file in the CI build on Windows, Linux and macOS (every system
against the same pinned hash). That step is supplementary
evidence, since a build step is not a test; the tests prove that the comparison logic is right.

### Test Environment

No setup beyond the standard test runner. The tests read the committed corpus `data/open/train.tsv` and the embedded
lexicon (the small runs), the committed `training.json` (on .NET 8 and later) and the C# sources of the tool (the guard
scan) from the working copy through `DataFiles.cs`, which finds the repository root. The held-out tests create temporary
folders below the system temporary folder and remove them. No network and no other tool is needed.

### Acceptance Criteria

All tests pass on every target framework. A test fails when two runs with the same inputs differ, a run with another seed
does not differ, the image of a reference run differs from its pinned hash, the trainer's tags differ from the decoder's,
a quantized weight differs from the oracle, a masked token changes a weight or is not counted as a step, a setting has no
effect, a held-out or development file is admitted as a source (also through a rooted path, a detour or a link) or a
held-out file is admitted for evaluation, a source
names a held-out set, an experiment without an output file is admitted, the check takes a perturbed, missing or damaged
file for a current one or rejects the compressed form of the current file, the configuration is accepted with a
misspelled member or an out-of-range value, a metric or a bootstrap interval differs from the known answer, or the
glossary experiment's decoder differs from the plain predictor, or the glossary evaluation reads any file, gives another
result for the same seed, puts a scored token in no category or in two, does not count a confident swap that a hard
constraint leaves with one candidate, changes a token before the first match, or applies one of its two decision rules
differently from the pre-registered text, or the held-out evaluation opens a file without a valid selection (also for
another model, rule or development file), runs after a results file exists, writes a dump inside the repository, accepts
a malformed probe or treebank row, selects an operating point that does not meet the pre-registered target and floor (or
relaxes them when none qualifies), computes a tally that disagrees with the metrics, renders a different report for the
same results or a treebank word into a report, or lets a stop condition pass.

### Test Scenarios

- **Deterministic training**: `ModelTrainer_Train_SameInputs_ByteIdenticalImage`,
  `ModelTrainer_Train_DifferentSeed_DifferentImage`, `ModelTrainer_Train_SmallUnrestrictedRun_MatchesGoldenHash` and
  `ModelTrainer_Train_SmallRestrictedRun_MatchesGoldenHash` (the golden values), and the generator tests
  `DeterministicRandom_NextUInt64_SeedZero_ReferenceSequence`,
  `DeterministicRandom_Shuffle_Permutation_ReproducibleAndSeedDependent` and
  `DeterministicRandom_NextInt_NonPositiveBound_Throws`.
- **Shared definitions**: `ModelTrainer_Predict_RestrictedWithLexicon_MatchesTagDecoder` and
  `ModelTrainer_Predict_Unrestricted_MatchesTagDecoderWithUnknownWords`.
- **Exact quantization**: `AveragedPerceptron_Quantize_RandomUpdates_MatchesDecimalOracle`,
  `AveragedPerceptron_Quantize_TiesAndClamp_RoundAwayFromZeroAndClamp` (a tie is rounded away from zero, large values are
  clamped to the 16-bit range without -32768), `AveragedPerceptron_Quantize_RangeExceeded_ThrowsClearException` and
  `AveragedPerceptron_Update_SumsExceedRange_ThrowsClearException` (a sum or a product that does not fit 64 bits, and a
  weight that does not fit `int`, are refused with a clear exception; the second test pushes a cell to the top of its
  range through reflection, since a real run cannot get there), `AveragedPerceptron_Quantize_PruneThreshold_DropsSmallWeights`,
  `ModelTrainer_Build_SmallRun_LoadsWithTheQuantizedWeights` (the written file loads with the quantized weights) and
  `ModelBuilder_WithTemperature_ReplacesOnlyTheTemperature` (the weights are untouched and a temperature that is not
  finite and positive is refused).
- **Corpus rules**: `ModelTrainer_Train_MaskedToken_NoUpdateButStepAndHistory`,
  `CorpusReader_Parse_MaskingRules_MarkTokensWithoutSignal` (masked tokens, `disputed` tokens when configured and silver
  tokens below the probability threshold), `CorpusReader_Parse_LimitAndSample_AreDeterministic`,
  `CorpusReader_Parse_UnknownTag_ThrowsWithLine` and `CorpusReader_LoadSources_Weight_RepeatsSentences`.
- **Training settings take effect**: `ModelTrainer_Train_MinCount_ShrinksTheModel` and
  `ModelTrainer_Train_RestrictionAndHistoryModes_ChangeTheImage`.
- **Development-only selection**: `DataGuard_EnsureDevelopmentFile_HeldOutOrOtherFile_Refused` (a theory over the held-out
  files, a training file and a dotted detour), `DataGuard_EnsureDevelopmentFile_DevelopmentFile_Admitted`,
  `DataGuard_EnsureTrainable_HeldOutOrDevelopmentFile_Refused` (also in other spellings of the path),
  `DataGuard_EnsureTrainable_TrainingCorpus_Admitted`, `DataGuard_EnsureTrainable_PathOutsideDataRoot_Refused` (a dotted
  detour or a path outside the `data` folder), `CorpusReader_LoadSources_RootedPathOutsideData_Refused` (a rooted source
  path, which `Path.Combine` lets replace the repository root),
  `DataGuard_EnsureTrainable_LinkToHeldOutOrOutsideData_Refused` (in a temporary fake repository: directory links to a
  held-out folder and to a folder outside the data root, made with symbolic links or, on Windows when those need a
  privilege, with junctions, and file links where symbolic links can be created; the test is skipped only where no link
  can be made at all and on .NET Framework, which does not resolve links),
  `DataGuard_EnsureDevelopmentHeader_NoDevSplitHeader_Throws`, `ModelTrainer_Sources_NoHeldOutPathLiterals` and
  `HeldOutEvaluation_Sources_NewFilesDoNotSpellHeldOutNames` (the evaluation files and the console program name no
  held-out set), and `DataGuard_HeldOutFiles_RefusedForDevelopmentEvaluationAndTraining` (the held-out files stay refused
  for development evaluation and for training).
- **Held-out gate**: `DataGuard_HeldOutFiles_AreFourSetsAndNoDevelopmentFile`, `DataGuard_EnsureHeldOutFile_NoSelection_Throws`
  (there is no way to resolve or read a held-out file without a selection),
  `SelectedOperatingPoints_Validate_ValidArtifact_KeepsSelectedPoints` and
  `SelectedOperatingPoints_Validate_InconsistentArtifact_Throws` (another schema, rule, model, a changed or missing
  development file, an off-grid threshold, and a configuration that names another model),
  `SelectedOperatingPoints_Validate_TamperedArtifact_Throws` (no points, duplicate, extra or missing development files,
  duplicate, extra, missing or renamed points, another target, another grid, an off-grid threshold),
  `SelectedOperatingPoints_Validate_ArbitraryGridThreshold_RefusedAgainstDerivation` (a point or count that differs from
  what the rule selects from the derived development curve is refused, the rule's own selection is accepted),
  `SelectedOperatingPoints_Validate_NoDerivationOrWrongDevAccuracy_Throws` (the gate fails closed: an expectation without
  the curve derivation or without the accuracy derivation is refused, and so is a recorded development accuracy that
  differs from the derived one by any amount),
  `OperatingPointsFile_Expect_MissingOrChangedDevelopmentFile_Throws` (in a temporary fake repository: a missing
  development file, or one whose bytes differ from the manifest hash, is refused; files as recorded give an expectation
  with both derivations) and
  `DevDerivation_PooledCurve_FixtureOutcomes_EqualsSelectionCurve` (the derived curve equals the curve of the selection),
  `DataGuard_EnsureHeldOutHeader_AnnouncedSet_AcceptedOthersRefused` (a theory: the announced set is accepted, a development
  split or another set is refused), `DataGuard_EnsureNotEvaluated_ExistingResultsFile_Throws` (the run-once marker: a
  results file or a lock file refuses the run), `DataGuard_AcquireRunLock_ExclusiveAndRemovedOnCompletion`,
  `DataGuard_AcquireRunLock_StaleLock_BlocksUntilDeletedByHand`, `RunLock_Complete_ExistingResultsFile_ThrowsAndKeepsLock`
  and `DataGuard_AcquireRunLock_ConcurrentAttempts_ExactlyOneWins` (in temporary folders: the lock file is created
  exclusively, a second run is refused, completion writes the results exclusively and removes the lock, a stale lock
  blocks until it is deleted by hand, and of 16 concurrent attempts exactly one wins),
  `DataGuard_EnsureOutsideRepository_InsideRefused_OutsideAccepted` (also through a dotted detour),
  `HeldOutReader_Read_FixtureRepository_VerifiesHashAndHeader` and `HeldOutReader_ReadUd_FixtureCache_VerifiesPin` (in a
  temporary fake repository: a changed file or a wrong header is refused, a changed treebank source, a mapped table that
  differs from its pin or a missing cache is refused, the latter with the fetch recipe),
  `DataGuard_UdMappedSha256_EqualsHashRecordedInCommittedResults` (the pinned constant is the hash that the committed
  results record for the mapped table), `OperatingPointsFile_FormatParseLoad_RoundTripAndValidation` (on .NET 8 and later:
  canonical form, unknown members refused, the selection hash is the hash of the file bytes),
  `OperatingPointsFile_Load_CommittedSelection_PassesGateForFrozenModel` (the committed selection is valid for the
  embedded model and the committed development files; no held-out file is opened) and
  `OperatingPointsFile_Load_CommittedSelection_EqualsDevDerivation` (the committed points and the committed development
  accuracy equal what the rule and the model give on the development data, and a tampered threshold or accuracy is
  refused).
- **Confidence curve and rule**: `EvalTally_Of_Outcomes_AgreesWithMetricsCompute` (also the sum of the document tallies),
  `ConfidenceCurve_Of_HandComputedOutcomes_MatchesExpectedRows` (answered, harmful, coverage and rates on seven
  decisions at
  several thresholds, coverage never rises with the threshold),
  `ConfidenceCurve_Reliability_Decisions_OneBinEach`,
  `OperatingPointRule_Select_FirstQualifyingThreshold_StrictAndBalanced` (the first qualifying threshold, a point that
  fails only the coverage floor is skipped), `OperatingPointRule_Select_BoundariesAndNone_AreNotRelaxed` (a target and a
  floor met exactly qualify, a hair above does not, none qualifies, nothing answered never qualifies) and
  `DevSelection_Create_FixtureOutcomes_DeterministicAndConsistent` (the selection, its report and its artifact are
  deterministic and consistent with the rule).
- **Held-out metrics and reports**: `HeldOutSets_ParseProbe_Rows_BracesRemovedAndValidated`,
  `HeldOutSets_ParseProbe_NegativeOrNonNumericTargetIndex_ThrowsDataError` (a negative, signed, fractional, empty,
  non-numeric or overflowing target index is an `InvalidDataException` that names the row, never an index error),
  `HeldOutSets_ParseUd_MappedTable_ReadsTagsRulesAndDocuments`, `DevSets_ParseGold_StatusAndAgreement_Carried`,
  `DevSets_GoldClassOf_EveryTag_CollapsesToTenClassesAndOther`,
  `ClusterBootstrap_Intervals_TiedBetterDeterministicAndSingleItemClusters`,
  `EvaluationReport_Summarize_OperatingPoint_PairedDifferenceAgainstReference`,
  `EvaluationReport_Summarize_TagFiguresAndIntervals_ArePresent` (the precision and recall of every tag that occurs, the
  ambiguous-accuracy and log-likelihood intervals and the confusion cells, on known counts),
  `OperatingPointsFile_ParseResults_CommittedResults_HaveTagFiguresAndIntervalsForEverySet` (on .NET 8 and later: every
  set of the committed results has its tags, intervals and confusion, the gold tokens of the tags add up to the tokens
  of the set, and the report has a tag table for every set),
  `EvaluationReport_Tripwires_EachConditionFiresAlone` (each stop condition fires alone, at its boundary),
  `HeldOutEvaluator_Score_FixtureSets_ViewsAndReports` (every view of every set, the judgment equal to the development
  judgment, a deterministic report with a tag table for every view and without any treebank word, sentence id or
  document),
  `HeldOutEvaluator_Throughput_Fixture_IsPositive`, `EvaluationReport_WriteDump_Tokens_HeaderAndRows` and
  `OperatingPointsFile_FormatResults_RoundTrip`.
- **Command-line safety**: `OutputGuard_EnsureExplicitOutput_ExperimentWithoutOutput_Throws` (a theory: no output and an
  empty output) and `OutputGuard_EnsureExplicitOutput_ExperimentWithOutputOrDefaultRun_Admitted`.
- **The check**: `ModelBuilder_Check_CommittedFile_DetectsStaleMissingAndInvalid` (the compressed committed form of the
  fresh image is current, another seed or another temperature is stale, no file is missing, bytes that are no weights file
  are invalid).
- **Configuration**: `ConfigFile_Parse_CommittedConfiguration_ValidAndCanonical` (on .NET 8 and later: three existing
  corpora, and rewriting the file gives the same text) and `ConfigFile_Parse_UnknownMemberOrBadRange_Throws`.
- **Development metrics**: `DevSets_ParseGoldDev_Fixture_ReadsTargetsAndMapsClasses`,
  `DevSets_ParseGoldDev_InvalidTarget_Throws` (a negative, non-numeric or out-of-sentence target index),
  `Metrics_Compute_KnownOutcomes_ExpectedNumbers`, `Metrics_Judge_TinyTagger_CorrectAndNounVerbError`,
  `Bootstrap_Compare_IdenticalLists_ZeroDifferenceTied`, `Bootstrap_Compare_BetterList_PositiveDifferenceExcludesZero`,
  `DevEvaluator_EvaluateOpen_FixtureSentences_OneOutcomePerToken` and `RunLog_Row_Summary_HasOneCellPerHeaderColumn`.
- **Glossary experiment**: `GlossaryTraining_Draw_NoWrongHints_HintsAreGoldAndReproducible`,
  `GlossaryTraining_Draw_AllHintsWrong_HintIsAnotherAllowedTag`,
  `GlossaryTraining_Draw_CapitalizedToken_UsesShapeCandidatesOfTheOriginalToken` and
  `GlossaryTraining_Draw_CorpusSample_EligibilityMatchesRuntimeMask` (eligibility uses the runtime candidate mask of the
  token as written), `ModelTrainer_Train_GlossaryHints_ChangeTheImage` (and
  the plain run keeps its pinned hash, which is the golden test),
  `GlossaryFeatures_Append_InBucketSpaceAndDependOnHintAndHistory`, `HintedPredictor_NoHints_MatchesQuantizedPredictor`
  and `HintedPredictor_HardHintAndHugeBoost_FollowTheHint`.
- **Glossary evaluation** (the tests run the evaluation code of the tool on tiny dev-like fixtures with known answers, not
  on the development data): `GoldTarget_Lemma_IsParsedAndOptional` (the lemma column of the gold development set is read
  when present, and is empty otherwise), `GlossaryEvaluation_StrengthGrid_ContainsTheDefaultSoftStrength` (the library
  constant is a member of the evaluated grid, which is sorted),
  `GlossaryEvaluation_ChooseSoftStrength_LargestPassingStrengthWins` and
  `GlossaryEvaluation_ChooseSoftStrength_ConditionsAndNonePasses` (the rule that fixes the default soft strength, on
  synthetic tables: the boundary of each of its two conditions, the largest passing strength wins even when a larger one
  has the better covered accuracy, and the smallest of the grid when none passes),
  `GlossaryEvaluation_ChooseSoftStrength_RecordedCells_EqualsTheDefaultSoftStrength` (the same rule on the cells recorded
  in the run log gives the library constant, which is a member of the grid),
  `GlossaryEvaluation_ChooseFirstRunSoftStrength_PicksSmallestWithinTolerance` and
  `GlossaryEvaluation_ChooseFirstRunSoftStrength_ConditionsAndNoneAdmissible` (the original pre-registered strength rule,
  the rule of the first run that the command still prints as the historical decision, on synthetic
  tables: the boundary of each admissibility condition, the tie tolerance, the smallest tied strength, and the smallest
  of the grid when none is admissible), `GlossaryEvaluation_ChooseNonHeadRule_BarNeedsGainIntervalAndNoLoss` (the bar of
  scenario G: a gain of the required size whose interval excludes zero and no loss at 10% wrong entries; the larger gain
  wins), `GlossaryEvaluation_CompoundCandidates_SpansEndInNounAndAreDistinct`,
  `GlossaryEvaluation_DrawTerms_DeterministicSeededAndWrongEntriesDiffer` (the same seed gives the same glossary, the
  coverage share is met and a wrong entry never keeps the right tag),
  `GlossaryEvaluation_ToEntries_UnrepresentableTermsAreSkipped`,
  `GlossaryEvaluation_RunSystem_CategoriesPartitionEveryScoredToken` (every scored token is in exactly one category and
  the at-risk flag marks the compounds whose first word is also a verb),
  `GlossaryEvaluation_RunScenario_ConfidentSwapsFlipsAndBaselineDenominator` (a hard wrong entry is a confident swap
  although it leaves one candidate, a hard right entry removes a swap, the denominator is the baseline's, and no token
  before the first match changes), `GlossaryEvaluation_AcronymEntries_AllCapitalNounsWithLexiconHomograph`,
  `GlossaryEvaluation_Run_TinyData_ReportsEveryScenarioDeterministically` (the whole pipeline, twice, with equal output,
  zero parity differences and zero changes before a match) and
  `GlossaryEvaluation_UsesOnlyDevData_NoPathParameterAndNoFileAccess` (the entry point takes no path, and the sources use
  no file API and name no data file). The measured results are not asserted anywhere; they are recorded in section E9 of
  the run log.
