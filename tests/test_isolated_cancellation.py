import unittest
from unittest.mock import Mock,patch
from isolated_compiler import IsolatedCompiler


class IsolatedCancellationTests(unittest.TestCase):
    def session(self):
        session=IsolatedCompiler.__new__(IsolatedCompiler)
        session.process=Mock();session.process.poll.return_value=None
        session.build_id='build'
        session.phase='compiling'
        session.cancel_requested=True
        session.cancel_sent=True
        session.cancel_error=None
        session.cancel_deadline=3
        session.pending={}
        session.snapshot=Mock()
        session.close=Mock()
        return session

    def test_explicit_cancel_closes_only_owned_worker_after_grace(self):
        session=self.session()
        session.operation=Mock(return_value={'status':'running','compiler_success':None})
        with patch('isolated_compiler.time.monotonic',return_value=4):result=session.tick()
        self.assertEqual(result['status'],'cancelled')
        self.assertEqual(result['cancellation_mode'],'owned_isolated_worker_terminated')
        self.assertIsNone(result['compiler_success'])
        session.close.assert_called_once_with()

    def test_natural_completion_wins_cancel_race(self):
        session=self.session();result={'status':'completed','compiler_success':True}
        session.operation=Mock(return_value=result)
        with patch('isolated_compiler.time.monotonic',return_value=4):self.assertEqual(session.tick(),result)
        session.close.assert_not_called()

    def test_observation_delay_without_cancel_never_terminates(self):
        session=self.session();session.cancel_requested=False
        session.operation=Mock(return_value={'status':'running','compiler_success':None})
        with patch('isolated_compiler.time.monotonic',return_value=100):self.assertIsNone(session.tick())
        session.close.assert_not_called()

    def test_completed_native_task_is_preserved_while_diagnostics_are_pending(self):
        session=self.session()
        session.operation=Mock(return_value={'status':'collecting_diagnostics',
            'compiler_success':True,'native_task_completed':True})
        with patch('isolated_compiler.time.monotonic',return_value=100):self.assertIsNone(session.tick())
        session.close.assert_not_called()


if __name__=='__main__':unittest.main()
