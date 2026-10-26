"""Run the vendor's mandatory post-compilation packaging on an owned snapshot."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
from build_snapshot import input_manifest

SUPPORTED_PACKAGING = {'Flexem.Studio.Build.dll': '10f6b9ade138d6806bbf9c6b538196abb048103cb7a2dad4e02e1a2990baeda3', 'Flexem.Studio.Build.Common.dll': '0682ab81c20ec11678ca33b5929da201995124b8f186de179f66290cbada7750', 'Flexem.Core.dll': 'aad11d110bbb7eb46c8737b6885a29446d759e8e07fb5c133ddadfe27865ea19', 'pack.dll': '5bbe8ba2d20ff3094b6453b821eec1bbec12972221b759f41377462e9029d1da'}


def binary_hashes(bin_dir):
    return {name:hashlib.sha256((Path(bin_dir)/name).read_bytes()).hexdigest() for name in SUPPORTED_PACKAGING}


def package_snapshot(snapshot,bin_dir):
    binaries=binary_hashes(bin_dir)
    if not binaries or binaries!=SUPPORTED_PACKAGING:raise RuntimeError('Unsupported installed native packaging binaries')
    project=snapshot.project.resolve(strict=True);owner=snapshot.owner.resolve(strict=True)
    if not project.is_relative_to(owner) or project.parent==owner:raise ValueError('Packaging requires an owned project snapshot')
    before_inputs=input_manifest(project.parent);before_outputs=input_manifest(project.parent/'Bin')
    if not before_outputs.get('fshmi.bin',{}).get('size'):raise ValueError('Compiled fshmi.bin missing')
    executable=Path(os.environ.get('WINDIR',r'C:\Windows'))/'SysWOW64/WindowsPowerShell/v1.0/powershell.exe'
    helper=Path(__file__).resolve().parent/'scripts/package_native_project.ps1'
    result=subprocess.run([str(executable),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(helper),
        '-ProjectDirectory',str(project.parent),'-OwnedRoot',str(owner),'-VendorBin',str(Path(bin_dir).resolve())],
        cwd=bin_dir,stdin=subprocess.DEVNULL,capture_output=True,timeout=120,creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:raise RuntimeError('Native packaging failed: '+result.stderr.decode('utf8',errors='replace')[-3000:])
    reply=json.loads(result.stdout.decode('utf8',errors='replace').strip().splitlines()[-1])
    if reply.get('packaging_returned') is not True or reply.get('info_exists') is not True:raise RuntimeError('Native packaging did not produce info.dat')
    if input_manifest(project.parent)!=before_inputs:raise RuntimeError('Native packaging changed saved project inputs')
    outputs=input_manifest(project.parent/'Bin')
    if outputs.get('info.dat',{}).get('size')!=1024:raise RuntimeError('Native packaging produced invalid info.dat size')
    if not outputs.get('fshmi.bin',{}).get('size'):raise RuntimeError('Native packaging lost fshmi.bin')
    record=dict(status='completed',backend='installed_editor_packaging_wrapper',binaries=binaries,
                compiled_bin_sha256=before_outputs['fshmi.bin']['sha256'],outputs=outputs)
    snapshot.write_record('packaging.json',record)
    return record
