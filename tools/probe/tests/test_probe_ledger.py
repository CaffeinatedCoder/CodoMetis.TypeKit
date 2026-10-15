#!/usr/bin/env python3
"""Regression tests for tools/probe/probe-ledger's evidence accounting, each against an isolated fixture
repository (git, a one-claim universe, a corpus and a ledger directory), never the real one.

Run: python3 tools/probe/tests/test_probe_ledger.py
"""
import json, os, shutil, subprocess, sys, tempfile, unittest

LEDGER = os.environ.get("PROBE_LEDGER") or os.path.join(os.path.dirname(os.path.dirname(os.path.realpath(__file__))), "probe-ledger")


def sh(cwd, *argv, check=True):
    r = subprocess.run(argv, cwd=cwd, capture_output=True, text=True)
    if check and r.returncode:
        raise AssertionError(f"{argv} failed: {r.stdout}{r.stderr}")
    return r


class Fixture:
    """A repository with one claim C1 over axes x and y (values 1 and 2) and one probe P covering it."""

    def __init__(self):
        self.root = tempfile.mkdtemp(prefix="probe-ledger-test-")
        self.mem = os.path.join(self.root, ".claude", "agent-memory-local", "prober")
        self.bdir = os.path.join(self.mem, "branches", "main")
        sh(self.root, "git", "init", "-q", "-b", "main")
        self.write("README.md", "# a\n\nThe claim.\n")
        self.write("src/a.cs", "class A {}\n")
        self.write(".claude/probe-axes.md", "```json probe-axes\n" + json.dumps({
            "axes": {"x": ["1", "2"], "y": ["1", "2"]},
            "surfaces": {"s": {"axes": ["x", "y"], "watched": ["src"]}}}) + "\n```\n")
        self.write("tools/probe/corpus/claims.json", json.dumps([{"id": "C1", "source": "README.md#a", "surfaces": ["s"]}]))
        self.write("tools/probe/corpus/registry.json", json.dumps({"probes": {"P": {"claim": "C1", "filter": "*P"}}}))
        self.write("tools/probe/corpus/harness/P.cs", "// probe P\n")
        self.rows([{"x": "1", "y": "1"}, {"x": "2", "y": "2"}])
        self.commit("init")
        os.makedirs(self.bdir, exist_ok=True)
        with open(os.path.join(self.mem, "axes-generated.json"), "w") as f:
            json.dump({"axes": {}, "needsValues": []}, f)
        self.ledger("universe", "--base", "HEAD")

    def write(self, rel, text):
        path = os.path.join(self.root, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w") as f:
            f.write(text)

    def rows(self, rows):
        self.write("tools/probe/corpus/rows/C1.json", json.dumps({"claim": "C1", "rows": [{"row": r} for r in rows]}))

    def commit(self, msg):
        sh(self.root, "git", "add", "-A")
        sh(self.root, "git", "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", msg)

    def ledger(self, *args, check=True):
        return sh(self.root, sys.executable, LEDGER, *args, check=check)

    def footprint(self):
        """The product footprint P ran: src/a.cs at its current id."""
        blob = sh(self.root, "git", "rev-parse", "HEAD:src/a.cs").stdout.strip()
        with open(os.path.join(self.bdir, "footprints.jsonl"), "a") as f:
            f.write(json.dumps({"probe": "P", "footprint": {"src/a.cs": blob}, "compileTime": {}, "toolchain": {}}) + "\n")

    def evidence(self, rows, applied=None, category="pass", expected=None, executed=None):
        """What a replay of P records: the rows it ran, how each ended and the settings that applied."""
        path = os.path.join(self.root, "evidence.json")
        idx = list(range(len(rows)))
        with open(path, "w") as f:
            json.dump({"variants": [{"env": {}, "expected": expected if expected is not None else idx,
                                     "executed": executed if executed is not None else idx}],
                       "rows": {str(i): {"category": category, "applied": (applied or rows)[i]} for i in idx}}, f)
        return path

    def covered(self):
        out = self.ledger("status", "--json")
        return json.loads(out.stdout)["claims"]["C1"]["current"]

    def close(self):
        shutil.rmtree(self.root, ignore_errors=True)


class Accounting(unittest.TestCase):
    def setUp(self):
        self.fx = Fixture()

    def tearDown(self):
        self.fx.close()

    def record_pass(self, rows, **kw):
        self.fx.footprint()
        self.fx.ledger("record-run", "--probe", "P", "--result", "pass", "--evidence", self.fx.evidence(rows, **kw))

    def test_a_pass_credits_the_pairs_of_the_rows_it_ran(self):
        self.record_pass([{"x": "1", "y": "1"}, {"x": "2", "y": "2"}])
        self.assertEqual(self.fx.covered(), 2)

    def test_an_old_pass_does_not_credit_a_row_added_since(self):
        """Reviewer, finding 1: an unchanged product footprint, an old passing run and a changed corpus row."""
        self.record_pass([{"x": "1", "y": "1"}, {"x": "2", "y": "2"}])
        self.fx.rows([{"x": "1", "y": "1"}, {"x": "2", "y": "2"}, {"x": "1", "y": "2"}])
        self.fx.commit("a new row")
        self.assertEqual(self.fx.covered(), 0, "the pass was for another corpus: nothing of it may count now")

    def test_a_changed_harness_voids_the_pass(self):
        self.record_pass([{"x": "1", "y": "1"}, {"x": "2", "y": "2"}])
        self.fx.write("tools/probe/corpus/harness/P.cs", "// probe P, changed\n")
        self.fx.commit("harness")
        self.assertEqual(self.fx.covered(), 0)

    def test_a_changed_axes_file_voids_the_pass(self):
        self.record_pass([{"x": "1", "y": "1"}, {"x": "2", "y": "2"}])
        with open(os.path.join(self.fx.root, ".claude/probe-axes.md")) as f:
            text = f.read()
        text = text.replace('"watched": ["src"]', '"watched": ["src", "README.md"]')
        self.fx.write(".claude/probe-axes.md", text)
        self.fx.commit("axes")
        self.assertEqual(self.fx.covered(), 0)

    def test_only_the_settings_that_applied_earn_credit(self):
        """Reviewer, finding 2: x=2 was requested in row 2, but the harness ran it as x=1."""
        rows = [{"x": "1", "y": "1"}, {"x": "2", "y": "2"}]
        self.record_pass(rows, applied=[{"x": "1", "y": "1"}, {"x": "1", "y": "2"}])
        self.assertEqual(self.fx.covered(), 2, "credit x1y1 and the applied x1y2, never the requested x2y2")
        # x2y2 must be the uncovered one
        out = self.fx.ledger("rows", "--claim", "C1")
        todo = {tuple(sorted(p)) for r in json.loads(out.stdout)["rows"] for p in r["pairs"]}
        self.assertIn(("x=2", "y=2"), todo)

    def test_an_axis_the_harness_never_read_earns_nothing(self):
        rows = [{"x": "1", "y": "1"}, {"x": "2", "y": "2"}]
        self.record_pass(rows, applied=[{"x": "1"}, {"x": "2"}])
        self.assertEqual(self.fx.covered(), 0)

    def test_a_refusal_is_no_evidence_of_success(self):
        """Reviewer, finding 3: an expected refusal says nothing about serializing successfully."""
        self.record_pass([{"x": "1", "y": "1"}, {"x": "2", "y": "2"}], category="expected-refusal")
        self.assertEqual(self.fx.covered(), 0)

    def test_a_row_that_did_not_run_is_never_credited(self):
        """Reviewer, finding 4: expected rows against executed rows."""
        self.record_pass([{"x": "1", "y": "1"}, {"x": "2", "y": "2"}], executed=[0])
        self.assertEqual(self.fx.covered(), 1)


class Lock(unittest.TestCase):
    def setUp(self):
        self.fx = Fixture()

    def tearDown(self):
        self.fx.close()

    def test_a_second_holder_is_refused_while_the_first_is_alive(self):
        self.fx.ledger("lock", "--holder", "a", "--mode", "explore", "--pid", str(os.getpid()))
        r = self.fx.ledger("lock", "--holder", "b", "--mode", "replay", check=False)
        self.assertNotEqual(r.returncode, 0)

    def test_a_holder_whose_heartbeat_stopped_is_taken_over(self):
        self.fx.ledger("lock", "--holder", "a", "--mode", "explore")
        path = os.path.join(self.fx.mem, "round.lock")
        with open(path) as f:
            held = json.load(f)
        held["heartbeat"] -= 21 * 60
        with open(path, "w") as f:
            json.dump(held, f)
        r = self.fx.ledger("lock", "--holder", "b", "--mode", "replay", check=False)
        self.assertEqual(r.returncode, 0, r.stderr)

    def test_a_heartbeat_keeps_the_holder_alive(self):
        self.fx.ledger("lock", "--holder", "a", "--mode", "explore")
        path = os.path.join(self.fx.mem, "round.lock")
        with open(path) as f:
            held = json.load(f)
        held["heartbeat"] -= 21 * 60
        with open(path, "w") as f:
            json.dump(held, f)
        self.fx.ledger("heartbeat", "--holder", "a")
        r = self.fx.ledger("lock", "--holder", "b", "--mode", "replay", check=False)
        self.assertNotEqual(r.returncode, 0)

    def test_two_takers_at_once_get_one_lock(self):
        procs = [subprocess.Popen([sys.executable, LEDGER, "lock", "--holder", f"h{i}", "--mode", "replay",
                                   "--pid", str(os.getpid())], cwd=self.fx.root, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
                 for i in range(8)]
        codes = [(p.communicate(), p.returncode)[1] for p in procs]
        self.assertEqual(codes.count(0), 1, codes)

    def test_racing_takers_of_a_dead_holder_get_one_lock(self):
        self.fx.ledger("lock", "--holder", "dead", "--mode", "explore")
        path = os.path.join(self.fx.mem, "round.lock")
        with open(path) as f:
            held = json.load(f)
        held["heartbeat"] -= 21 * 60
        with open(path, "w") as f:
            json.dump(held, f)
        procs = [subprocess.Popen([sys.executable, LEDGER, "lock", "--holder", f"h{i}", "--mode", "replay"],
                                  cwd=self.fx.root, stdout=subprocess.PIPE, stderr=subprocess.PIPE) for i in range(8)]
        codes = [(p.communicate(), p.returncode)[1] for p in procs]
        self.assertEqual(codes.count(0), 1, codes)

class Gate(unittest.TestCase):
    def setUp(self):
        self.fx = Fixture()

    def tearDown(self):
        self.fx.close()

    def gate(self):
        r = self.fx.ledger("gate", "--since", "HEAD~0", "--json", check=False)
        return r.returncode, json.loads(r.stdout)

    def touch_claim(self):
        self.fx.write("README.md", "# a\n\nThe claim, reworded.\n")
        self.fx.commit("docs")

    def test_an_unprobed_touched_claim_fails_with_what_is_missing(self):
        sh(self.fx.root, "git", "tag", "v1")
        self.touch_claim()
        code, g = self.fx.ledger("gate", "--since", "v1", "--json", check=False).returncode, None
        self.assertNotEqual(code, 0)

    def test_a_reviewed_waiver_meets_the_obligation_without_a_probe(self):
        sh(self.fx.root, "git", "tag", "v1")
        self.fx.write("tools/probe/corpus/registry.json", json.dumps({"probes": {}}))
        self.fx.write("tools/probe/corpus/waivers.json", json.dumps([{"claim": "C1", "reason": "covered by the suite's C1Tests", "reviewed_by": "maintainer", "date": "2026-10-15"}]))
        self.touch_claim()
        r = self.fx.ledger("gate", "--since", "v1", check=False)
        self.assertEqual(r.returncode, 0, r.stderr)

    def test_an_unsigned_waiver_meets_nothing(self):
        sh(self.fx.root, "git", "tag", "v1")
        self.fx.write("tools/probe/corpus/registry.json", json.dumps({"probes": {}}))
        self.fx.write("tools/probe/corpus/waivers.json", json.dumps([{"claim": "C1", "reason": "later"}]))
        self.touch_claim()
        r = self.fx.ledger("gate", "--since", "v1", check=False)
        self.assertNotEqual(r.returncode, 0)

    def test_state_goes_where_PROBE_STATE_DIR_says(self):
        state = tempfile.mkdtemp(prefix="probe-state-")
        try:
            env = {**os.environ, "PROBE_STATE_DIR": state}
            r = subprocess.run([sys.executable, LEDGER, "lock", "--holder", "ci", "--mode", "replay"], cwd=self.fx.root, env=env, capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertTrue(os.path.exists(os.path.join(state, "round.lock")))
            self.assertFalse(os.path.exists(os.path.join(self.fx.mem, "round.lock")))
        finally:
            shutil.rmtree(state, ignore_errors=True)


if __name__ == "__main__":
    unittest.main(verbosity=2)
