"""批量画布声明的纯数据展开；在连接宿主前拒绝歧义和超限输入。

预设只携带可复用样式及模式。名称和边界仍由每个控件明确提供，
这里不读取原生模型、不缓存对象句柄，也不修改调用者传入的字典。
"""
from copy import deepcopy
import math


def _require_scalar(value, field):
    """新建声明只接受标量，防止导入会话句柄后修改其他控件的共享子模型。"""
    if value is None or type(value) in (bool, str, int):
        return
    if type(value) is float and math.isfinite(value):
        return
    raise ValueError(f'{field}: expected a finite JSON scalar, not a model reference or collection')


def _merge_paths(base, override, field):
    """按属性路径合并，控件覆盖预设；同一声明内部重复路径视为错误。"""
    merged = {}
    for entries in (base, override):
        seen = set()
        for entry in entries:
            path = entry['member']
            if path in seen:
                raise ValueError(f'{field}: duplicate member {path}')
            seen.add(path)
            # 更新已有键不会改变顺序，原生存在先设模式再设参数的依赖。
            merged[path] = deepcopy(entry)
    return list(merged.values())


def expand_widgets(widgets, presets=None):
    """展开已通过 JSON schema 校验的声明，并核对展开后的实际工作量。

    最多 30 个控件、20 个预设、每控件 60 个属性和 8 个本地地址，
    全批最多 1200 个属性。引用不存在的预设、重复名称及身份/边界
    属性都在发出任何宿主修改之前失败，避免生成一半才发现声明有误。
    """
    presets = presets or {}
    if not 1 <= len(widgets) <= 30 or len(presets) > 20:
        raise ValueError('Expected 1..30 widgets and at most 20 presets')
    result, names, total = [], set(), 0
    for widget in widgets:
        name = widget['name']
        if not name.strip() or name in names:
            raise ValueError(f'Widget name is blank or duplicated: {name!r}')
        names.add(name)
        preset_name = widget.get('preset')
        if preset_name is not None and preset_name not in presets:
            raise ValueError(f'Unknown preset: {preset_name}')
        base = presets.get(preset_name, {})
        merged = deepcopy(base)
        merged.update(deepcopy({key: value for key, value in widget.items() if key != 'preset'}))
        for field in ('tool_properties', 'text_style'):
            if field in base or field in widget:
                merged[field] = deepcopy(base.get(field, {}))
                merged[field].update(deepcopy(widget.get(field, {})))
        for field in ('properties', 'local_addresses'):
            if field in base or field in widget:
                merged[field] = _merge_paths(base.get(field, []), widget.get(field, []), field)
        if not merged.get('tool'):
            raise ValueError(f'{name}: tool is required on the widget or its preset')
        properties = merged.get('properties', [])
        addresses = merged.get('local_addresses', [])
        if len(properties) > 60 or len(addresses) > 8:
            raise ValueError(f'{name}: expanded property/address limit exceeded')
        for entry in properties:
            # 身份和最终边界由批量操作管理；禁止预设覆盖它们破坏定位或布局。
            if entry['member'].split('.')[0] in ('Position', 'Comment', 'UniqueId', 'Id', 'Name', 'DisplayName', 'GraphicsName'):
                raise ValueError(f'{name}: identity/bounds property is reserved: {entry["member"]}')
            _require_scalar(entry['value'], f'{name}.{entry["member"]}')
        for key, value in merged.get('tool_properties', {}).items():
            _require_scalar(value, f'{name}.tool_properties.{key}')
        if ('text' in merged or 'text_style' in merged) and merged['tool'] != 'Flexem.Studio.GraphicsDesigner.Tools.StaticTextTool':
            raise ValueError(f'{name}: text/text_style requires StaticTextTool')
        # 原生编译规则要求至少一种语言的标签非空，不能把不完整文字控件当成完成的批量声明。
        if merged['tool'] == 'Flexem.Studio.GraphicsDesigner.Tools.StaticTextTool':
            if not isinstance(merged.get('text'), str) or not merged['text'].strip():
                raise ValueError(f'{name}: StaticTextTool requires nonempty text; use canvas_prepare for staged configuration')
        total += len(properties)
        if total > 1200:
            raise ValueError('Expanded batch exceeds 1200 properties')
        result.append(merged)
    return result
