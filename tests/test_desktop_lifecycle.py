import unittest
from unittest.mock import Mock
from desktop_process import DesktopProcess


class DesktopLifecycleTests(unittest.TestCase):
    def test_job_termination_waits_without_reterminating_exiting_process(self):
        process=DesktopProcess.__new__(DesktopProcess)
        process.kernel=Mock();process.user=Mock()
        process.kernel.WaitForSingleObject.return_value=0
        process.process=123;process.job=456;process.job_assigned=True;process.desktop=789
        process.poll=Mock(return_value=None)
        process.close()
        process.kernel.TerminateJobObject.assert_called_once_with(456,1)
        process.kernel.TerminateProcess.assert_not_called()
        process.kernel.WaitForSingleObject.assert_called_once_with(123,5000)
        process.user.CloseDesktop.assert_called_once_with(789)
        self.assertIsNone(process.process)
        self.assertIsNone(process.job)
        self.assertIsNone(process.desktop)


if __name__=='__main__':unittest.main()
