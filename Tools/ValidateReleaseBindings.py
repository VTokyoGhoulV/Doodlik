#!/usr/bin/env python3
"""Read-only validation of Doodlik's serialized script bindings in a release.

Usage (Python 3.10+, standard library only):
  python Tools/ValidateReleaseBindings.py <Windows Doodlik_Data directory>
  python Tools/ValidateReleaseBindings.py <Doodlik Android APK>

The input must be inside a directory named Doodlik. Nothing is extracted,
changed, executed, or loaded by Unity. APK split files are joined in memory.
Every level/shared-assets, resources, and global-manager file is checked for
empty MonoBehaviour/ScriptableObject script hashes and MonoScript class/assembly
names. Android's hash-named serialized resource files are included as well.
Referenced MonoScripts in other asset files are checked too. In level0, live
scene components must resolve to Player, Spawner, and CameraFollow with actual
serialized fields, not just base fields.

Exit codes: 0 = PASS, 1 = broken bindings, 2 = unsupported/corrupt input.
This checks scene serialization, not gameplay, rendering, or device behavior.
Supports Unity serialized-file versions 22 and 23 (the project's Unity 6 files).
"""

from __future__ import annotations

import argparse
import re
import struct
import sys
import zipfile
from collections import Counter
from dataclasses import dataclass
from pathlib import Path, PurePosixPath


EMPTY_SCRIPT_HASH = bytes.fromhex("31d6cfe0d16ae931b73c59d7e0c089c0")
ZERO_HASH = bytes(16)
REQUIRED_SCENE_CLASSES = {"Player", "Spawner", "CameraFollow"}
CHECKED_FILE = re.compile(
    r"(?:level\d+|sharedassets\d+\.assets|resources\.assets|"
    r"globalgamemanagers(?:\.assets)?|[0-9a-f]{32})$"
)


class ValidationError(Exception):
    """Malformed or unsupported release input; never silently skip a check."""


class Reader:
    def __init__(self, data: bytes, offset: int = 0, endian: str = "<"):
        self.data, self.offset, self.endian = data, offset, endian

    def take(self, count: int) -> bytes:
        if count < 0 or self.offset + count > len(self.data):
            raise ValidationError(f"read beyond serialized data at {self.offset}")
        value = self.data[self.offset:self.offset + count]
        self.offset += count
        return value

    def value(self, fmt: str):
        values = struct.unpack(self.endian + fmt, self.take(struct.calcsize(self.endian + fmt)))
        return values[0] if len(values) == 1 else values

    def align(self):
        self.take((-self.offset) % 4)

    def count(self, label: str, maximum: int = 2_000_000) -> int:
        count = self.value("i")
        if not 0 <= count <= maximum:
            raise ValidationError(f"invalid {label} count: {count}")
        return count

    def cstring(self) -> str:
        end = self.data.find(b"\0", self.offset)
        if end < 0:
            raise ValidationError("unterminated metadata string")
        value = self.take(end - self.offset).decode("utf-8")
        self.take(1)
        return value

    def string(self) -> str:
        value = self.take(self.count("string bytes")).decode("utf-8")
        self.align()
        return value


@dataclass(frozen=True)
class SerializedType:
    class_id: int
    script_index: int
    script_hash: bytes | None


@dataclass(frozen=True)
class SerializedObject:
    kind: SerializedType
    raw: bytes


class SerializedFile:
    def __init__(self, name: str, data: bytes):
        self.name = name
        if len(data) < 48:
            raise ValidationError(f"{name}: missing serialized-file header")
        version = struct.unpack_from(">I", data, 8)[0]
        if version not in (22, 23):
            raise ValidationError(f"{name}: unsupported serialized-file version {version}")
        metadata_size, file_size, data_offset = struct.unpack_from(">Iqq", data, 20)
        if file_size != len(data) or not 48 <= data_offset <= len(data):
            raise ValidationError(f"{name}: incorrect file length or data offset")
        if metadata_size > len(data):
            raise ValidationError(f"{name}: incorrect metadata length")
        self.endian = ">" if data[16] else "<"
        r = Reader(data, 48, self.endian)
        self.engine = r.cstring()
        self.target = r.value("i")
        has_tree = r.value("?")
        types = []
        for _ in range(r.count("serialized types")):
            class_id = r.value("i")
            r.value("?")  # stripped type
            script_index = r.value("h")
            script_hash = r.take(16) if class_id == 114 else None
            r.take(16)  # old type hash
            if has_tree:
                nodes = r.count("type-tree nodes")
                strings = r.count("type-tree string bytes", len(data))
                r.take(nodes * 32)  # version >= 19: 32-byte blob node
                r.take(strings)
                r.take(r.count("type dependencies") * 4)
            types.append(SerializedType(class_id, script_index, script_hash))

        self.objects = {}
        for _ in range(r.count("serialized objects")):
            r.align()
            path_id, start, size, type_index = r.value("qqIi")
            if path_id in self.objects or not 0 <= type_index < len(types):
                raise ValidationError(f"{name}: duplicate object or invalid type index")
            start += data_offset
            if start < data_offset or start + size > len(data):
                raise ValidationError(f"{name}: object {path_id} outside data")
            self.objects[path_id] = SerializedObject(types[type_index], data[start:start + size])

        for _ in range(r.count("script identifiers")):
            r.value("i")
            r.align()
            r.value("q")
        self.externals = []
        for _ in range(r.count("external references")):
            r.cstring()  # reserved name
            r.take(16)   # GUID
            r.value("i")
            self.externals.append(r.cstring())

    def object_name(self, path_id: int) -> str:
        obj = self.objects.get(path_id)
        if obj is None or obj.kind.class_id != 1:
            return f"GameObject#{path_id}"
        r = Reader(obj.raw, endian=self.endian)
        r.take(r.count("GameObject components") * 12)
        r.value("I")  # layer
        return r.string()


class ReleaseData:
    def __init__(self, path: Path):
        self.path = path.resolve(strict=True)
        if not any(part.lower() == "doodlik" for part in self.path.parts[:-1]):
            raise ValidationError("input must be inside the Doodlik project/release directories")
        self.archive = None
        if self.path.is_file() and self.path.suffix.lower() == ".apk":
            self.archive = zipfile.ZipFile(self.path)
            prefix = "assets/bin/Data/"
            self.files = {name[len(prefix):]: name for name in self.archive.namelist()
                          if name.startswith(prefix) and not name.endswith("/")}
        elif self.path.is_dir():
            self.files = {p.name: p for p in self.path.iterdir() if p.is_file()}
        else:
            raise ValidationError("expected a Windows Data directory or Android APK")
        self.cache = {}

    def close(self):
        if self.archive is not None:
            self.archive.close()

    def raw(self, name: str) -> bytes:
        def read(key):
            value = self.files[key]
            return self.archive.read(value) if self.archive is not None else value.read_bytes()
        if name in self.files:
            return read(name)
        split = []
        for key in self.files:
            match = re.fullmatch(re.escape(name) + r"\.split(\d+)", key)
            if match:
                split.append((int(match.group(1)), key))
        split.sort()
        if not split or [n for n, _ in split] != list(range(len(split))):
            raise ValidationError(f"{name}: missing file or discontinuous split parts")
        return b"".join(read(key) for _, key in split)

    def serialized(self, name: str) -> SerializedFile:
        if name not in self.cache:
            self.cache[name] = SerializedFile(name, self.raw(name))
        return self.cache[name]

    def resolve(self, source: SerializedFile, file_id: int, path_id: int):
        if file_id < 0 or file_id > len(source.externals):
            raise ValidationError(f"{source.name}: script has invalid external file ID {file_id}")
        name = source.name if file_id == 0 else PurePosixPath(
            source.externals[file_id - 1].replace("\\", "/")).name
        asset = self.serialized(name)
        obj = asset.objects.get(path_id)
        if obj is None:
            raise ValidationError(f"{name}: referenced script object {path_id} missing")
        return asset, obj


def script_identity(asset: SerializedFile, obj: SerializedObject):
    if obj.kind.class_id != 115:
        raise ValidationError(f"{asset.name}: script reference does not target a MonoScript")
    r = Reader(obj.raw, endian=asset.endian)
    name = r.string()
    r.value("i")  # execution order
    r.take(16)    # properties hash
    class_name, namespace, assembly = r.string(), r.string(), r.string()
    return name, class_name, namespace, assembly


def validate(data: ReleaseData) -> bool:
    names = sorted({re.sub(r"\.split\d+$", "", name) for name in data.files
                    if CHECKED_FILE.fullmatch(re.sub(r"\.split\d+$", "", name))})
    if "level0" not in names:
        raise ValidationError("release has no level0 scene")
    failures = []
    seen_scripts = set()
    components = Counter()
    mono_count = 0

    def check_script(asset, path_id, obj):
        key = (asset.name, path_id)
        identity = script_identity(asset, obj)
        if key not in seen_scripts:
            seen_scripts.add(key)
            name, class_name, _, assembly = identity
            if not class_name or not assembly:
                failures.append(f"{asset.name}#{path_id} MonoScript '{name}' has empty class/assembly")
        return identity

    for name in names:
        asset = data.serialized(name)
        for path_id, obj in asset.objects.items():
            if obj.kind.class_id == 115:
                check_script(asset, path_id, obj)
            if obj.kind.class_id != 114:
                continue
            mono_count += 1
            if obj.kind.script_hash in (EMPTY_SCRIPT_HASH, ZERO_HASH):
                failures.append(f"{name}#{path_id} MonoBehaviour has empty script hash")
            if len(obj.raw) < 32:
                failures.append(f"{name}#{path_id} MonoBehaviour has incomplete base fields")
                continue
            go_path = struct.unpack_from(asset.endian + "q", obj.raw, 4)[0]
            file_id, script_path = struct.unpack_from(asset.endian + "iq", obj.raw, 16)
            if script_path == 0:
                failures.append(f"{name}#{path_id} MonoBehaviour has null script reference")
                continue
            script_asset, script = data.resolve(asset, file_id, script_path)
            _, class_name, _, assembly = check_script(script_asset, script_path, script)
            if name == "level0" and class_name in REQUIRED_SCENE_CLASSES:
                components[class_name] += 1
                if assembly.removesuffix(".dll") != "Assembly-CSharp":
                    failures.append(f"level0 '{class_name}' has unexpected assembly '{assembly}'")
                name_length = struct.unpack_from(asset.endian + "i", obj.raw, 28)[0]
                if name_length < 0 or 32 + name_length > len(obj.raw):
                    raise ValidationError(f"{name}#{path_id}: invalid MonoBehaviour name")
                base_size = 32 + ((name_length + 3) & ~3)
                if len(obj.raw) <= base_size:
                    failures.append(f"level0 '{class_name}' has no serialized game fields")
                print(f"  SCENE {asset.object_name(go_path)} -> {class_name} ({len(obj.raw)} bytes)")

    for class_name in sorted(REQUIRED_SCENE_CLASSES - components.keys()):
        failures.append(f"level0 is missing a bound {class_name} component")
    for failure in failures:
        print("  ERROR " + failure)
    print(f"{'PASS' if not failures else 'FAIL'} {data.path.name}: "
          f"{len(names)} serialized files, {mono_count} MonoBehaviour/ScriptableObject assets, "
          f"{len(seen_scripts)} MonoScripts, {len(failures)} errors")
    return not failures


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("release", type=Path)
    args = parser.parse_args()
    data = None
    try:
        data = ReleaseData(args.release)
        return 0 if validate(data) else 1
    except (ValidationError, OSError, UnicodeError, ValueError, struct.error, zipfile.BadZipFile) as error:
        print(f"ERROR {error}", file=sys.stderr)
        return 2
    finally:
        if data is not None:
            data.close()


if __name__ == "__main__":
    sys.exit(main())
