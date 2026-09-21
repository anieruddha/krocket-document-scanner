import re
import sys
from pathlib import Path


def strip_cs(src, removed):
    out = []
    i = 0
    n = len(src)

    def string_regular(i, out, interpolated):
        out.append(src[i]); i += 1
        while i < n:
            c = src[i]
            if c == '\\':
                out.append(src[i:i+2]); i += 2; continue
            if c == '"':
                out.append(c); return i + 1
            if interpolated and c == '{':
                if src[i:i+2] == '{{':
                    out.append('{{'); i += 2; continue
                out.append(c); i += 1
                i = code(i, out, stop_brace=True)
                continue
            out.append(c); i += 1
        return i

    def string_verbatim(i, out, interpolated):
        out.append(src[i]); i += 1
        while i < n:
            c = src[i]
            if c == '"':
                if src[i:i+2] == '""':
                    out.append('""'); i += 2; continue
                out.append(c); return i + 1
            if interpolated and c == '{':
                if src[i:i+2] == '{{':
                    out.append('{{'); i += 2; continue
                out.append(c); i += 1
                i = code(i, out, stop_brace=True)
                continue
            out.append(c); i += 1
        return i

    def string_raw(i, out, interpolated, dollars):
        m = re.compile(r'"{3,}').match(src, i)
        q = m.group(0)
        out.append(q); i = m.end()
        while i < n:
            if src.startswith(q, i):
                j = i
                while j < n and src[j] == '"':
                    j += 1
                out.append(src[i:j]); return j
            if interpolated and src[i] == '{' and dollars:
                open_run = re.compile(r'\{+').match(src, i).group(0)
                if len(open_run) >= dollars:
                    out.append(src[i]); i += 1
                    i = code(i, out, stop_brace=True)
                    continue
                out.append(open_run); i += len(open_run); continue
            out.append(src[i]); i += 1
        return i

    def code(i, out, stop_brace=False):
        depth = 0
        while i < n:
            c = src[i]
            nxt = src[i+1] if i + 1 < n else ''
            if c == '/' and nxt == '/':
                j = src.find('\n', i)
                if j == -1:
                    j = n
                removed.append(src[i:j])
                out.append('\x00')
                i = j
                continue
            if c == '/' and nxt == '*':
                j = src.find('*/', i + 2)
                j = n if j == -1 else j + 2
                removed.append(src[i:j])
                seg = src[i:j]
                out.append('\x00' + '\n' * seg.count('\n') if '\n' in seg else '\x00')
                i = j
                continue
            if stop_brace:
                if c == '{':
                    depth += 1
                elif c == '}':
                    if depth == 0:
                        out.append(c); return i + 1
                    depth -= 1
            if c == '"':
                if src.startswith('"""', i):
                    i = string_raw(i, out, False, 0); continue
                i = string_regular(i, out, False); continue
            if c in '$@':
                m = re.compile(r'(\$+@?|@\$*)"').match(src, i)
                if m:
                    prefix = m.group(1)
                    out.append(prefix); i += len(prefix)
                    interp = '$' in prefix
                    if src.startswith('"""', i):
                        i = string_raw(i, out, interp, prefix.count('$'))
                    elif '@' in prefix:
                        i = string_verbatim(i, out, interp)
                    else:
                        i = string_regular(i, out, interp)
                    continue
            if c == "'":
                m = re.compile(r"'(\\.[^']*|[^'\\])'").match(src, i)
                if m:
                    out.append(m.group(0)); i = m.end(); continue
            out.append(c); i += 1
        return i

    code(0, out)
    return ''.join(out)


def finish(text, keep_shebang=False):
    lines = text.split('\n')
    res = []
    dropped_since_blank = False
    for ln in lines:
        if '\x00' in ln:
            stripped = ln.replace('\x00', '')
            if stripped.strip() == '':
                dropped_since_blank = True
                continue
            res.append(stripped.rstrip())
            dropped_since_blank = False
            continue
        if ln.strip() == '':
            if res and res[-1].strip() == '' and dropped_since_blank:
                continue
            res.append(ln)
            continue
        res.append(ln)
        dropped_since_blank = False
    return '\n'.join(res)


def strip_xml(src, removed):
    def rep(m):
        removed.append(m.group(0))
        return '\x00'
    return re.sub(r'<!--.*?-->', rep, src, flags=re.S)


def strip_hash(src, removed, keep_first_shebang=True):
    out = []
    for idx, ln in enumerate(src.split('\n')):
        if ln.lstrip().startswith('#') and not (idx == 0 and ln.startswith('#!')):
            removed.append(ln)
            out.append('\x00')
        else:
            out.append(ln)
    return '\n'.join(out)


KINDS = {'.cs': 'cs', '.axaml': 'xml', '.csproj': 'xml', '.sh': 'hash', '.yml': 'hash'}


def kind_of(path):
    p = Path(path)
    if p.name == '.gitignore':
        return 'hash'
    return KINDS.get(p.suffix)


def main():
    changed = []
    for f in sys.argv[1:]:
        kind = kind_of(f)
        if kind is None:
            continue
        p = Path(f)
        src = p.read_text(encoding='utf-8')
        removed = []
        if kind == 'cs':
            t = strip_cs(src, removed)
        elif kind == 'xml':
            t = strip_xml(src, removed)
        else:
            t = strip_hash(src, removed)
        t = finish(t)
        if t != src:
            p.write_text(t, encoding='utf-8', newline='')
            changed.append(f)
            print(f'removed comments: {f}')
    return changed


main()
