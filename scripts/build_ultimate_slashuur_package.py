#!/usr/bin/env python3
"""Build and independently verify Ultimate_Slashuur.t2mod without retail redistribution."""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import struct
import zipfile
from pathlib import Path


MAGIC = b"T2DELTA1"
ORIGINAL_EXE_SIZE = 4_796_416
ORIGINAL_EXE_SHA256 = "b827478461e1687f9404679211fc2d2d47bd7222aeb81a8554bd793575a89564"
ORIGINAL_DAT_SIZE = 502_067_200
ORIGINAL_DAT_SHA256 = "cadae3cea3ad60a1f61acf4984f47adb15efccc880fe82c254b332196caa04b2"
ORIGINAL_SLASHUUR_SIZE = 605_632
ORIGINAL_SLASHUUR_SHA256 = "e3f93aa1e63efefb403854a5eea603cc322a9de6b953de89292e804e76777451"
FINAL_LPAC_SIZE = 886_656
FINAL_LPAC_SHA256 = "004b8a19dddffe6aeef991a96ea49c33a8ab97ee21a86f26f7eff6a554bbb7fc"
BLOCK_SIZE = 32
SOURCE_STRIDE = 16
MINIMUM_MATCH = 64
MAXIMUM_DATA_CHUNK = 1024 * 1024
ZIP_TIME = (1980, 1, 1, 0, 0, 0)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def require_file(path: Path, expected_size: int, expected_hash: str) -> bytes:
    data = path.read_bytes()
    if len(data) != expected_size or sha256(data) != expected_hash:
        raise ValueError(f"Fingerprint mismatch: {path}")
    return data


class Brotli:
    def __init__(self) -> None:
        encoder = ctypes.CDLL("libbrotlienc.so.1")
        decoder = ctypes.CDLL("libbrotlidec.so.1")
        encoder.BrotliEncoderMaxCompressedSize.argtypes = [ctypes.c_size_t]
        encoder.BrotliEncoderMaxCompressedSize.restype = ctypes.c_size_t
        encoder.BrotliEncoderCompress.argtypes = [
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_size_t,
            ctypes.c_void_p,
            ctypes.POINTER(ctypes.c_size_t),
            ctypes.c_void_p,
        ]
        encoder.BrotliEncoderCompress.restype = ctypes.c_int
        decoder.BrotliDecoderDecompress.argtypes = [
            ctypes.c_size_t,
            ctypes.c_void_p,
            ctypes.POINTER(ctypes.c_size_t),
            ctypes.c_void_p,
        ]
        decoder.BrotliDecoderDecompress.restype = ctypes.c_int
        self.encoder = encoder
        self.decoder = decoder

    def compress(self, raw: bytes) -> bytes:
        capacity = self.encoder.BrotliEncoderMaxCompressedSize(len(raw))
        output = ctypes.create_string_buffer(capacity)
        output_size = ctypes.c_size_t(capacity)
        input_buffer = ctypes.create_string_buffer(raw)
        result = self.encoder.BrotliEncoderCompress(
            11, 22, 0, len(raw), input_buffer, ctypes.byref(output_size), output
        )
        if result != 1:
            raise RuntimeError("Brotli compression failed")
        return output.raw[: output_size.value]

    def decompress(self, compressed: bytes, expected_size: int) -> bytes:
        output = ctypes.create_string_buffer(expected_size)
        output_size = ctypes.c_size_t(expected_size)
        input_buffer = ctypes.create_string_buffer(compressed)
        result = self.decoder.BrotliDecoderDecompress(
            len(compressed), input_buffer, ctypes.byref(output_size), output
        )
        if result != 1 or output_size.value != expected_size:
            raise RuntimeError("Brotli decompression failed")
        return output.raw[: output_size.value]


def matching_length(source: bytes, target: bytes, source_offset: int, target_offset: int) -> int:
    maximum = min(len(source) - source_offset, len(target) - target_offset)
    length = BLOCK_SIZE
    chunk_size = 64 * 1024
    while length < maximum:
        count = min(chunk_size, maximum - length)
        left = source[source_offset + length : source_offset + length + count]
        right = target[target_offset + length : target_offset + length + count]
        if left == right:
            length += count
            continue
        for index, (source_byte, target_byte) in enumerate(zip(left, right)):
            if source_byte != target_byte:
                return length + index
        raise AssertionError("Mismatch search failed")
    return length


def build_instructions(source: bytes, target: bytes) -> list[tuple[int, int, int | bytes]]:
    source_index: dict[bytes, list[int]] = {}
    for offset in range(0, len(source) - BLOCK_SIZE + 1, SOURCE_STRIDE):
        key = source[offset : offset + BLOCK_SIZE]
        candidates = source_index.setdefault(key, [])
        if len(candidates) < 32:
            candidates.append(offset)

    instructions: list[tuple[int, int, int | bytes]] = []
    pending = bytearray()

    def flush_data() -> None:
        if pending:
            instructions.append((1, 0, bytes(pending)))
            pending.clear()

    target_offset = 0
    while target_offset < len(target):
        best_source = -1
        best_length = 0
        if target_offset + BLOCK_SIZE <= len(target):
            key = target[target_offset : target_offset + BLOCK_SIZE]
            for candidate in source_index.get(key, []):
                length = matching_length(source, target, candidate, target_offset)
                if length > best_length:
                    best_source, best_length = candidate, length

        if best_length >= MINIMUM_MATCH:
            flush_data()
            if (
                instructions
                and instructions[-1][0] == 0
                and instructions[-1][1] + int(instructions[-1][2]) == best_source
            ):
                previous = instructions[-1]
                instructions[-1] = (0, previous[1], int(previous[2]) + best_length)
            else:
                instructions.append((0, best_source, best_length))
            target_offset += best_length
        else:
            pending.append(target[target_offset])
            target_offset += 1
            if len(pending) == MAXIMUM_DATA_CHUNK:
                flush_data()

    flush_data()
    return instructions


def make_delta(source: bytes, target: bytes, brotli: Brotli) -> bytes:
    instructions = build_instructions(source, target)
    output = bytearray()
    output += MAGIC
    output += struct.pack("<Iqq", 1, len(source), len(target))
    output += hashlib.sha256(source).digest()
    output += hashlib.sha256(target).digest()
    output += struct.pack("<i", len(instructions))
    for opcode, source_offset, value in instructions:
        output.append(opcode)
        if opcode == 0:
            output += struct.pack("<qi", source_offset, int(value))
        else:
            raw = bytes(value)
            compressed = brotli.compress(raw)
            output += struct.pack("<ii", len(raw), len(compressed))
            output += compressed
    return bytes(output)


def apply_delta(source: bytes, delta: bytes, brotli: Brotli) -> bytes:
    position = 0

    def read(count: int) -> bytes:
        nonlocal position
        value = delta[position : position + count]
        if len(value) != count:
            raise ValueError("Truncated T2DELTA1")
        position += count
        return value

    if read(8) != MAGIC:
        raise ValueError("Invalid T2DELTA1 magic")
    version, source_size, target_size = struct.unpack("<Iqq", read(20))
    source_hash, target_hash = read(32), read(32)
    instruction_count = struct.unpack("<i", read(4))[0]
    if version != 1 or source_size != len(source) or hashlib.sha256(source).digest() != source_hash:
        raise ValueError("Invalid T2DELTA1 source metadata")
    output = bytearray()
    for _ in range(instruction_count):
        opcode = read(1)[0]
        if opcode == 0:
            source_offset, length = struct.unpack("<qi", read(12))
            if source_offset < 0 or length <= 0 or source_offset + length > len(source):
                raise ValueError("Invalid T2DELTA1 copy")
            output += source[source_offset : source_offset + length]
        elif opcode == 1:
            raw_size, compressed_size = struct.unpack("<ii", read(8))
            output += brotli.decompress(read(compressed_size), raw_size)
        else:
            raise ValueError("Invalid T2DELTA1 opcode")
        if len(output) > target_size:
            raise ValueError("T2DELTA1 output overflow")
    if position != len(delta) or len(output) != target_size or hashlib.sha256(output).digest() != target_hash:
        raise ValueError("T2DELTA1 output validation failed")
    return bytes(output)


def validate_pe32_x86(executable: bytes) -> None:
    if len(executable) < 512 or executable[:2] != b"MZ":
        raise ValueError("Standalone executable lacks an MZ header")
    pe_offset = struct.unpack_from("<I", executable, 0x3C)[0]
    if executable[pe_offset : pe_offset + 4] != b"PE\0\0":
        raise ValueError("Standalone executable lacks a PE signature")
    machine = struct.unpack_from("<H", executable, pe_offset + 4)[0]
    optional_magic = struct.unpack_from("<H", executable, pe_offset + 24)[0]
    if machine != 0x14C or optional_magic != 0x10B:
        raise ValueError("Standalone executable is not PE32 x86")


def validate_lpac(lpac: bytes) -> None:
    if lpac[:4] != b"LPAC" or struct.unpack_from("<I", lpac, 4)[0] != 77:
        raise ValueError("Final LPAC is not the expected 77-chunk archive")
    position = 64
    for chunk in range(77):
        if position + 64 > len(lpac):
            raise ValueError(f"LPAC chunk {chunk} header is truncated")
        size = struct.unpack_from("<I", lpac, position + 4)[0]
        data_offset = (position + 64 + 63) & ~63
        end = data_offset + size
        if end > len(lpac):
            raise ValueError(f"LPAC chunk {chunk} data is truncated")
        position = (end + 63) & ~63
    if position != len(lpac):
        raise ValueError("LPAC has unexpected trailing data")


def zip_write(archive: zipfile.ZipFile, name: str, data: bytes, compression: int) -> None:
    info = zipfile.ZipInfo(name, ZIP_TIME)
    info.create_system = 3
    info.compress_type = compression
    info.external_attr = 0o100644 << 16
    archive.writestr(info, data)


def build_manifest(delta: bytes, target: bytes, lpac: bytes) -> dict[str, object]:
    return {
        "schemaVersion": 1,
        "id": "Ultimate_Slashuur",
        "displayName": "Ultimate Slashuur",
        "version": "1.0.1",
        "description": (
            "Adds playable Slashuur with all five boss moves and Adaptive Teleport v2. "
            "Teleport targets eligible nearby ground enemies within a 25-unit radius, "
            "excludes flying enemies, preserves the original forward fallback, and "
            "revalidates targets before use. Includes the established attack-cancel "
            "rules while preserving hidden/collisionless invulnerability and all "
            "existing visual effects. Does not enable MSAA, Intro Skip, widescreen, "
            "or difficulty changes."
        ),
        "target": {
            "game": "TMNT2_Battle_Nexus_PC",
            "region": "US/NA",
            "architecture": "x86",
            "executableSha256": ORIGINAL_EXE_SHA256,
            "archiveSha256": ORIGINAL_DAT_SHA256,
        },
        "resources": [
            {"id": "file:TMNT2.exe", "mode": "exclusive"},
            {"id": "afs:TMNT.DAT:143", "mode": "exclusive"},
            {"id": "feature:playable-slashuur", "mode": "exclusive"},
            {"id": "exe:function:CSlashuur::OnTeleport", "mode": "exclusive"},
        ],
        "dependencies": [],
        "conflicts": [],
        "operations": [
            {
                "type": "apply-t2delta",
                "target": "TMNT2.exe",
                "payload": "payload/Ultimate_Slashuur.t2delta",
                "payloadSha256": sha256(delta),
                "payloadSize": len(delta),
                "expectedInputSha256": ORIGINAL_EXE_SHA256,
                "expectedInputSize": ORIGINAL_EXE_SIZE,
                "expectedOutputSha256": sha256(target),
                "expectedOutputSize": len(target),
            },
            {
                "type": "replace-afs-entry",
                "target": "TMNT.DAT",
                "payload": "payload/Ultimate_Slashuur.lpac",
                "payloadSha256": sha256(lpac),
                "payloadSize": len(lpac),
                "entryId": 143,
                "logicalPath": "player/slashuur/slashuur.lpac",
                "expectedInputSha256": ORIGINAL_SLASHUUR_SHA256,
                "expectedInputSize": ORIGINAL_SLASHUUR_SIZE,
                "expectedOutputSha256": sha256(lpac),
                "expectedOutputSize": len(lpac),
            },
        ],
    }


def validate_package(path: Path, source: bytes, target: bytes, lpac: bytes, brotli: Brotli) -> None:
    with zipfile.ZipFile(path, "r") as archive:
        expected = {
            "manifest.json",
            "payload/Ultimate_Slashuur.t2delta",
            "payload/Ultimate_Slashuur.lpac",
        }
        if set(archive.namelist()) != expected:
            raise ValueError("Package contains unexpected entries")
        manifest = json.loads(archive.read("manifest.json"))
        delta = archive.read("payload/Ultimate_Slashuur.t2delta")
        packaged_lpac = archive.read("payload/Ultimate_Slashuur.lpac")
    if manifest != build_manifest(delta, target, packaged_lpac):
        raise ValueError("Packaged manifest is inconsistent")
    if packaged_lpac != lpac or apply_delta(source, delta, brotli) != target:
        raise ValueError("Packaged payload round trip failed")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--original-exe", type=Path, required=True)
    parser.add_argument("--standalone-exe", type=Path, required=True)
    parser.add_argument("--slashuur-lpac", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    source = require_file(args.original_exe, ORIGINAL_EXE_SIZE, ORIGINAL_EXE_SHA256)
    target = args.standalone_exe.read_bytes()
    lpac = require_file(args.slashuur_lpac, FINAL_LPAC_SIZE, FINAL_LPAC_SHA256)
    validate_pe32_x86(target)
    validate_lpac(lpac)
    brotli = Brotli()
    delta = make_delta(source, target, brotli)
    if apply_delta(source, delta, brotli) != target:
        raise ValueError("Generated executable delta did not round trip")

    manifest = build_manifest(delta, target, lpac)
    manifest_bytes = (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    if args.output.exists():
        raise FileExistsError(f"Refusing to overwrite: {args.output}")
    with zipfile.ZipFile(args.output, "x") as archive:
        zip_write(archive, "manifest.json", manifest_bytes, zipfile.ZIP_DEFLATED)
        zip_write(archive, "payload/Ultimate_Slashuur.t2delta", delta, zipfile.ZIP_STORED)
        zip_write(archive, "payload/Ultimate_Slashuur.lpac", lpac, zipfile.ZIP_DEFLATED)
    validate_package(args.output, source, target, lpac, brotli)
    print(json.dumps({
        "package": str(args.output),
        "packageSize": args.output.stat().st_size,
        "packageSha256": hashlib.sha256(args.output.read_bytes()).hexdigest(),
        "deltaSize": len(delta),
        "deltaSha256": sha256(delta),
        "standaloneExeSize": len(target),
        "standaloneExeSha256": sha256(target),
        "lpacSize": len(lpac),
        "lpacSha256": sha256(lpac),
    }, indent=2))


if __name__ == "__main__":
    main()
