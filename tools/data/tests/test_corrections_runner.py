"""Tests of the TRX runner `run_trx.py` that writes the requirements evidence of the Python tests."""
import contextlib
import io
import os
import tempfile
import unittest
import xml.etree.ElementTree as ElementTree

import run_trx

NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.out = os.path.join(self.temp.name, "evidence", "result.trx")

    def run_main(self, *argv):
        errors = io.StringIO()
        with contextlib.redirect_stderr(errors), contextlib.redirect_stdout(io.StringIO()):
            code = run_trx.main(list(argv))
        return code, errors.getvalue()

    def test_a_pattern_that_finds_no_test_is_an_error_and_writes_no_evidence(self):
        # Arrange: no test file matches the pattern
        # Act
        code, errors = self.run_main("--out", self.out, "--pattern", "no_such_tests_*.py")
        # Assert
        self.assertEqual(1, code)
        self.assertIn("no tests found", errors)
        self.assertFalse(os.path.exists(self.out))
        self.assertFalse(os.path.exists(os.path.dirname(self.out)))

    def test_a_failed_test_wins_over_a_passed_record_and_the_run_is_marked_failed(self):
        # Arrange
        records = [("m.C.test_a", 0.1, "passed", ""), ("m.C.test_a", 0.2, "failure", "boom"), ("m.C.test_b", 0.1, "passed", ""),
                   ("m.C.test_c", 0.0, "skipped", "why")]
        # Act
        run = run_trx.trx_element(records)
        outcomes = {r.get("testName"): r.get("outcome") for r in run.iter(NAMESPACE + "UnitTestResult")}
        summary = run.find(NAMESPACE + "ResultSummary")
        # Assert
        self.assertEqual({"test_a": "Failed", "test_b": "Passed", "test_c": "NotExecuted"}, outcomes)
        self.assertEqual("Failed", summary.get("outcome"))
        self.assertEqual("3", summary.find(NAMESPACE + "Counters").get("total"))

    def test_the_written_file_is_a_readable_trx_with_one_result_per_test(self):
        # Arrange
        records = [("m.C.test_a", 0.1, "passed", ""), ("m.C.test_b", 0.1, "passed", "")]
        os.makedirs(os.path.dirname(self.out))
        # Act
        ElementTree.ElementTree(run_trx.trx_element(records)).write(self.out, encoding="utf-8", xml_declaration=True)
        root = ElementTree.parse(self.out).getroot()
        # Assert
        self.assertEqual(NAMESPACE + "TestRun", root.tag)
        self.assertEqual(["test_a", "test_b"], sorted(r.get("testName") for r in root.iter(NAMESPACE + "UnitTestResult")))
        self.assertEqual("Completed", root.find(NAMESPACE + "ResultSummary").get("outcome"))


if __name__ == "__main__":
    unittest.main()
