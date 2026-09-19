"""Conservative C# lexical scanner for offline checks, not a C# compiler.

Raw, verbatim and interpolated literals remain opaque tokens. The checker
may validate delimiter/layout consistency but cannot resolve types or APIs.
"""
import re
from dataclasses import dataclass

@dataclass
class Token:
    kind: str
    value: str
    start: int
    end: int

def lex(source):
    """Preserve offsets and literal contents; fail on unterminated comments or strings."""
    n = len(source)
    i = 0
    result = []

    def quoted(at):
        """Scan one literal, recursively skipping strings inside interpolation braces."""
        j = at
        while j < n and source[j] in '@$':
            j += 1
        if j >= n or source[j] not in '"\'':
            return None
        prefix = source[at:j]
        quote = source[j]
        if quote == '"':
            m = re.match('"{3,}', source[j:])
            if m:
                fence = m.group()
                end = source.find(fence, j + len(fence))
                if end < 0:
                    raise ValueError('Unterminated raw string')
                return end + len(fence)
        verbatim = '@' in prefix
        interpolated = '$' in prefix
        j += 1
        while j < n:
            if source[j] == quote:
                if verbatim and j + 1 < n and (source[j + 1] == quote):
                    j += 2
                    continue
                return j + 1
            if source[j] == '\\' and (not verbatim):
                j += 2
                continue
            if interpolated and source[j] == '{':
                if j + 1 < n and source[j + 1] == '{':
                    j += 2
                    continue
                depth = 1
                j += 1
                while j < n and depth:
                    if source.startswith('//', j):
                        k = source.find('\n', j)
                        j = n if k < 0 else k
                        continue
                    if source.startswith('/*', j):
                        k = source.find('*/', j + 2)
                        if k < 0:
                            raise ValueError('Unterminated comment')
                        j = k + 2
                        continue
                    end = quoted(j) if source[j] in '@$"\'' else None
                    if end is not None:
                        j = end
                        continue
                    if source[j] == '{':
                        depth += 1
                    elif source[j] == '}':
                        depth -= 1
                    j += 1
                continue
            j += 1
        raise ValueError('Unterminated literal at ' + str(at))
    operators = sorted(('>>>=', '<<=', '>>=', '??=', '>>>', '=>', '==', '!=', '<=', '>=', '&&', '||', '++', '--', '+=', '-=', '*=', '/=', '%=', '&=', '|=', '^=', '<<', '>>', '??', '?.', '?[', '::', '->', '..'), key=len, reverse=True)
    while i < n:
        start = i
        c = source[i]
        if c.isspace():
            while i < n and source[i].isspace():
                i += 1
            result.append(Token('ws', source[start:i], start, i))
            continue
        if source.startswith('//', i):
            j = source.find('\n', i)
            i = n if j < 0 else j
            kind = 'comment'
        elif source.startswith('/*', i):
            j = source.find('*/', i + 2)
            if j < 0:
                raise ValueError('Unterminated comment')
            i = j + 2
            kind = 'comment'
        elif c == '#' and (not source[source.rfind('\n', 0, i) + 1:i].strip()):
            j = source.find('\n', i)
            i = n if j < 0 else j
            kind = 'directive'
        elif c in '@$"\'' and (end := quoted(i)) is not None:
            i = end
            kind = 'literal'
        elif c.isalpha() or c == '_' or c == '@':
            i += 1
            while i < n and (source[i].isalnum() or source[i] == '_'):
                i += 1
            kind = 'word'
        elif c.isdigit() or (c == '.' and i + 1 < n and source[i + 1].isdigit()):
            m = re.match('(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|(?:\\d[\\d_]*(?:\\.(?!\\.)[\\d_]*)?|\\.\\d[\\d_]*)(?:[eE][+-]?[\\d_]+)?)[uUlLfFdDmM]*', source[i:])
            i += len(m.group())
            kind = 'number'
        else:
            op = next((op for op in operators if source.startswith(op, i)), c)
            i += len(op)
            kind = 'punct'
        result.append(Token(kind, source[start:i], start, i))
    return result

def fingerprint(source):
    return [(t.kind, t.value) for t in lex(source) if t.kind != 'ws']

def top_types(source):
    """Find top-level declarations and their bodies without interpreting method contents."""
    tokens = [t for t in lex(source) if t.kind != 'ws']
    depth = 0
    starts = []
    for i, t in enumerate(tokens):
        if depth == 0 and t.value in ('class', 'struct', 'record', 'enum', 'interface'):
            if t.value == 'struct' and i and (tokens[i - 1].value == 'record'):
                continue
            j = i + 1
            if tokens[j].value in ('class', 'struct'):
                j += 1
            name = tokens[j].value
            start = source.rfind('\n', 0, t.start) + 1
            k = i - 1
            while k >= 0 and tokens[k].start >= start:
                k -= 1
            while k >= 0 and tokens[k].kind == 'comment':
                start = source.rfind('\n', 0, tokens[k].start) + 1
                k -= 1
            end = None
            level = 0
            paren = 0
            for z in tokens[j + 1:]:
                if z.value == '(':
                    paren += 1
                elif z.value == ')':
                    paren -= 1
                elif z.value == '{' and paren == 0:
                    level += 1
                elif z.value == '}' and paren == 0:
                    level -= 1
                    if level == 0:
                        end = z.end
                        break
                elif z.value == ';' and paren == 0 and (level == 0):
                    end = z.end
                    break
            if end is None:
                raise ValueError('Missing type body ' + name)
            starts.append((name, start, end))
        if t.value == '{':
            depth += 1
        elif t.value == '}':
            depth -= 1
    return starts
