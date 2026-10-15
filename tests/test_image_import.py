import base64
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import Mock
from server import FStudio


class ImageImportTests(unittest.TestCase):
    def test_pending_state_is_observed_before_native_import(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);source=root/'alpha.png'
            source.write_bytes(base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg=='))
            service=FStudio.__new__(FStudio);service.profile=SimpleNamespace(name='elhart')
            service.visible=Mock()
            service.visible.read.side_effect=[{'job_id':'state-one','status':'queued'},
                {'value':{'$ref':'normal'}},{'value':None},{'value':'None'}]
            service.visible.request.side_effect=[{'job_id':'state-one','status':'completed',
                'result':{'project':str(root/'project.fsprj')}},
                {'job_id':'import-one','status':'completed','result':{'picture_origin':'File','xml_repair_required':False}}]
            result=service.import_image('picture',str(source))
            self.assertEqual(result['native']['picture_origin'],'File')
            self.assertFalse(result['repair_origin']['required'])
            self.assertEqual([call.args[0] for call in service.visible.request.call_args_list],['job','drawing_import_image'])
            self.assertEqual(service.visible.request.call_args_list[0].kwargs,{'job_id':'state-one'})


if __name__=='__main__':unittest.main()
