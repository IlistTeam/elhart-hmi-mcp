"""OEM catalog and version gates must not inherit upstream certificates."""
from pathlib import Path
import tempfile
import unittest
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from runtime_profile import RuntimeProfile


class RuntimeProfileTests(unittest.TestCase):
    def test_oem_allowlist_alias_resolution_and_overlay(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            catalog = root/'Device/HMI'
            (catalog/'Custom').mkdir(parents=True)
            (catalog/'Custom/119.xml').write_text('''<HmiOemConfig>
                <NameMappings><Entry><Key>2050</Key><Value>ECP10</Value></Entry></NameMappings>
                <WhiteIds><int>2050</int></WhiteIds></HmiOemConfig>''', encoding='utf8')
            (catalog/'HMI_index.CFG').write_text('''<Catalog>
                <HMIType ID="2050" Name="F010" DisplaySizeX="1024" DisplaySizeY="768" Runtime="a8" DefaultColor="16"/>
                <HMIType ID="9999" Name="F999" DisplaySizeX="1" DisplaySizeY="1"/>
                </Catalog>''', encoding='utf8')
            overlay = root/'Template/HMI/Custom/119/2050.ptpl'
            overlay.parent.mkdir(parents=True)
            overlay.touch()
            profile = RuntimeProfile.discover(root/'Bin', 'elhart')
            models = profile.model_catalog()
            self.assertEqual(set(models), {'F010'})
            self.assertEqual(models['F010']['display_name'], 'ECP10')
            self.assertEqual((models['F010']['width'], models['F010']['height']), (1024, 768))
            self.assertEqual(models['F010']['oem_overlay'], str(overlay))
            self.assertEqual(profile.templates, root/'Template/HMI/zh')
            self.assertEqual(profile.resolve_model('ECP10'), 'F010')
            with self.assertRaisesRegex(ValueError, 'missing or ambiguous'):
                profile.resolve_model('F999')
            self.assertEqual(profile.describe('3.0.15569.0')['verification'], 'installed_metadata_only')

    def test_wrong_installation_and_unknown_version_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaisesRegex(ValueError, 'OEM catalog'):
                RuntimeProfile.discover(Path(temporary)/'Bin', 'elhart')
            profile = RuntimeProfile('elhart', Path(temporary)/'Bin')
            profile.require_supported('3.0.15569.0')
            with self.assertRaisesRegex(RuntimeError, 'Unsupported'):
                profile.require_supported('3.0.15685.0')
            self.assertFalse(profile.describe('3.0.15685.0')['version_supported'])


if __name__ == '__main__':
    unittest.main()
