#!/usr/bin/env python3
"""A11 terseness counter — counts application LoC per the A11 stated method.

Usage: python loc-count.py <app-root> [app-root ...]

Counts non-blank, non-comment lines in hand-written application code (.cs plus served
static assets .html/.css/.js), excluding obj/, bin/, generated files (*.g.cs,
*.Designer.cs, *AssemblyInfo.cs), and JSON config — configuration is counted as keys
instead, and package references are read from .csproj. Runs from a fresh checkout with
no arguments beyond the app roots; the table in LOC-receipt-draft.md must be
reproducible from these commands alone.
"""
import json
import os
import re
import sys

CODE_EXTS = {'.cs', '.html', '.htm', '.css', '.js'}
GENERATED = re.compile(r'(\.g\.cs|\.Designer\.cs|AssemblyInfo\.cs|\.razor\.cs|\.razor)$')
SKIP_DIRS = {'obj', 'bin', '.git', 'node_modules', 'TestResults'}


def count_lines(path):
    n, in_block = 0, False
    try:
        lines = open(path, encoding='utf-8', errors='replace').read().splitlines()
    except OSError:
        return 0
    for raw in lines:
        s = raw.strip()
        if in_block:
            if '*/' in s:
                in_block = False
                s = s.split('*/', 1)[1].strip()
            else:
                continue
        if not s:
            continue
        if s.startswith('/*'):
            if '*/' not in s:
                in_block = True
                continue
            s = s.split('*/', 1)[1].strip()
            if not s:
                continue
        if s.startswith('//'):
            continue
        n += 1
    return n


def count_keys(obj):
    if not isinstance(obj, dict):
        return 0
    return sum(1 + count_keys(v) if isinstance(v, dict) else 1 for v in obj.values())


def scan(root):
    total, files, cfg_keys, pkg_refs = 0, 0, 0, 0
    by_ext = {}
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            p = os.path.join(dirpath, fn)
            ext = os.path.splitext(fn)[1].lower()
            if fn.startswith('appsettings'):
                try:
                    cfg_keys += count_keys(json.load(open(p, encoding='utf-8-sig')))
                except (OSError, ValueError):
                    pass
                continue
            if fn.endswith('.csproj'):
                pkg_refs += open(p, encoding='utf-8', errors='replace').read().count('PackageReference')
                continue
            if ext in CODE_EXTS and not GENERATED.search(fn):
                c = count_lines(p)
                total += c
                files += 1
                by_ext[ext] = by_ext.get(ext, 0) + c
    return total, files, by_ext, cfg_keys, pkg_refs


def main(roots):
    print(f"{'root':60s} {'LoC':>6s} {'files':>6s} {'cfgkeys':>8s} {'pkgrefs':>8s}")
    for root in roots:
        total, files, by_ext, cfg_keys, pkg_refs = scan(root)
        breakdown = ', '.join(f'{k}={v}' for k, v in sorted(by_ext.items(), key=lambda x: -x[1]))
        print(f"{root:60s} {total:>6d} {files:>6d} {cfg_keys:>8d} {pkg_refs:>8d}  [{breakdown}]")


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1:])
