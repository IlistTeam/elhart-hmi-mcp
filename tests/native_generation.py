"""在独立桌面和新建工程中验证批量生成；从不操作用户当前工程。

baseline 编译指定 Git 提交的宿主源码，默认固定旧版 3a03219；optimized 编译工作树宿主。
两者使用同样的 24 个矩形和 3 页测试，只比较本地调用/生成/回读耗时，
不包含 AI 推理和网络时间。测试工程、源码副本、日志、结果都保留供复核。
"""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from desktop_process import DesktopProcess
from server import DEFAULT_BIN, FStudio
from visible_host import VisibleHost


class ObservedHost(VisibleHost):
    """分别记录实际管道往返和逻辑操作数，查询原任务不重复提交修改。"""
    def __init__(self, endpoint):
        super().__init__(endpoint)
        self.requests = self.operations = 0

    def request(self, op, **args):
        self.requests += 1
        if op != 'job':
            self.operations += 1
        return super().request(op, **args)

    def complete(self, op, **args):
        """所有响应先判终态，再判观察期限；失败记录保留原生堆栈。"""
        result = self.request(op, **args)
        deadline = time.monotonic() + 120
        while 'job_id' in result:
            if result['status'] == 'completed':
                return result['result']
            if result['status'] == 'failed':
                raise RuntimeError(result['error'])
            if time.monotonic() > deadline:
                raise TimeoutError(json.dumps(result))
            time.sleep(.01)
            result = self.request('job', job_id=result['job_id'])
        return result


def compile_host(mode, directory, baseline_ref='3a03219'):
    """编译独立 DLL，不覆盖已安装插件或宿主设置；基线从 Git 中读取。"""
    sources = directory / 'src'
    sources.mkdir(parents=True)
    names = ['VisibleHost.cs', 'LiveModels.cs', 'ProjectCreation.cs', 'NativeDrawing.cs',
             'BackgroundBuild.cs', 'BuildLogCapture.cs', 'NativePages.cs']
    if mode == 'baseline':
        # 从对应版本的安装脚本取得其完整宿主文件清单，避免新版基线漏编新增源文件。
        import re
        installer = subprocess.check_output(['git', 'show', f'{baseline_ref}:install-visible-host.ps1'], cwd=ROOT).decode('utf8')
        names = list(dict.fromkeys(re.findall(r'src\\([A-Za-z_][A-Za-z_0-9]*\.cs)', installer)))
        if not names:
            raise ValueError('Baseline installer has no host source list')
    else:
        names.append('BatchDrawing.cs')
    for name in names:
        target = sources / name
        if mode == 'baseline':
            target.write_bytes(subprocess.check_output(['git', 'show', f'{baseline_ref}:src/{name}'], cwd=ROOT))
        else:
            shutil.copy2(ROOT / 'src' / name, target)
    if mode == 'optimized':
        # 测试专用渲染入口只注入临时副本，不向生产宿主增加截图或任意代码能力。
        host_source = sources / 'VisibleHost.cs'
        source = host_source.read_text(encoding='utf-8-sig')
        marker = 'string op=S(a,"op");'
        assert source.count(marker) == 1
        source = source.replace(marker, marker+'\n   if(op=="test_render_canvas")return RenderCanvasForTest(S(a,"file"));'
                                +'\n   if(op=="test_history"||op=="test_undo"||op=="test_redo")return NativeHistoryForTest(op);')
        source = source.replace('op=="build_diagnostics"?DispatcherPriority.ContextIdle',
                                '(op=="build_diagnostics"||op=="test_render_canvas")?DispatcherPriority.ContextIdle')
        host_source.write_text(source, encoding='utf8')
        shutil.copy2(ROOT/'tests/native_probe.cs', sources/'NativeProbe.cs')
        names.append('NativeProbe.cs')
    framework = Path(r'C:\Windows\Microsoft.NET\Framework\v4.0.30319')
    addin = directory / 'addin'
    addin.mkdir()
    args = [str(framework / 'csc.exe'), '/nologo', '/target:library', '/platform:x86',
            f'/out:{addin / "FStudioMcp.Visible.dll"}', f'/r:{DEFAULT_BIN / "ICSharpCode.Core.dll"}',
            '/r:System.Core.dll', '/r:System.Xaml.dll', '/r:System.Web.Extensions.dll']
    args.extend(f'/r:{framework / "WPF" / name}' for name in ('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll'))
    args.extend(str(sources / name) for name in names)
    built = subprocess.run(args, capture_output=True, text=True)
    (directory / 'compile.log').write_text(built.stdout + built.stderr, encoding='utf8')
    if built.returncode:
        raise RuntimeError(built.stdout + built.stderr)
    shutil.copy2(ROOT / 'artifacts/visible-addin/FStudioMcp.addin', addin / 'FStudioMcp.addin')
    return addin


def geometry_legacy(host):
    """用旧接口完成与快照相同的名称、UUID、位置/尺寸回读。"""
    state = host.complete('drawing_state')
    objects = host.complete('read_member', ref=state['graphics']['$ref'])['items']
    rows = []
    for item in objects:
        ref = item['$ref']
        name = host.complete('read_member', ref=ref, member='Comment')['value']
        unique = host.complete('read_member', ref=ref, member='UniqueId')['value']
        position = host.complete('read_member', ref=ref, member='Position')['value']['$ref']
        bounds = {key.lower(): host.complete('read_member', ref=position, member=key)['value']
                  for key in ('X', 'Y', 'Width', 'Height')}
        rows.append(dict(name=name, uuid=unique, bounds=bounds, model=item))
    return {'items': rows}


def measure(host, action):
    """同时记录逻辑操作与真实往返，避免将调用次数减少冒充耗时倍数。"""
    before = host.operations, host.requests
    started = time.perf_counter()
    value = action()
    return value, dict(elapsed_ms=(time.perf_counter()-started)*1000,
                       operations=host.operations-before[0], pipe_requests=host.requests-before[1])


def run(mode, output, baseline_ref='3a03219'):
    token = uuid.uuid4().hex[:8]
    directory = output / f'{mode}-{token}'
    directory.mkdir(parents=True)
    workspace = ROOT / 'projects' / f'perf-{token}'
    service = FStudio(workspace, DEFAULT_BIN)
    process = None
    report = dict(mode=mode, test_scope='本地宿主调用；不含 AI/网络', rounds=[], artifact_dir=str(directory))
    report['baseline_ref'] = subprocess.check_output(['git','rev-parse',baseline_ref],cwd=ROOT,text=True).strip()
    try:
        addin = compile_host(mode, directory, baseline_ref)
        state = directory / 'host'
        (addin / 'host-settings.json').write_text(json.dumps(dict(workspace=str(workspace), state_directory=str(state))), encoding='utf8')
        host = ObservedHost(state / 'endpoint.json')
        process = DesktopProcess(DEFAULT_BIN / 'FStudio.exe', ['-addindir:'+str(addin)], cwd=DEFAULT_BIN)
        deadline = time.monotonic()+90
        while not host.endpoint.is_file():
            if process.poll() is not None or time.monotonic()>deadline:
                raise RuntimeError('独立宿主未就绪: '+json.dumps(process.windows(), ensure_ascii=False))
            time.sleep(.25)
        report['health'] = host.complete('health')
        assert report['health']['pid'] == process.pid
        # 安装包模板本身可能是旧版本；用原生创建器完成版本迁移，避免升级对话框。
        with zipfile.ZipFile(service._template('F010'), metadata_encoding='gbk') as template:
            xml = template.read('HMI/System/ConnectInfo.cfg').decode('utf-8-sig')
        native_created = host.complete('create_project', name='sample', model='F010', template_xml=xml)
        created = {'entry': native_created['project']}
        report['project'] = created['entry']
        print(json.dumps(dict(mode=mode, phase='host_ready', pid=process.pid)), flush=True)
        for repeat in range(3):
            host.complete('page_create', name=f'性能测试{repeat}', width=640, height=480)
            pages = host.complete('pages_list')['pages']
            page = next(p for p in pages if p['name']==f'性能测试{repeat}')
            host.complete('page_open', uuid=page['uuid'])
            widgets = [dict(name=f'box{i:02}', tool='Flexem.Studio.GraphicsDesigner.Tools.RectangleTool',
                            x=20+(i%6)*98, y=20+(i//6)*90, width=80, height=60) for i in range(24)]
            history_before = host.complete('test_history') if mode=='optimized' else None
            def generate():
                if mode == 'optimized':
                    return host.complete('drawing_create_many', page_uuid=page['uuid'], widgets=widgets)
                result = []
                for widget in widgets:
                    draft = host.complete('drawing_prepare', tool=widget['tool'])
                    result.append(host.complete('drawing_commit', draft_id=draft['draft_id'], **{k:v for k,v in widget.items() if k!='tool'}))
                return result
            generated, generation_time = measure(host, generate)
            snapshot, read_time = measure(host, lambda: host.complete('drawing_snapshot', page_uuid=page['uuid']) if mode=='optimized' else geometry_legacy(host))
            assert len(snapshot['items']) == 24, snapshot
            for spec, actual in zip(widgets, snapshot['items']):
                assert actual['name'] == spec['name'], actual
                assert actual['bounds'] == {k:spec[k] for k in ('x','y','width','height')}, actual
            if mode == 'optimized':
                after = host.complete('test_history')
                assert len(after['undo']) == len(history_before['undo'])+1, (history_before, after)
                identities = [item['uuid'] for item in snapshot['items']]
                host.complete('test_undo')
                assert host.complete('drawing_snapshot')['items'] == []
                host.complete('test_redo')
                assert [item['uuid'] for item in host.complete('drawing_snapshot')['items']] == identities
            report['rounds'].append(dict(generation=generation_time, readback=read_time, page_uuid=page['uuid']))
            (directory / f'page-{repeat}.json').write_text(json.dumps(dict(generated=generated,snapshot=snapshot), ensure_ascii=False, indent=2), encoding='utf8')
            print(json.dumps(dict(mode=mode, round=repeat, generation=generation_time, readback=read_time)), flush=True)
        if mode == 'optimized':
            quality(host, report, directory)
        host.complete('save_project')
        host.complete('close_project')
        host.complete('open_project', file=created['entry'])
        for row in report['rounds']:
            host.complete('page_open', uuid=row['page_uuid'])
            snapshot = host.complete('drawing_snapshot') if mode=='optimized' else geometry_legacy(host)
            assert len(snapshot['items']) == 24
            assert len({item['uuid'] for item in snapshot['items']}) == 24
        report['saved_and_reopened'] = True
        if mode == 'optimized':
            host.complete('page_open', uuid=report['quality_page'])
            reopened = host.complete('drawing_snapshot')
            (directory / 'quality-reopened.json').write_text(json.dumps(reopened, ensure_ascii=False, indent=2), encoding='utf8')
            assert len(reopened['items']) == 4
            ids = host.complete('test_history')['component_ids']
            assert len({(item['type'],item['id']) for item in ids}) == len(ids), ids
            report['component_ids_after_reopen'] = ids
            report['rendered_canvas'] = host.complete('test_render_canvas', file=str(directory/'quality-native.png'))
            task = host.complete('build_start')
            build_deadline = time.monotonic()+180
            while True:
                build = host.complete('build_status', build_id=task['build_id'])
                if build['status'] in ('completed','failed','cancelled','ui_required'):
                    break
                if time.monotonic()>build_deadline:
                    raise TimeoutError('Native build still running: '+json.dumps(build))
                time.sleep(.25)
            report['native_build'] = build
            assert build.get('compiler_success') is True, build
        report['success'] = True
    except BaseException as error:
        report['success'] = False
        report['error'] = str(error)
        if process is not None:
            report['windows'] = process.windows()
        raise
    finally:
        if process is not None:
            process.close()
        service.native.close()
        (directory / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
        print(json.dumps(dict(report=str(directory / 'report.json'), success=report.get('success')), ensure_ascii=False), flush=True)


def quality(host, report, directory):
    """覆盖文字、输入地址、失败无残留、错页防护与批量属性实际回读。"""
    host.complete('page_create', name='质量回归', width=640, height=480)
    page = next(p for p in host.complete('pages_list')['pages'] if p['name']=='质量回归')
    report['quality_page'] = page['uuid']
    host.complete('page_open', uuid=page['uuid'])
    widgets = [dict(name='title',tool='Flexem.Studio.GraphicsDesigner.Tools.StaticTextTool',
                    text='批量生成质量测试 ABC 123',text_style=dict(font_size=24,bold=True,color='#FF143D59'),x=20,y=20,width=500,height=50),
               dict(name='caption',tool='Flexem.Studio.GraphicsDesigner.Tools.StaticTextTool',
                    text='目标值',text_style=dict(font_size=18,color='#FF253746'),x=20,y=110,width=130,height=45),
               dict(name='target',tool='Flexem.Studio.GraphicsDesigner.Tools.NumericCharTool',
                    tool_properties={'OperationAttrib':'NumericInput'},x=180,y=110,width=180,height=55,
                    properties=[{'member':'NormalData.IntegerValue','value':4},{'member':'NormalData.DecimalValue','value':1}],
                    local_addresses=[{'member':'NormalData.ReadAddress','address':120}]),
               dict(name='target2',tool='Flexem.Studio.GraphicsDesigner.Tools.NumericCharTool',
                    tool_properties={'OperationAttrib':'NumericInput'},x=180,y=200,width=180,height=55,
                    properties=[{'member':'NormalData.IntegerValue','value':4},{'member':'NormalData.DecimalValue','value':1}],
                    local_addresses=[{'member':'NormalData.ReadAddress','address':121}])]
    result = host.complete('drawing_create_many', widgets=widgets, page_uuid=page['uuid'])
    report['quality_created'] = result
    snapshot = host.complete('drawing_snapshot')
    (directory / 'quality.json').write_text(json.dumps(snapshot, ensure_ascii=False, indent=2), encoding='utf8')
    assert len(snapshot['items']) == 4
    # 已发布批量接口会给项目中所有初始化语言赋值；不能只填当前语言而让其他语言静默空白。
    title_ref = snapshot['items'][0]['model']['$ref']
    labels = host.complete('read_member',ref=title_ref,member='NormalInfo.StaticTextContentInfos')['items']
    label_values = host.complete('read_members',reads=[
        {'ref':label['$ref'],'member':member}
        for label in labels for member in ('LabelContent','FontStyleInfo.Font.FontType')])['results']
    assert len(labels) >= 2
    assert [item['value'] for item in label_values] == [value for _ in labels for value in (widgets[0]['text'],'Graphic')]
    report['language_label_count'] = len(labels)
    # 后项无效属性必须整批拒绝，前项草稿不能留在画布或草稿列表里。
    invalid = [dict(widgets[1],name='rollback.good'), dict(widgets[1],name='rollback.bad',properties=[{'member':'MissingProperty','value':1}])]
    try:
        host.complete('drawing_create_many', widgets=invalid)
        raise AssertionError('Invalid batch was accepted')
    except RuntimeError:
        pass
    assert len(host.complete('drawing_snapshot')['items']) == 4
    assert host.complete('drawing_state')['draft_count'] == 0
    for args in ({'widgets':widgets}, {'widgets':[dict(widgets[1],name='wrongpage')], 'page_uuid':str(uuid.uuid4())}):
        try:
            host.complete('drawing_create_many', **args)
            raise AssertionError('Duplicate/wrong-page batch was accepted')
        except RuntimeError:
            pass
    selected = host.complete('drawing_snapshot', names=['title','target','absent'])
    assert selected['missing_names'] == ['absent'] and len(selected['items']) == 2
    refs = [item['model']['$ref'] for item in snapshot['items'][:2]]
    host.complete('apply_members', refs=refs, properties=[{'member':'Position.X','value':30}])
    read = host.complete('read_members', reads=[{'ref':ref,'member':'Position.X'} for ref in refs])
    assert [row['value'] for row in read['results']] == [30,30]
    report['quality_assertions'] = ['中英文文字与原生数值输入创建','本地地址配置','同类控件编号保存重开',
                                    '后项非法时草稿与画布无残留','重复名称拒绝','错页拒绝','名称筛选缺失回报','批量点路径写入回读']
    # 保留旧 prepare/commit 的默认语义，专门覆盖旧草稿跨过新批量创建后的编号冲突。
    host.complete('page_create', name='旧接口与默认文字', width=640, height=480)
    compatibility = next(p for p in host.complete('pages_list')['pages'] if p['name']=='旧接口与默认文字')
    host.complete('page_open', uuid=compatibility['uuid'])
    draft = host.complete('drawing_prepare', tool=widgets[2]['tool'], tool_properties={'OperationAttrib':'NumericInput'})
    address = host.complete('read_member', ref=draft['model']['$ref'], member='NormalData.ReadAddress')['value']['$ref']
    host.complete('drawing_configure_local_address', ref=address, address=130)
    # 空静态文本会被原生编译器拒绝，批量入口应在创建之前明确阻止，旧草稿保持可提交。
    try:
        host.complete('drawing_create_many', widgets=[
            {'name':'default.text','tool':widgets[0]['tool'],'x':20,'y':20,'width':100,'height':40}])
        raise AssertionError('Empty static text was accepted')
    except RuntimeError as error:
        assert 'nonempty text' in str(error), error
    assert host.complete('drawing_snapshot')['items'] == []
    host.complete('drawing_create_many', widgets=[dict(widgets[2],name='interleaved.batch')])
    host.complete('drawing_commit', draft_id=draft['draft_id'], name='interleaved.legacy', x=180,y=200,width=180,height=55)
    ids = host.complete('test_history')['component_ids']
    assert len({(item['type'],item['id']) for item in ids}) == len(ids), ids
    report['interleaved_component_ids'] = ids
    report['compatibility_page'] = compatibility['uuid']
    report['quality_assertions'].extend(['空静态文字在创建前拒绝','旧草稿在新批量后提交的编号冲突修复'])
    host.complete('page_open', uuid=page['uuid'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', choices=('baseline','optimized'), required=True)
    parser.add_argument('--output', type=Path, default=ROOT/'artifacts/generation-benchmark')
    parser.add_argument('--baseline-ref', default='3a03219', help='固定基线 Git 提交，默认 3a03219；结果记录完整提交号')
    args = parser.parse_args()
    run(args.mode, args.output.resolve(), args.baseline_ref)
