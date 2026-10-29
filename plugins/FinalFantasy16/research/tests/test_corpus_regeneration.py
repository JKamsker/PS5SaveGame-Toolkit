import hashlib
import json
import os
from pathlib import Path
import struct
import unittest

from corpus_inputs import (ALLOCATION_OFFSET, HEADER_SIZE, TITLES, container,
                           repair_crc, write_archives)
from tooling_support import GOLDENS, RESEARCH, checkout_bytes, run_script, temporary_directory


class CorpusRegenerationTests(unittest.TestCase):
    def test_synthetic_archives_reproduce_committed_metadata_golden(self):
        with temporary_directory() as scratch:
            write_archives(scratch)
            result = run_script(RESEARCH / "summarize_corpus.py", artifacts=scratch)
            self.assertEqual(0, result.returncode, result.stderr.decode("utf-8"))
            self.assertEqual(checkout_bytes(GOLDENS / "corpus-synthetic.json"), result.stdout)

    def test_summary_mode_only_omits_individual_identities(self):
        expected = json.loads(checkout_bytes(GOLDENS / "corpus-synthetic.json"))
        for title in expected["titles"]:
            del title["records"]
        with temporary_directory() as scratch:
            write_archives(scratch)
            result = run_script(RESEARCH / "summarize_corpus.py", "--summary", artifacts=scratch)
            self.assertEqual(0, result.returncode, result.stderr.decode("utf-8"))
            self.assertEqual((json.dumps(expected, indent=2) + "\n").encode("utf-8"), result.stdout)

    def test_corrupt_records_retain_assertion_diagnostics_without_summary_output(self):
        # Each input gets a valid CRC except the CRC case. Later framing/item
        # checks must actually run rather than getting masked by the CRC gate.
        changes = [("header", 0, "<I", 8), ("crc", 4, "<I", 0),
                   ("block-too-small", HEADER_SIZE + 4, "<I", 16),
                   ("block-unaligned", HEADER_SIZE + 4, "<I", 25),
                   ("block-overrun", HEADER_SIZE + 4, "<I", 40000),
                   ("item-version", HEADER_SIZE + 8, "<Q", 0),
                   ("allocation-count", HEADER_SIZE * 2 + ALLOCATION_OFFSET, "<Q", 1201),
                   ("first-item", HEADER_SIZE * 2, "<i", 2)]
        for name, offset, encoding, value in changes:
            with self.subTest(case=name), temporary_directory() as scratch:
                data = bytearray(container(inventory=True))
                struct.pack_into(encoding, data, offset, value)
                if name != "crc":
                    data = repair_crc(data)
                write_archives(scratch, first_record=data)
                result = run_script(RESEARCH / "summarize_corpus.py", artifacts=scratch)
                self.assertEqual(1, result.returncode)
                self.assertEqual(b"", result.stdout)
                self.assertEqual("AssertionError", result.stderr.decode("utf-8").splitlines()[-1])

    def test_private_pinned_archives_reproduce_committed_corpus_summary(self):
        artifacts = os.environ.get("SAVETOOLKIT_ARTIFACTS")
        if not artifacts or any(not (Path(artifacts) / f"{title}_saves_dec.zip").is_file()
                                for title in TITLES):
            self.skipTest("Private pinned archives unavailable; synthetic corpus stages remain covered offline.")
        expected = checkout_bytes(RESEARCH / "corpus-summary.json")
        for title in json.loads(expected)["titles"]:
            digest = hashlib.sha256((Path(artifacts) / title["archive"]).read_bytes()).hexdigest()
            self.assertEqual(title["archiveSha256"], digest, title["archive"])
        result = run_script(RESEARCH / "summarize_corpus.py", "--summary", artifacts=artifacts)
        self.assertEqual(0, result.returncode, result.stderr.decode("utf-8"))
        self.assertEqual(expected, result.stdout)
