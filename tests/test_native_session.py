"""Cleanup must never kill an editor without identity and clean-state proof."""
from pathlib import Path
import unittest
from unittest.mock import Mock, patch
from native_session import TestEditor


class EditorOwnershipTests(unittest.TestCase):
    def editor(self):
        editor=TestEditor.__new__(TestEditor)
        editor.token='owner'
        editor.process=Mock(pid=123)
        editor.process.poll.return_value=None
        editor.kernel=Mock()
        editor.job=456
        editor.mutex=None
        return editor

    def test_wrong_endpoint_never_terminates(self):
        editor=self.editor();host=Mock()
        host.request.return_value={'pid':999}
        with self.assertRaisesRegex(RuntimeError,'not the editor'):
            editor.cleanup(host,[])
        editor.kernel.TerminateJobObject.assert_not_called()
        editor.process.wait.assert_not_called()

    def test_failed_native_save_leaves_process_alive(self):
        editor=self.editor();host=Mock();host.request.return_value={'pid':123}
        with patch('native_session.require_terminal',side_effect=RuntimeError('save failed')):
            with self.assertRaisesRegex(RuntimeError,'save failed'):
                editor.cleanup(host,[Path('project.fsprj')])
        editor.kernel.TerminateJobObject.assert_not_called()

    def test_dirty_acknowledgement_never_terminates(self):
        editor=self.editor();host=Mock();host.request.return_value={'pid':123}
        with patch('native_session.require_terminal',return_value={'shutdown_scheduled':True,'project':None,'dirty_views':1}):
            with self.assertRaisesRegex(RuntimeError,'safe shutdown'):
                editor.cleanup(host,[])
        editor.kernel.TerminateJobObject.assert_not_called()

    def test_clean_owned_host_and_children_are_closed(self):
        editor=self.editor();host=Mock();host.request.return_value={'pid':123}
        with patch('native_session.require_terminal',return_value={'shutdown_scheduled':True,'project':None,'dirty_views':0}) as shutdown:
            result=editor.cleanup(host,[Path('project.fsprj')])
        self.assertEqual(result['status'],'closed')
        self.assertTrue(result['child_cleanup_confirmed'])
        self.assertEqual(shutdown.call_args.kwargs['token'],'owner')
        self.assertEqual(shutdown.call_args.kwargs['pid'],123)
        editor.kernel.TerminateJobObject.assert_called_once_with(456,0)
        editor.process.wait.assert_called_once_with(timeout=10)


if __name__=='__main__':unittest.main()
