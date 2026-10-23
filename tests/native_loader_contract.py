"""Explicit offline Win32 contract probe; NEVER calls LoaderControls.start()."""
import ctypes
import json
from pathlib import Path
import sys
import uuid
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from desktop_process import DesktopProcess
from loader_windows import LoaderControls,loader_processes
from panel_download import LOADER_SHA256,digest
from runtime_profile import default_bin


def main():
    root=Path(__file__).resolve().parents[1]/'artifacts/loader-contract'/uuid.uuid4().hex
    (root/'Bin').mkdir(parents=True);(root/'Settings').mkdir()
    loader=default_bin('elhart')/'FSTools.exe'
    if digest(loader)!=LOADER_SHA256:raise RuntimeError('Unsupported loader')
    with DesktopProcess(loader,['-d',str(root/'Bin'),'-c','en','-o','119'],cwd=root) as process:
        controls=LoaderControls(process);controls.attach();options=controls.configure(root/'Bin')
        assert process.pid in [p['pid'] for p in loader_processes()],'Owned loader missing from process snapshot'
        # Populate only our own journal with an unmistakable synthetic readback marker.
        marker=ctypes.create_unicode_buffer('MCP_OFFLINE_READBACK_PROBE')
        controls.send(controls.require(1016,'ListBox'),0x180,0,ctypes.addressof(marker))
        controls.send(controls.require(1002,'msctls_progress32'),0x402,7)
        observed=controls.observe()
        assert observed['messages']==['MCP_OFFLINE_READBACK_PROBE'],observed
        assert observed['progress']==7,observed
        assert not observed['dialogs'],observed
        controls.close()
    assert process.pid not in [p['pid'] for p in loader_processes()],'Owned loader survived cleanup'
    report=dict(control_contract_passed=True,synthetic_journal_readback=True,synthetic_progress_readback=True,
                process_guard_verified=True,options=options,transmission_started=False,hardware_success=None,cleanup_complete=True)
    (root/'evidence.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    print(json.dumps(dict(evidence=str(root/'evidence.json'),**report),ensure_ascii=False,indent=2))


if __name__=='__main__':main()
