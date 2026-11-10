package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def ModelTrainer {
        doc /* The build-time tool that trains the embedded averaged-perceptron weights from the committed corpora with the library's own feature extractor, selects every setting on development data only, and checks that the committed weights are current. It is not shipped. It also runs the one held-out evaluation of the frozen model, behind a development-only selection of the confidence thresholds that must be committed first. For the context-feature experiment (the embedded model enables the clause-cue group) it trains the switchable feature groups (including the jackknife preview tags of the right preview group) and scores a trained arm on development data only (the command eval-arm). */

        metadata info : SoftwareInfo {
            description = "The build-time tool that trains the embedded averaged-perceptron weights from the committed corpora with the library's own feature extractor, selects every setting on development data only, and checks that the committed weights are current. It is not shipped. It also runs the one held-out evaluation of the frozen model, behind a development-only selection of the confidence thresholds that must be committed first. For the context-feature experiment (the embedded model enables the clause-cue group) it trains the switchable feature groups (including the jackknife preview tags of the right preview group) and scores a trained arm on development data only (the command eval-arm).";
            sourceFiles = ("tools/DemaConsulting.PartOfSpeech.Trainer/Program.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/ConfigFile.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/OperatingPointsFile.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/RunLog.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/AveragedPerceptron.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/CorpusReader.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/DeterministicRandom.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/GlossaryFeatures.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/Jackknife.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/ModelBuilder.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/OutputGuard.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/PerceptronTrainer.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/QuantizedPredictor.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/TagSelection.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Training/TrainingConfig.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/ArmEvaluation.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/Calibration.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/DevEvaluator.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/DevSets.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/ShapesDev.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/ShapesDevTranches.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/GlossaryExperiment.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/GlossaryEvaluation.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/GlossaryEvaluationReport.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/GlossaryProbe.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/Metrics.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/ClusterBootstrap.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/ConfidenceCurve.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/EvalTally.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/EvaluationData.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/EvaluationReport.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/HeldOutEvaluator.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/HeldOutSets.cs", "tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/OperatingPoints.cs");
            // Data artifacts (not modeled by SoftwareInfo):
            //   tools/DemaConsulting.PartOfSpeech.Trainer/training.json
            //   tools/DemaConsulting.PartOfSpeech.Trainer/README.md
            //   tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md
            //   src/DemaConsulting.PartOfSpeech/Resources/weights.bin
            //   docs/evaluation/operating-points.json
            //   docs/evaluation/confidence-thresholds.md
            //   docs/evaluation/disclosure.md
            //   docs/evaluation/test-results.json
            //   docs/evaluation/test-results.md
            //   docs/evaluation/README.md
            //   data/eval/shapes-dev/dev.tsv
            //   data/eval/shapes-dev/hard.tsv
            //   data/eval/shapes-dev/compound-head.tsv
            //   data/eval/shapes-dev/headings.tsv
            //   data/eval/shapes-dev/named-state.tsv
            //   data/imperative/imperative.tsv
            //   data/compound/compound.tsv
            //   data/headings/headings.tsv
            //   tools/DemaConsulting.PartOfSpeech.Trainer/context-features/arms.json
            //   tools/DemaConsulting.PartOfSpeech.Trainer/context-features/run_arms.ps1
            //   tools/DemaConsulting.PartOfSpeech.Trainer/context-features/analyze_arms.py
            //   tools/DemaConsulting.PartOfSpeech.Trainer/imperative-data/analyze_imperative.py
            //   tools/DemaConsulting.PartOfSpeech.Trainer/imperative-data/runner-base.cmd
            //   tools/DemaConsulting.PartOfSpeech.Trainer/imperative-data/runner-imp.cmd
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/ModelTrainer/ModelTrainerTests.cs", "test/DemaConsulting.PartOfSpeech.Tests/ModelTrainer/HeldOutEvaluationTests.cs", "test/DemaConsulting.PartOfSpeech.Tests/ModelTrainer/ShapesDevEvaluationTests.cs");
            designFiles = ("docs/design/part-of-speech/model-trainer.md");
            verificationFiles = ("docs/verification/part-of-speech/model-trainer.md");
            requirementFiles = ("docs/reqstream/part-of-speech/model-trainer.yaml");
        }
    }
}
