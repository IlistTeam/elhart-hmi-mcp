"""把技能与配套 MCP 源码组装为独立下载包；仅打包白名单源码，不带运行产物或厂商资源。"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent
MCP_FILES = ('server.py', 'runtime_profile.py', 'canvas_spec.py', 'visible_host.py', 'build_snapshot.py', 'desktop_process.py',
             'isolated_build_job.py', 'isolated_build_manager.py', 'isolated_compiler.py',
             'build.ps1', 'install-visible-host.ps1', 'mcp.example.json')


def package_files(profile='elhart'):
    """映射仓库源码到 skills 为根、mcp 为子目录的结构；拒绝外部路径和意外文件类型。"""
    if profile not in ('elhart','fstudio'):
        raise ValueError('Unknown package profile')
    prefix='elhart-hmi-mcp' if profile=='elhart' else 'fstudio-skills/mcp'
    files = {}
    if profile=='fstudio':
        skill = ROOT/'skills/fstudio-skills'
        files['fstudio-skills/LICENSE'] = ROOT/'LICENSE'
        for path in sorted(skill.rglob('*')):
            if not path.is_file() or '__pycache__' in path.parts:
                continue
            if path.suffix not in ('.md', '.yaml', '.py', '.ps1'):
                raise ValueError('Unexpected skill resource: ' + str(path))
            files['fstudio-skills/' + path.relative_to(skill).as_posix()] = path
    else:
        for name in ('LICENSE','README.md','PLAN.md','docs/elhart-installation.md','docs/elhart-validation.md','docs/elhart-capabilities.md','docs/elhart-manual-acceptance.md'):
            files[prefix+'/'+name]=ROOT/name
    for name in MCP_FILES:
        files[prefix+'/' + name] = ROOT/name
    for path in sorted((ROOT/'src').glob('*.cs')):
        files[prefix+'/src/' + path.name] = path
    for path in files.values():
        if not path.is_file() or not path.resolve().is_relative_to(ROOT):
            raise ValueError('Missing or external package source: ' + str(path))
    return files


def build_package(output,profile='elhart'):
    """先写临时 ZIP 并逐项回读比对，验证后原子替换下载包；失败保留旧包。"""
    output = output.resolve()
    if output.suffix.lower() != '.zip':
        raise ValueError('Output must be a .zip file')
    files = package_files(profile)
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(prefix='fstudio-skills-', suffix='.tmp', dir=output.parent, delete=False) as handle:
        temporary = Path(handle.name)
    try:
        with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED) as archive:
            for name, path in files.items():
                archive.writestr(name, path.read_bytes())
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip() is not None or set(archive.namelist()) != set(files):
                raise ValueError('Package verification failed')
            for name, path in files.items():
                if archive.read(name) != path.read_bytes():
                    raise ValueError('Package content differs from source: ' + name)
        temporary.replace(output)
    finally:
        # 仅清理本次创建的临时文件，绝不递归清理技能目录或用户工程。
        temporary.unlink(missing_ok=True)
    return {'package': str(output), 'files': len(files), 'bytes': output.stat().st_size,
            'sha256': hashlib.sha256(output.read_bytes()).hexdigest(), 'includes_mcp_source': True,
            'profile':profile,'includes_native_project_skill':profile=='fstudio'}


def main():
    """接收可选输出路径，生成含 MCP 的完整技能包并打印可核验的摘要。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profile',choices=('elhart','fstudio'),default='elhart')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    output=args.output or ROOT/('artifacts/skill-delivery/Elhart_MCP_source.zip' if args.profile=='elhart' else 'artifacts/skill-delivery/Fstudio_skills.zip')
    print(json.dumps(build_package(output,args.profile), ensure_ascii=False))


if __name__ == '__main__':
    main()
