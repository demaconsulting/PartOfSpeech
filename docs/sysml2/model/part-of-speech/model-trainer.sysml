package PartOfSpeech {
    part def ModelTrainer {
        doc /* The build-time tool that trains the embedded averaged-perceptron weights from the committed corpora with the library's own feature extractor, selects every setting on development data only, and checks that the committed weights are current. It is not shipped. */

        comment sourceRef /* Source: tools/DemaConsulting.PartOfSpeech.Trainer/Program.cs, tools/DemaConsulting.PartOfSpeech.Trainer/ConfigFile.cs, tools/DemaConsulting.PartOfSpeech.Trainer/RunLog.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Training/AveragedPerceptron.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Training/CorpusReader.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Training/DeterministicRandom.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Training/GlossaryFeatures.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Training/ModelBuilder.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Training/PerceptronTrainer.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Training/QuantizedPredictor.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Training/TagSelection.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Training/TrainingConfig.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/Calibration.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/DevEvaluator.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/DevSets.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/GlossaryExperiment.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/GlossaryEvaluation.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/GlossaryEvaluationReport.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/GlossaryProbe.cs, tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/Metrics.cs */
        comment dataRef /* Data: tools/DemaConsulting.PartOfSpeech.Trainer/training.json, tools/DemaConsulting.PartOfSpeech.Trainer/README.md, tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md, src/DemaConsulting.PartOfSpeech/Resources/weights.bin */
        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/ModelTrainer/ModelTrainerTests.cs */
        comment designRef /* Design: docs/design/part-of-speech/model-trainer.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech/model-trainer.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech/model-trainer.yaml */
    }
}
