"""Installed package layout, fail-closed configuration and relocated stdio launch."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class PluginPackageTests(unittest.TestCase):
    def test_components_and_skill_resources_are_self_contained(self):
        portable = json.loads((ROOT / 'plugin.json').read_text(encoding='utf-8'))
        legacy = json.loads((ROOT / '.codex-plugin/plugin.json').read_text(encoding='utf-8'))
        self.assertEqual((portable['name'], portable['version']), (legacy['name'], legacy['version']))
        primary = json.loads((ROOT / 'mcp.json').read_text(encoding='utf-8'))
        fallback = json.loads((ROOT / '.mcp.json').read_text(encoding='utf-8'))
        self.assertEqual(primary['mcpServers'], fallback['mcpServers'])
        skill = ROOT / legacy['skills'] / 'elhart-hmi-projects'
        for resource in ('SKILL.md', 'references/mcp-integration.md', 'scripts/native_xml.py',
                         'scripts/project_tool.py', 'assets/slm-ecp10-starter.zip'):
            self.assertTrue((skill / resource).is_file(), resource)
        self.assertFalse((ROOT / 'skills/fstudio-skills').exists())

    @unittest.skipUnless(os.name == 'nt', 'Windows PowerShell launcher')
    def test_relocated_launcher_preserves_stdio_and_selected_paths(self):
        with tempfile.TemporaryDirectory(prefix='elhart-plugin-') as directory:
            base = Path(directory)
            installed = base / 'installed plugin'
            scripts = installed / 'scripts'
            scripts.mkdir(parents=True)
            shutil.copyfile(ROOT / 'scripts/start-plugin.ps1', scripts / 'start-plugin.ps1')
            workspace = base / 'user projects'
            workspace.mkdir()
            studio = base / 'vendor Bin'
            studio.mkdir()
            artifacts = installed / 'artifacts/visible-addin'
            artifacts.mkdir(parents=True)
            (installed / 'artifacts/FStudioBridge.exe').touch()
            (artifacts / 'host-settings.json').write_text(json.dumps({
                'workspace': str(workspace), 'bin': str(studio), 'profile': 'elhart'}))
            # A fixture server echoes the actual native command arguments and stdin.
            # Vendor bridge/IDE integration is a separate native test, never faked here.
            (installed / 'server.py').write_text(
                'import json,sys\nprint(json.dumps({"args":sys.argv[1:],"wire":sys.stdin.readline().strip()}))\n')
            config = base / 'config.json'
            settings = {'python': sys.executable, 'workspace': str(workspace), 'studio_bin': str(studio)}
            config.write_text(json.dumps(settings))
            env = {**os.environ, 'ILIST_ELHART_CONFIG': str(config)}
            command = ['powershell.exe', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
                       '-File', str(scripts / 'start-plugin.ps1')]
            run = subprocess.run(command, input='{"jsonrpc":"2.0"}\n', text=True,
                                 encoding='utf-8', capture_output=True, env=env, cwd=base, timeout=20)
            self.assertEqual(run.returncode, 0, run.stderr)
            result = json.loads(run.stdout)
            self.assertEqual(result['args'], ['--workspace', str(workspace), '--profile', 'elhart', '--studio-bin', str(studio)])
            self.assertEqual(json.loads(result['wire']), {'jsonrpc': '2.0'})
            settings['workspace'] = str(installed)
            config.write_text(json.dumps(settings))
            run = subprocess.run(command, capture_output=True, env=env, timeout=20)
            self.assertNotEqual(run.returncode, 0)
            self.assertIn(b'outside the plugin cache', run.stderr)
            config.unlink()
            run = subprocess.run(command, capture_output=True, env=env, timeout=20)
            self.assertNotEqual(run.returncode, 0)
            self.assertIn(b'not configured', run.stderr)


if __name__ == '__main__':
    unittest.main()
