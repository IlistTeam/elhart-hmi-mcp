"""核验完整技能 ZIP 可独立导入，且只分发明确允许的源码资源。"""
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('package_skill', ROOT/'package-skill.py')
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class ReleasePackageTests(unittest.TestCase):
    def test_distribution_is_source_only_and_complete(self):
        """包包含新 Python 依赖与唯一绘图实现，不夹带工程、日志或厂商二进制。"""
        files = packager.package_files('fstudio')
        self.assertIn('fstudio-skills/mcp/canvas_spec.py', files)
        self.assertIn('fstudio-skills/mcp/src/BatchDrawing.cs', files)
        self.assertNotIn('fstudio-skills/mcp/src/NativeDrawingBatch.cs', files)
        for name in files:
            self.assertNotIn(Path(name).suffix.lower(), ('.dll','.exe','.fsprj','.wcfg','.log','.pyc'))
            self.assertFalse(set(Path(name).parts) & {'artifacts','projects','tests','__pycache__'})

    def test_extracted_server_imports_without_repository(self):
        """在独立临时目录运行解压后的入口，防止打包遗漏依赖却在维护仓库中误通过。"""
        with tempfile.TemporaryDirectory(prefix='fstudio-package-') as directory:
            root = Path(directory)
            result = packager.build_package(root/'Fstudio_skills.zip','fstudio')
            self.assertTrue(result['includes_mcp_source'])
            with zipfile.ZipFile(result['package']) as archive:
                self.assertIsNone(archive.testzip())
                archive.extractall(root/'extracted')
            entry = root/'extracted/fstudio-skills/mcp/server.py'
            run = subprocess.run([sys.executable,str(entry),'--help'],cwd=root,capture_output=True,timeout=30)
            self.assertEqual(run.returncode,0,(run.stdout+run.stderr).decode('utf8',errors='replace'))
            self.assertIn(b'--workspace',run.stdout)

    def test_elhart_distribution_has_no_vendor_or_projects(self):
        files=packager.package_files('elhart')
        self.assertIn('elhart-hmi-mcp/src/NativeIndicators.cs',files)
        self.assertIn('elhart-hmi-mcp/docs/elhart-installation.md',files)
        for name in files:
            self.assertTrue(name.startswith('elhart-hmi-mcp/'))
            self.assertIn(Path(name).suffix.lower(),('.md','.py','.ps1','.json','.cs',''))
            self.assertFalse(set(Path(name).parts)&{'artifacts','projects','assets','tests','__pycache__'})

    def test_elhart_extracted_server_runs_without_repository(self):
        with tempfile.TemporaryDirectory(prefix='elhart-package-') as directory:
            root=Path(directory);result=packager.build_package(root/'Elhart.zip','elhart')
            with zipfile.ZipFile(result['package']) as archive:archive.extractall(root/'extracted')
            entry=root/'extracted/elhart-hmi-mcp/server.py'
            run=subprocess.run([sys.executable,str(entry),'--help'],cwd=root,capture_output=True,timeout=30)
            self.assertEqual(run.returncode,0,(run.stdout+run.stderr).decode('utf8',errors='replace'))
            self.assertIn(b'--profile',run.stdout)
            self.assertFalse(result['includes_native_project_skill'])


if __name__ == '__main__':
    unittest.main()
