"""编译真实 LiveModels.cs 并运行独立模型桩，避免启动或修改用户的 FStudio 工程。"""

from pathlib import Path
import subprocess
import tempfile
import unittest


class ModelAccessRegressionTests(unittest.TestCase):
    def test_model_access_harness(self):
        """覆盖点路径、批量限制、句柄身份、后台保护和异常回滚。"""
        repository = Path(__file__).resolve().parents[1]
        compiler = Path(r"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe")
        if not compiler.is_file():
            self.skipTest("需要 Windows .NET Framework 4 C# 编译器")
        with tempfile.TemporaryDirectory(prefix="fstudio-model-tests-") as temporary:
            executable = Path(temporary) / "model-access-tests.exe"
            build = subprocess.run(
                [str(compiler), "/nologo", "/r:System.Core.dll", f"/out:{executable}",
                 str(repository / "src" / "LiveModels.cs"),
                 str(repository / "tests" / "model_access_harness.cs")],
                capture_output=True, text=True, errors="replace", timeout=60,
            )
            self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
            run = subprocess.run(
                [str(executable)], capture_output=True, text=True,
                errors="replace", timeout=60,
            )
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn("PASS 23 model access scenarios", run.stdout)
            print(run.stdout.strip())


if __name__ == "__main__":
    unittest.main()
