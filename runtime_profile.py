"""Installed OEM identity and paths; metadata is not a behavior certificate."""
from dataclasses import dataclass
import os
from pathlib import Path
import xml.etree.ElementTree as ET

ELHART_BIN = Path(r'C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x\Bin')
FSTUDIO_BIN = Path(r'C:\Program Files (x86)\Flexem\FStudio 3.x\Bin')


def default_bin(profile='auto'):
    if profile == 'elhart' or (profile == 'auto' and ELHART_BIN.is_dir()):
        return ELHART_BIN
    return FSTUDIO_BIN


@dataclass(frozen=True)
class RuntimeProfile:
    name: str
    bin: Path

    @classmethod
    def discover(cls, bin_dir, requested='auto'):
        bin_dir = Path(bin_dir).resolve()
        detected = 'elhart' if (bin_dir.parent/'Device/HMI/Custom/119.xml').is_file() else 'fstudio'
        if requested not in ('auto', 'elhart', 'fstudio'):
            raise ValueError('Unknown runtime profile')
        if requested == 'elhart' and detected != 'elhart':
            raise ValueError('Elhart OEM catalog Device/HMI/Custom/119.xml is missing')
        return cls(detected if requested == 'auto' else requested, bin_dir)

    @property
    def templates(self):
        # .ptpl is an OEM overlay, not a replacement for the full .tpl project.
        return self.bin.parent/'Template/HMI/zh'

    @property
    def log_file(self):
        root = Path(os.environ.get('APPDATA', Path.home()/'AppData/Roaming'))
        return root/('KIP/ELHART HMI Soft3/Logs/Log.txt' if self.name == 'elhart'
                     else 'Flexem/FStudio3/Logs/Log.txt')

    @property
    def supported_versions(self):
        return ('3.0.15569.0',) if self.name == 'elhart' else ('3.0.15685.0',)

    def model_catalog(self):
        catalog = self.bin.parent/'Device/HMI/HMI_index.CFG'
        if not catalog.is_file():
            return {}
        labels, allowed = {}, None
        if self.name == 'elhart':
            root = ET.parse(self.bin.parent/'Device/HMI/Custom/119.xml').getroot()
            for child in root:
                key = child.tag.rsplit('}', 1)[-1]
                if key == 'WhiteIds':
                    allowed = {int(x.text) for x in child}
                elif key == 'NameMappings':
                    for entry in child:
                        values = list(entry)
                        labels[int(values[0].text)] = values[1].text
        models = {}
        for item in ET.parse(catalog).getroot().iter('HMIType'):
            data = item.attrib
            identifier = int(data['ID'])
            if allowed is not None and identifier not in allowed:
                continue
            name = data['Name']
            models[name] = dict(model=name, id=identifier,
                                display_name=labels.get(identifier, name),
                                width=int(data['DisplaySizeX']), height=int(data['DisplaySizeY']),
                                runtime=data.get('Runtime'), default_color=data.get('DefaultColor'),
                                oem_overlay=str(self.overlay(identifier)) if self.overlay(identifier) else None)
        return models

    def overlay(self, identifier):
        path = self.bin.parent/f'Template/HMI/Custom/119/{identifier}.ptpl'
        return path if self.name == 'elhart' and path.is_file() else None

    def resolve_model(self, name):
        catalog = self.model_catalog()
        if name in catalog:
            return name
        aliases = [key for key, info in catalog.items()
                   if name in (info.get('display_name') or '').split('/')]
        if len(aliases) == 1:
            return aliases[0]
        if self.name == 'elhart':
            raise ValueError('Model is missing or ambiguous in the installed Elhart OEM catalog: '+name)
        return name

    def describe(self, version=None):
        return dict(name=self.name, bin=str(self.bin), templates=str(self.templates),
                    oem=119 if self.name == 'elhart' else None, log_file=str(self.log_file),
                    dto_version=version, version_supported=version in self.supported_versions,
                    supported_versions=list(self.supported_versions),
                    verification='installed_metadata_only')

    def require_supported(self, version):
        if version not in self.supported_versions:
            raise RuntimeError(f'Unsupported {self.name} DTO version {version}; '
                               f'expected {self.supported_versions}. Read-only discovery remains available.')
