#!/usr/bin/env python3
"""Deterministically replace only manifest.json in a validated .t2mod package."""

from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from pathlib import Path


ZIP_TIME = (1980, 1, 1, 0, 0, 0)
ALLOWED_ENTRIES = (
    "manifest.json",
    "payload/Ultimate_Slashuur.t2delta",
    "payload/Ultimate_Slashuur.lpac",
)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def zip_write(archive: zipfile.ZipFile, name: str, data: bytes, compression: int) -> None:
    info = zipfile.ZipInfo(name, ZIP_TIME)
    info.create_system = 3
    info.compress_type = compression
    info.external_attr = 0o100644 << 16
    archive.writestr(info, data)


def read_package(path: Path) -> dict[str, bytes]:
    with zipfile.ZipFile(path, "r") as archive:
        names = tuple(entry.filename for entry in archive.infolist())
        if names != ALLOWED_ENTRIES:
            raise ValueError(f"Unexpected package entries: {names}")
        return {name: archive.read(name) for name in names}


def validate_manifest(manifest_bytes: bytes, payloads: dict[str, bytes]) -> dict[str, object]:
    manifest = json.loads(manifest_bytes)
    if manifest.get("id") != "Ultimate_Slashuur":
        raise ValueError("The replacement manifest has the wrong package ID")
    if not str(manifest.get("description", "")).strip():
        raise ValueError("The replacement manifest has no description")

    operations = manifest.get("operations")
    if not isinstance(operations, list):
        raise ValueError("The replacement manifest has no operations")
    for operation in operations:
        payload_name = operation["payload"]
        payload = payloads.get(payload_name)
        if payload is None:
            raise ValueError(f"Manifest references a missing payload: {payload_name}")
        if len(payload) != operation["payloadSize"] or sha256(payload) != operation["payloadSha256"]:
            raise ValueError(f"Payload fingerprint mismatch: {payload_name}")
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    if args.output.exists():
        raise FileExistsError(f"Refusing to overwrite: {args.output}")

    source_entries = read_package(args.source)
    manifest_bytes = args.manifest.read_bytes()
    validate_manifest(manifest_bytes, source_entries)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(args.output, "x") as archive:
        zip_write(archive, "manifest.json", manifest_bytes, zipfile.ZIP_DEFLATED)
        zip_write(
            archive,
            "payload/Ultimate_Slashuur.t2delta",
            source_entries["payload/Ultimate_Slashuur.t2delta"],
            zipfile.ZIP_STORED,
        )
        zip_write(
            archive,
            "payload/Ultimate_Slashuur.lpac",
            source_entries["payload/Ultimate_Slashuur.lpac"],
            zipfile.ZIP_DEFLATED,
        )

    output_entries = read_package(args.output)
    output_manifest = validate_manifest(output_entries["manifest.json"], output_entries)
    for payload_name in ALLOWED_ENTRIES[1:]:
        if output_entries[payload_name] != source_entries[payload_name]:
            raise ValueError(f"Payload bytes changed: {payload_name}")

    print(json.dumps({
        "package": str(args.output),
        "packageSize": args.output.stat().st_size,
        "packageSha256": hashlib.sha256(args.output.read_bytes()).hexdigest(),
        "version": output_manifest["version"],
        "description": output_manifest["description"],
        "payloads": {
            name: {"size": len(output_entries[name]), "sha256": sha256(output_entries[name])}
            for name in ALLOWED_ENTRIES[1:]
        },
    }, indent=2))


if __name__ == "__main__":
    main()
