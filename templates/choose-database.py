#!/usr/bin/env python3
"""Doğrulama yardımcısı: üretilen çözümde kullanıcının yapacağı veritabanı seçimini taklit eder.

    choose-database.py <çözüm klasörü> postgres|sqlserver|sqlite

postgres / sqlserver: şablondaki hazır yorum satırları açılır (yorumların gerçekten derlendiği denenir).
sqlite: docker gerektirmeyen açılış testi için SQLite eklenir (şablonda hazır gelmez).
"""
import glob
import os
import re
import sys

root, provider = sys.argv[1], sys.argv[2]


def one(pattern):
    found = glob.glob(os.path.join(root, pattern))
    if len(found) != 1:
        sys.exit(f"{pattern}: {len(found)} dosya bulundu")
    return found[0]


def edit(path, change):
    with open(path, encoding="utf-8") as f:
        text = f.read()
    new = change(text)
    if new == text:
        sys.exit(f"{path}: değişiklik yapılamadı ({provider})")
    with open(path, "w", encoding="utf-8") as f:
        f.write(new)


def uncomment_xml(package):
    # <!-- <PackageVersion Include="X" Version="1" /> ... --> → <PackageVersion Include="X" Version="1" />
    return lambda t: re.sub(r'<!-- (<Package(?:Version|Reference) Include="' + re.escape(package) + r'"[^>]*/>).*?-->', r"\1", t)


packages = {
    "postgres": ["Npgsql.EntityFrameworkCore.PostgreSQL"],
    "sqlserver": ["Microsoft.EntityFrameworkCore.SqlServer", "System.Security.Cryptography.Pkcs"],
}
props = one("Directory.Packages.props")
csproj = one("src/*.Persistence/*.Persistence.csproj")
registration = one("src/*.Persistence/PersistenceServiceRegistration.cs")
development = one("src/*.WebAPI/appsettings.Development.json")

THROW = re.compile(r'[ \t]*// TODO\(canfxgen\): Veritabanı.*?açıklamasına bak\.\"\);\n', re.S)

if provider in packages:
    for package in packages[provider]:
        edit(props, uncomment_xml(package))
        edit(csproj, uncomment_xml(package))
    method = "UseNpgsql" if provider == "postgres" else "UseSqlServer"
    edit(registration, lambda t: THROW.sub("", t.replace("// options." + method + "(", "options." + method + "(")))
    marker = "Host=" if provider == "postgres" else "Server="
    edit(development, lambda t: re.sub(r'\n[ \t]*"[^"]+": ""', "", re.sub(r'// ("[^"]+": "' + marker + ')', r"\1", t), count=1))
    compose = "postgres" if provider == "postgres" else "sqlserver"
    edit(development, lambda t: t.replace('// "Database": { "Compose": "' + compose + '"', '"Database": { "Compose": "' + compose + '"', 1))
elif provider == "sqlite":
    edit(props, lambda t: t.replace('    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design"',
                                    '    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.13" />\n'
                                    '    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design"', 1))
    edit(csproj, lambda t: re.sub(r'([ \t]*)(<!-- <PackageReference Include="Npgsql)',
                                  r'\1<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />\n\1\2', t, count=1))
    edit(registration, lambda t: THROW.sub("                options.UseSqlite(tenantConnectionString);\n", t))
    edit(development, lambda t: re.sub(r'("[^"]+"): ""', r'\1: "Data Source=verify.db"', t, count=1))
else:
    sys.exit(f"bilinmeyen sağlayıcı: {provider}")
print(f"{root}: {provider}")
