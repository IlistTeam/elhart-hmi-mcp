"""Edit native XML while retaining scoped QName prefixes in xsi:type."""
from pathlib import Path
from xml.dom import minidom, Node
import os,tempfile
NS='http://schemas.flexem.com/fs/model/1'
XSI='http://www.w3.org/2001/XMLSchema-instance'
XSD='http://www.w3.org/2001/XMLSchema'
PRIMITIVES=set('string boolean decimal float double duration dateTime time date hexBinary base64Binary anyURI QName integer int long short byte unsignedLong unsignedInt unsignedShort unsignedByte nonNegativeInteger'.split())

def children(node,name):
    return [n for n in node.childNodes if n.nodeType==Node.ELEMENT_NODE and n.namespaceURI==NS and n.localName==name]

def direct(node,name):
    found=children(node,name)
    if len(found)!=1:raise ValueError(f'Expected one direct {name}, found {len(found)}')
    return found[0]

def set_text(node,value):
    if any(n.nodeType==Node.ELEMENT_NODE for n in node.childNodes):raise ValueError('Refusing to replace a structured DTO with text')
    while node.firstChild:node.removeChild(node.firstChild)
    node.appendChild(node.ownerDocument.createTextNode(str(value)))

def namespace_at(node,prefix):
    key='xmlns:'+prefix if prefix else 'xmlns'
    current=node
    while current and current.nodeType==Node.ELEMENT_NODE:
        if current.hasAttribute(key):return current.getAttribute(key)
        current=current.parentNode
    if prefix=='xml':return 'http://www.w3.org/XML/1998/namespace'
    raise ValueError(f'Undeclared QName prefix {prefix!r}')

def qname_types(doc):
    result=[]
    for node in doc.getElementsByTagName('*'):
        if not node.hasAttributeNS(XSI,'type'):continue
        value=node.getAttributeNS(XSI,'type');parts=value.split(':')
        if len(parts)>2 or any(not x for x in parts):raise ValueError(f'Invalid QName {value!r}')
        prefix,local=parts if len(parts)==2 else ('',parts[0])
        uri=namespace_at(node,prefix)
        if local in PRIMITIVES and uri!=XSD:raise ValueError(f'{value} resolves to {uri}, expected XMLSchema primitive')
        result.append((uri,local))
    return result

def load(path):
    doc=minidom.parse(str(path));qname_types(doc);return doc

def save(path,doc):
    """Atomic replacement; caller chooses destination and backup policy."""
    path=Path(path);expected=qname_types(doc);data=doc.toxml(encoding='utf-8')
    roundtrip=minidom.parseString(data)
    if qname_types(roundtrip)!=expected:raise ValueError('QName types changed during serialization')
    handle,tmp=tempfile.mkstemp(prefix=path.name+'.',suffix='.tmp',dir=path.parent)
    try:
        with os.fdopen(handle,'wb') as stream:stream.write(data)
        os.replace(tmp,path)
    finally:
        if os.path.exists(tmp):os.unlink(tmp)
