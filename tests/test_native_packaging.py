import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock,patch
import native_packaging as pack


class NativePackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.directory=self.root/'p';self.directory.mkdir()
        self.project=self.directory/'p.fsprj';self.project.write_text('<FsProject/>')
        self.outputs=self.directory/'Bin';self.outputs.mkdir();(self.outputs/'fshmi.bin').write_bytes(b'compiled')
        self.snapshot=SimpleNamespace(project=self.project,owner=self.root,write_record=Mock())
        self.hashes=patch.object(pack,'binary_hashes',return_value=pack.SUPPORTED_PACKAGING);self.hashes.start();self.addCleanup(self.hashes.stop)

    def invoke(self,behavior):
        with patch.object(pack.subprocess,'run',side_effect=behavior):return pack.package_snapshot(self.snapshot,self.root)

    def good(self,*args,**kwargs):
        (self.outputs/'fshmi.bin').write_bytes(b'natively packaged')
        (self.outputs/'info.dat').write_bytes(bytes(1024))
        return subprocess.CompletedProcess(args[0],0,b'{"packaging_returned":true,"info_exists":true}',b'')

    def test_native_output_and_receipt_preserved(self):
        result=self.invoke(self.good)
        self.assertEqual(result['status'],'completed');self.assertEqual(result['outputs']['info.dat']['size'],1024)
        self.snapshot.write_record.assert_called_once_with('packaging.json',result)
        self.assertNotEqual(result['compiled_bin_sha256'],result['outputs']['fshmi.bin']['sha256'])
    def test_failure_never_certifies_outputs(self):
        with self.assertRaisesRegex(RuntimeError,'packaging failed'):
            self.invoke(lambda *a,**kw:subprocess.CompletedProcess(a[0],1,b'',b'failure'))
        self.snapshot.write_record.assert_not_called()
    def test_missing_sidecar_is_rejected(self):
        with self.assertRaises((RuntimeError,KeyError)):
            self.invoke(lambda *a,**kw:subprocess.CompletedProcess(a[0],0,b'{"packaging_returned":true,"info_exists":false}',b''))
        self.snapshot.write_record.assert_not_called()
    def test_input_mutation_is_rejected(self):
        def mutate(*a,**kw):
            self.project.write_text('changed');return self.good(*a,**kw)
        with self.assertRaisesRegex(RuntimeError,'changed saved'):self.invoke(mutate)
        self.snapshot.write_record.assert_not_called()
    def test_unknown_vendor_binaries_rejected_before_invocation(self):
        with patch.object(pack,'binary_hashes',return_value={'pack.dll':'unknown'}),patch.object(pack.subprocess,'run') as run:
            with self.assertRaisesRegex(RuntimeError,'Unsupported'):pack.package_snapshot(self.snapshot,self.root)
        run.assert_not_called()


if __name__=='__main__':unittest.main()
