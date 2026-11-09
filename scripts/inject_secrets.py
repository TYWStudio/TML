# -*- coding: utf-8 -*-
"""把发版用的地址与密钥临时注入 ModSecret.vb —— 只在这一台机器/这一次构建里生效。

仓库里的 ModSecret.vb 恒为空值，公开可见；真实值来自：
    1) 环境变量（GitHub Actions 的 Secrets、本机临时 export）
    2) _deploy/ 下的同名 txt（本机发版用，已在 .gitignore 里排除）
两个都没有 → 保持空值，产物就是纯离线的开源版。

用法：
    python scripts/inject_secrets.py apply    # 注入（会打印掩码后的值）
    python scripts/inject_secrets.py restore  # 还原成空值
    python scripts/inject_secrets.py status   # 只看当前状态，不改文件

也可以被 build.py 直接 import：inject_secrets.apply() / .restore()
"""

import io
import os
import re
import sys

# Windows 控制台/管道默认是 cp1252，直接 print 中文会 UnicodeEncodeError（CI 上就这么炸过）
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SECRET_VB = os.path.join(ROOT, 'TYW Launcher', 'Modules', 'ModSecret.vb')
# 本机发版用的明文文件放这里：_deploy/ 整个目录已在 .gitignore 里排除
SECRETS_DIR = os.path.join(ROOT, '_deploy')

# 常量名, 环境变量, 本地文件名, 是否要求以 / 结尾
PLACEHOLDERS = [
    ('BuiltInServerAddress', 'TYW_SERVER_URL', 'server_url.txt', True),
    ('BuiltInOAuthClientId', 'PCL_MS_CLIENT_ID', 'client_id.txt', False),
    ('BuiltInCurseForgeKey', 'PCL_CURSEFORGE_API_KEY', 'curseforge_key.txt', False),
    ('ClsBaseUrl', 'TYW_CLS_URL', 'cls_url.txt', False),
]

_BACKUP = None  # 原始文件内容（带 BOM 的原始 bytes 解码后），restore 用


def _read():
    return io.open(SECRET_VB, 'rb').read().decode('utf-8-sig')


def _write(text):
    io.open(SECRET_VB, 'wb').write(text.encode('utf-8-sig'))


def _local_value(filename):
    path = os.path.join(SECRETS_DIR, filename)
    if not os.path.exists(path):
        return ''
    try:
        return io.open(path, encoding='utf-8-sig').read().strip()
    except Exception:
        return ''


def resolve(const_name, env_name, filename, want_slash):
    """取一个值的来源：环境变量 > _deploy 下的 txt > 空。"""
    value = ''
    source = ''
    if os.environ.get(env_name, '').strip():
        value = os.environ[env_name].strip()
        source = 'env:' + env_name
    else:
        value = _local_value(filename)
        if value:
            source = '_deploy/' + filename
    if value and want_slash and not value.endswith('/'):
        value += '/'
    return value, source


def mask(value):
    """日志里只显示掩码，避免 CI 输出把密钥原样打出来。"""
    if not value:
        return '（空）'
    if len(value) <= 8:
        return value[:2] + '***'
    return value[:8] + '***(' + str(len(value)) + ')'


def _vb_escape(value):
    return value.replace('"', '""')


def apply(quiet=False):
    """按 PLACEHOLDERS 逐个注入，返回 {常量名: 是否已注入}。"""
    global _BACKUP
    _BACKUP = _read()
    text = _BACKUP
    result = {}
    for const_name, env_name, filename, want_slash in PLACEHOLDERS:
        value, source = resolve(const_name, env_name, filename, want_slash)
        if not value:
            result[const_name] = False
            if not quiet:
                print('  %-22s 空（离线/开源默认值）' % const_name, flush=True)
            continue
        pattern = r'(Public Const ' + const_name + r' As String = )"[^"]*"'
        if re.search(pattern, text) is None:
            result[const_name] = False
            print('  %-22s 注入失败：ModSecret.vb 里找不到该常量' % const_name, flush=True)
            continue
        new_text = re.sub(pattern, lambda m: m.group(1) + '"' + _vb_escape(value) + '"', text, count=1)
        if new_text == text:
            # 本机工作副本的源码本来就写着真实值（local_secrets.py apply），无需改动
            result[const_name] = True
            if not quiet:
                print('  %-22s 已就位（源码里本来就是该值）' % const_name, flush=True)
            continue
        text = new_text
        result[const_name] = True
        if not quiet:
            print('  %-22s %s  ← %s' % (const_name, mask(value), source), flush=True)
    if text != _BACKUP:
        _write(text)
    return result


def restore():
    """还原成注入前的样子。

    同一次进程里（build.py 的用法）直接用内存里的备份还原；
    跨进程（先 `apply` 再 `restore`，比如手动排查）时无法知道原值，
    按仓库约定一律清成空串——仓库里的四个常量本来就恒为空。
    """
    global _BACKUP
    if _BACKUP is not None:
        _write(_BACKUP)
        _BACKUP = None
        return True
    text = _read()
    new_text = text
    for const_name, _, _, _ in PLACEHOLDERS:
        pattern = r'(Public Const ' + const_name + r' As String = )"[^"]*"'
        new_text = re.sub(pattern, lambda m: m.group(1) + '""', new_text, count=1)
    if new_text == text:
        return False
    _write(new_text)
    return True


def current():
    """读出文件里当前的值（不含 BOM），用于 status。"""
    text = _read()
    values = {}
    for const_name, _, _, _ in PLACEHOLDERS:
        m = re.search(r'Public Const ' + const_name + r' As String = "([^"]*)"', text)
        values[const_name] = m.group(1) if m else None
    return values


def main(argv):
    action = (argv[1] if len(argv) > 1 else 'apply').lower()
    if action == 'apply':
        print('注入构建期地址与密钥（源码里的空值只在构建期间被替换）：', flush=True)
        apply()
        return 0
    if action == 'restore':
        print('已还原 ModSecret.vb' if restore() else '没有需要还原的改动', flush=True)
        return 0
    if action == 'status':
        for key, value in current().items():
            print('%-22s %s' % (key, mask(value or '')), flush=True)
        return 0
    print(__doc__, flush=True)
    return 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
