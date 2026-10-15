"""Cleanup must remove only the owned snapshot from the selected OEM history."""
from pathlib import Path
import tempfile
import unittest
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_snapshot import prune_recent_projects


class RecentProjectCleanupTests(unittest.TestCase):
    def test_exact_owned_snapshot_only(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            own = root/'fsmcp-own/p/demo.fsprj'
            other = root/'fsmcp-other/p/demo.fsprj'
            user = root/'real-project/demo.fsprj'
            recent = root/'RecentProjects.xml'
            recent.write_text(f'<Projects><Project Value="{own}"/><Project Value="{other}"/><Project Value="{user}"/></Projects>', encoding='utf8')
            self.assertEqual(prune_recent_projects(own, recent), 1)
            actual = recent.read_text(encoding='utf-8-sig')
            self.assertNotIn(str(own), actual)
            self.assertIn(str(other), actual)
            self.assertIn(str(user), actual)
            self.assertEqual(prune_recent_projects(own, recent), 0)

    def test_no_implicit_global_cleanup(self):
        self.assertEqual(prune_recent_projects(), 0)


if __name__ == '__main__':
    unittest.main()
