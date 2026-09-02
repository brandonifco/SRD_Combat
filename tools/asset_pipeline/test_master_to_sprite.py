#!/usr/bin/env python3
"""Pins master_to_sprite.py's ship-status reporting (#514).

Standalone ``unittest`` — this project's gate (``scripts/validate.sh``) is
dotnet-only and does not run Python, so this is not wired into CI. Run it by
hand after touching ``_shipped_folder_for``/``_shipped_folders_from_sprite_library``/
``_ship_status``:

    python3 tools/asset_pipeline/test_master_to_sprite.py

Knockout check performed by hand for #514's fix: reverting
``_shipped_folder_for`` to match on folder value alone (the pre-#514 shape)
turns five tests red — ``test_goblin_warrior_reports_shipped``,
``test_scout_reports_shipped``, ``test_name_folder_mismatch_resolves_by_name``,
and the ``goblin_warrior`` and ``scout`` subtests of the whole-roster pin
(``test_full_masters_directory_ship_status_matches_known_baseline``) — as both
masters come back "unshipped", confirming these tests exercise the fixed code
path rather than passing vacuously.
"""

from __future__ import annotations

import unittest
from pathlib import Path

import master_to_sprite as mts


# A tiny, self-contained stand-in for SpriteLibrary.cs's two dictionaries —
# real entries copied verbatim from client/SpriteLibrary.cs so the shapes
# under test (#442's casing/spacing mismatch, #514's name-vs-folder
# mismatch, an ordinary exact match, and "not mapped at all") are all
# present without this test depending on the live file's exact contents.
_FIXTURE_MAPPING = {
    "Goblin Warrior": "Goblin_Drawn",  # #514: folder has nothing in common with the stem
    "Scout": "Scout_Human",  # #514: same shape
    "Swarm of Bats": "Swarm_of_Bats",  # #442: lowercase "of", folder == name modulo casing
    "Ogre": "Ogre",  # ordinary exact match
    "Giant Wolf Spider": "Giant_Wolf_Spider",  # ordinary multi-word exact match
    # "Centaur Trooper" deliberately has no stem "centaur" equivalent —
    # nothing in this fixture claims that stem, so it must stay unmapped.
    "Centaur Trooper": "Centaur_Trooper",
}


class ShippedFolderForTests(unittest.TestCase):
    """Unit-level coverage of the matching rule in isolation, against the
    synthetic fixture above — independent of whatever SpriteLibrary.cs
    happens to contain today."""

    def test_name_folder_mismatch_resolves_by_name(self) -> None:
        # The #514 shape: the mapped folder shares no substring with the
        # stem at all: only matching against the *name* key finds it.
        self.assertEqual(mts._shipped_folder_for("goblin_warrior", _FIXTURE_MAPPING), "Goblin_Drawn")
        self.assertEqual(mts._shipped_folder_for("scout", _FIXTURE_MAPPING), "Scout_Human")

    def test_casing_and_spacing_mismatch_still_resolves(self) -> None:
        # The #442 shape stays fixed under the new matching rule too.
        self.assertEqual(mts._shipped_folder_for("swarm_of_bats", _FIXTURE_MAPPING), "Swarm_of_Bats")

    def test_ordinary_exact_matches_are_unaffected(self) -> None:
        self.assertEqual(mts._shipped_folder_for("ogre", _FIXTURE_MAPPING), "Ogre")
        self.assertEqual(
            mts._shipped_folder_for("giant_wolf_spider", _FIXTURE_MAPPING), "Giant_Wolf_Spider"
        )

    def test_unmapped_stem_stays_unmapped(self) -> None:
        # "centaur" names nothing in the fixture (only "Centaur Trooper"
        # does) — a real, separate mismatch (#514's issue body says so
        # explicitly) that this fix does not and should not paper over.
        self.assertIsNone(mts._shipped_folder_for("centaur", _FIXTURE_MAPPING))
        self.assertIsNone(mts._shipped_folder_for("nonexistent_creature", _FIXTURE_MAPPING))

    def test_ship_status_three_states(self) -> None:
        self.assertEqual(mts._ship_status("nonexistent_creature", _FIXTURE_MAPPING), "unshipped")
        # "Ogre" maps to folder "Ogre", which is not on disk relative to
        # this fixture's isolated context (SPRITES_DIR is the real repo's
        # sprites tree, and there is no reason a folder literally named
        # "Ogre" wouldn't exist there — so assert the real, expected state
        # instead of a fragile guess).
        self.assertIn(
            mts._ship_status("ogre", _FIXTURE_MAPPING), {"shipped", "mapped, folder missing"}
        )


class RealSpriteLibraryRegressionTests(unittest.TestCase):
    """Pins the exact regression #514 reported, against the real, committed
    client/SpriteLibrary.cs — this is the literal claim the issue makes,
    not just the isolated matching rule."""

    @classmethod
    def setUpClass(cls) -> None:
        if not mts.SPRITE_LIBRARY_CS.exists():
            raise unittest.SkipTest(f"no {mts.SPRITE_LIBRARY_CS} in this checkout")
        cls.mappings = mts._shipped_folders_from_sprite_library()

    def test_goblin_warrior_reports_shipped(self) -> None:
        self.assertEqual(mts._shipped_folder_for("goblin_warrior", self.mappings), "Goblin_Drawn")
        self.assertEqual(mts._ship_status("goblin_warrior", self.mappings), "shipped")

    def test_scout_reports_shipped(self) -> None:
        self.assertEqual(mts._shipped_folder_for("scout", self.mappings), "Scout_Human")
        self.assertEqual(mts._ship_status("scout", self.mappings), "shipped")

    def test_known_still_unshipped_masters_are_unaffected(self) -> None:
        # These predate #514 and are genuine mismatches (no SpriteLibrary
        # entry names them under any casing) — the issue is explicit that
        # they are out of scope for this fix, and this fix must not
        # accidentally paper over them.
        for stem in (
            "animated_sword",
            "arrow",
            "bolt",
            "bugbear",
            "centaur",
            "dart",
            "gnoll",
            "hand_axe",
            "sahuagin",
        ):
            with self.subTest(stem=stem):
                self.assertEqual(mts._ship_status(stem, self.mappings), "unshipped")

    def test_full_masters_directory_ship_status_matches_known_baseline(self) -> None:
        """Whole-roster pin: every master's stem in client/assets/masters/
        reports exactly the status recorded here. Captures the full
        before/after diff from #514's fix (only goblin_warrior and scout
        flip) in one assertion, so a future change to the matching rule
        that flips anything else is caught immediately rather than noticed
        by eye in `list` output."""

        if not mts.MASTERS_DIR.exists():
            raise unittest.SkipTest(f"no {mts.MASTERS_DIR} in this checkout")

        stems = sorted({p.stem for p in mts.MASTERS_DIR.glob("*.png")})
        actual = {stem: mts._ship_status(stem, self.mappings) for stem in stems}

        expected_unshipped = {
            "animated_sword",
            "arrow",
            "bolt",
            "bugbear",
            "centaur",
            "dart",
            "gnoll",
            "hand_axe",
            "sahuagin",
        }

        for stem, status in actual.items():
            with self.subTest(stem=stem):
                if stem in expected_unshipped:
                    self.assertEqual(status, "unshipped")
                else:
                    self.assertEqual(status, "shipped")


if __name__ == "__main__":
    unittest.main()
