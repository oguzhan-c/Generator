#!/usr/bin/env python3
"""Doğrulama yardımcısı: docker gerektirmeyen açılış testi için üretilen çözümde SQLite'ı ayarlar.

    choose-database.py <çözüm klasörü> sqlite

Paket (Microsoft.EntityFrameworkCore.Sqlite) doğrulama betiğinde dotnet add package ile eklenir. PostgreSQL / SQL Server
için kurulu canfxgen'in "database" komutu kullanılır (Rider: CanFx → Database).
"""
import glob
import os
import re
import sys

root, provider = sys.argv[1], sys.argv[2]
if provider != "sqlite":
    sys.exit(f"yalnızca sqlite (postgres / sqlserver: canfxgen database): {provider}")


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


registration = one("src/*.Persistence/PersistenceServiceRegistration.cs")
development = one("src/*.WebAPI/appsettings.Development.json")

THROW = re.compile(r'[ \t]*// TODO\(canfxgen\): Veritabanı.*?açıklamasına bak\.\"\);\n', re.S)
edit(registration, lambda t: THROW.sub("                options.UseSqlite(tenantConnectionString);\n", t))
edit(development, lambda t: re.sub(r'("[^"]+"): ""', r'\1: "Data Source=verify.db"', t, count=1))
print(f"{root}: {provider}")
