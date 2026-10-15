"""Generated Elhart compiler files must not become project snapshot inputs."""
from pathlib import Path
import tempfile
import unittest
from build_snapshot import input_manifest


class SnapshotInputTests(unittest.TestCase):
    def test_elhart_outputs_excluded_but_project_and_database_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            for name in ('Bin/fshmi.bin','Subbin/Images/state.png','Subbin/Fonts/ARIAL.TTF',
                         'HMI/System/fs2.s3db','HMI/Lib/Graphics/state.png','demo.fsprj'):
                path=root/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'test')
            self.assertEqual(set(input_manifest(root)),{'HMI/System/fs2.s3db','HMI/Lib/Graphics/state.png','demo.fsprj'})
