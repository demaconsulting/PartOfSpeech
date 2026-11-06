"""Run the unit tests of the corrections tools and write a TRX file that ReqStream reads as test evidence (Python 3, stdlib only).

  python tools/data/tests/run_trx.py [--out artifacts/python-data-tools.trx] [--pattern "test_corrections_*.py"]

The discovery is the one of `python -m unittest discover -s tools/data/tests -p PATTERN`; the exit code is 0 when every test passed
(a skipped test is neither a pass nor a failure) and 1 otherwise, also when the pattern finds no test (nothing is written then:
empty evidence must not look like a pass). The TRX is written in both other cases. A test result is named by the
method name (`testName`), which is what a requirement lists under `tests:`; the class is `<module>.<class>`.
"""
import argparse
import datetime
import os
import sys
import time
import unittest
import uuid
import xml.etree.ElementTree as ElementTree

HERE = os.path.dirname(os.path.abspath(__file__))
NAMESPACE = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
OUTCOMES = {"passed": "Passed", "failure": "Failed", "error": "Failed", "skipped": "NotExecuted"}


class Recorder(unittest.TextTestResult):
    """A text result that also keeps one record per test: (test id, seconds, outcome, message); a failed subtest fails its test."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.records = []
        self._started = {}

    def startTest(self, test):
        self._started[test.id()] = time.perf_counter()
        super().startTest(test)

    def _record(self, test, outcome, message=""):
        seconds = time.perf_counter() - self._started.get(test.id(), time.perf_counter())
        self.records.append((test.id(), seconds, outcome, message))

    def addSuccess(self, test):
        super().addSuccess(test)
        self._record(test, "passed")

    def addFailure(self, test, err):
        super().addFailure(test, err)
        self._record(test, "failure", self._exc_info_to_string(err, test))

    def addError(self, test, err):
        super().addError(test, err)
        self._record(test, "error", self._exc_info_to_string(err, test))

    def addSkip(self, test, reason):
        super().addSkip(test, reason)
        self._record(test, "skipped", reason)

    def addSubTest(self, test, subtest, err):
        super().addSubTest(test, subtest, err)
        if err is not None:
            self._record(test, "failure", self._exc_info_to_string(err, test))


def results_by_test(records):
    """{test id: (seconds, outcome, message)}: a failure of a test or of one of its subtests wins over the success record."""
    merged = {}
    for test_id, seconds, outcome, message in records:
        known = merged.get(test_id)
        if known is None or (known[1] == "passed" and outcome != "passed"):
            merged[test_id] = (seconds, outcome, message)
    return merged


def trx_element(records, name="tools-data-tests"):
    """The `TestRun` element of the records (the subset of the schema that ReqStream and the VSTest tools read)."""
    ElementTree.register_namespace("", NAMESPACE)

    def q(tag):
        return "{%s}%s" % (NAMESPACE, tag)

    now = datetime.datetime.now(datetime.timezone.utc).isoformat()
    merged = results_by_test(records)
    run = ElementTree.Element(q("TestRun"), {"id": str(uuid.uuid4()), "name": name})
    results = ElementTree.SubElement(run, q("Results"))
    definitions = ElementTree.SubElement(run, q("TestDefinitions"))
    entries = ElementTree.SubElement(run, q("TestEntries"))
    counters = {"total": 0, "executed": 0, "passed": 0, "failed": 0, "notExecuted": 0}
    for test_id in sorted(merged):
        seconds, outcome, message = merged[test_id]
        module, _, method = test_id.rpartition(".")
        guid = str(uuid.uuid4())
        verdict = OUTCOMES[outcome]
        result = ElementTree.SubElement(results, q("UnitTestResult"), {
            "executionId": str(uuid.uuid4()), "testId": guid, "testName": method, "computerName": "local",
            "duration": str(datetime.timedelta(seconds=seconds)), "startTime": now, "endTime": now, "outcome": verdict})
        if outcome in ("failure", "error"):
            info = ElementTree.SubElement(ElementTree.SubElement(ElementTree.SubElement(result, q("Output")), q("ErrorInfo")), q("Message"))
            info.text = message
        unit = ElementTree.SubElement(definitions, q("UnitTest"), {"name": method, "id": guid})
        ElementTree.SubElement(unit, q("TestMethod"), {"codeBase": "tools/data/tests", "className": module, "name": method})
        ElementTree.SubElement(entries, q("TestEntry"), {"testId": guid, "executionId": result.get("executionId")})
        counters["total"] += 1
        counters["executed"] += outcome != "skipped"
        counters["passed"] += outcome == "passed"
        counters["failed"] += outcome in ("failure", "error")
        counters["notExecuted"] += outcome == "skipped"
    summary = ElementTree.SubElement(run, q("ResultSummary"), {"outcome": "Failed" if counters["failed"] else "Completed"})
    ElementTree.SubElement(summary, q("Counters"), {key: str(value) for key, value in counters.items()})
    return run


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=os.path.join("artifacts", "python-data-tools.trx"), help="TRX file to write")
    parser.add_argument("--pattern", default="test_corrections_*.py", help="test file pattern of the discovery")
    args = parser.parse_args(argv)
    sys.path.insert(0, HERE)
    suite = unittest.defaultTestLoader.discover(HERE, pattern=args.pattern, top_level_dir=HERE)
    if suite.countTestCases() == 0:
        # No evidence is better than empty evidence: a pattern that finds nothing must not look like a pass.
        print("error: no tests found for the pattern %r in %s; no TRX written" % (args.pattern, HERE), file=sys.stderr)
        return 1
    result = unittest.TextTestRunner(resultclass=Recorder, verbosity=1).run(suite)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    tree = ElementTree.ElementTree(trx_element(result.records))
    ElementTree.indent(tree)
    tree.write(args.out, encoding="utf-8", xml_declaration=True)
    print("wrote %s (%d test results)" % (args.out, len(results_by_test(result.records))))
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    sys.exit(main())
