"""Behavioral regression: scoped types survive; damaged XML and overwrite fail."""
from pathlib import Path
import tempfile,subprocess,sys
from native_xml import load,save,direct,set_text,qname_types

def test():
    with tempfile.TemporaryDirectory(prefix='elhart_skill_test_') as folder:
        root=Path(folder);path=root/'scoped.wcfg'
        xml='<HMIWindowInfo xmlns="http://schemas.flexem.com/fs/model/1" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><Height>600</Height><Constant xmlns:a="http://www.w3.org/2001/XMLSchema" i:type="a:double">99</Constant><Collection xmlns:a="http://schemas.microsoft.com/2003/10/Serialization/Arrays" i:type="a:ArrayOfstring"/></HMIWindowInfo>'
        path.write_text(xml,encoding='utf-8');doc=load(path);original=qname_types(doc)
        set_text(direct(doc.documentElement,'Height'),768);save(path,doc)
        reloaded=load(path);assert qname_types(reloaded)==original
        assert direct(reloaded.documentElement,'Height').firstChild.nodeValue=='768'
        path.write_text(xml.replace('xmlns:a="http://www.w3.org/2001/XMLSchema"','xmlns:a="http://schemas.microsoft.com/2003/10/Serialization/Arrays"'),encoding='utf-8')
        try:load(path)
        except ValueError:pass
        else:raise AssertionError('Corrupt primitive namespace accepted')
        script=Path(__file__).with_name('project_tool.py')
        result=subprocess.run([sys.executable,str(script),'starter','--out',str(root),'--name','Test'],capture_output=True)
        assert result.returncode!=0 and b'already exists' in result.stderr
    print('PASS: scoped QName round-trip, corrupt primitive rejection, no overwrite of existing output.')
if __name__=='__main__':test()
