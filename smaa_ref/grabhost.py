import urllib.request, re

for name in ('SMAA.cpp', 'SMAA.h'):
    url = f'https://raw.githubusercontent.com/iryoku/smaa/master/Demo/DX10/Code/{name}'
    src = urllib.request.urlopen(url).read().decode('utf-8', 'replace')
    with open(name.replace('.h', '.h'), 'w', encoding='utf-8') as f:
        f.write(src)
    print(f'=== {name} ({len(src)} bytes) ===')
    for i, line in enumerate(src.splitlines(), 1):
        if re.search(r'DXGI_FORMAT|AreaTex|SearchTex|SetResource|Sampler|CreateTexture|Texture2D\(', line):
            print(f'{i}: {line.rstrip()}')
