#!/usr/bin/env python3
"""Add a released version to the Jellyfin plugin repository manifest.

Jellyfin reads manifest.json to decide which versions of a plugin exist and where to download them,
so every published release has to appear here or servers subscribed to the repository will never see
the update. The release workflow calls this after uploading the archive.

Written in Python rather than as a jq pipeline so that it can be run and tested on a developer machine,
not only on a CI runner.

A release carries one archive per Jellyfin generation, each its own plugin version (1.1.0.11,
1.1.0.12), all attached to the one GitHub release tagged after ReleaseHub's version (v1.1.0). The
workflow therefore calls this once per archive and passes that tag.

Usage:
    update_manifest.py <version> <targetAbi> <checksum> <zipName> [--tag TAG] [--manifest PATH]
"""

from __future__ import annotations

import argparse
import datetime as _dt
import json
import os
import re
import sys

VERSION_PATTERN = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
CHECKSUM_PATTERN = re.compile(r"^[0-9A-F]{32}$")
DEFAULT_REPO = "Warna-Finance-System/Jellyfin-ReleaseHub"


def fail(message: str) -> "None":
    # The ::error:: prefix surfaces the message in the GitHub Actions summary rather than burying it
    # in the step log.
    print(f"::error::{message}", file=sys.stderr)
    raise SystemExit(1)


def version_key(entry: dict) -> tuple[int, ...]:
    # Compared the way Jellyfin compares them, as System.Version, not as text: 1.1.0.12 > 1.1.0.9.
    return tuple(int(part) for part in str(entry.get("version", "0")).split(".") if part.isdigit())


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Add a released version to manifest.json.")
    parser.add_argument("version")
    parser.add_argument("target_abi")
    parser.add_argument("checksum")
    parser.add_argument("zip_name")
    parser.add_argument("--tag", help="Release tag the archive is attached to. Defaults to v<version>.")
    parser.add_argument("--manifest", default="manifest.json")
    args = parser.parse_args(argv[1:])

    version, target_abi, checksum, zip_name = args.version, args.target_abi, args.checksum, args.zip_name
    manifest_path = args.manifest
    tag = args.tag or f"v{version}"

    # Jellyfin compares plugin versions as four-part System.Version values; anything else installs but
    # then sorts unpredictably against other releases.
    if not VERSION_PATTERN.match(version):
        fail(f"version '{version}' must have four numeric parts, for example 1.0.0.1")

    checksum = checksum.upper()
    if not CHECKSUM_PATTERN.match(checksum):
        fail(f"checksum '{checksum}' must be a 32-character uppercase MD5 hex digest")

    if not os.path.isfile(manifest_path):
        fail(f"{manifest_path} not found")

    with open(manifest_path, "r", encoding="utf-8") as handle:
        manifest = json.load(handle)

    if not isinstance(manifest, list) or not manifest:
        fail(f"{manifest_path} must be a non-empty JSON array of plugin entries")

    plugin = manifest[0]
    repo = os.environ.get("GITHUB_REPOSITORY", DEFAULT_REPO)

    entry = {
        "version": version,
        "changelog": f"https://github.com/{repo}/releases/tag/{tag}",
        "targetAbi": target_abi,
        "sourceUrl": f"https://github.com/{repo}/releases/download/{tag}/{zip_name}",
        "checksum": checksum,
        "timestamp": _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }

    # Replacing any existing entry for the same version rather than appending keeps the workflow safe
    # to re-run after a failure.
    existing = [v for v in plugin.get("versions", []) if v.get("version") != version]

    # Newest first: Jellyfin presents the list in order. Sorted rather than prepended, because one
    # release now adds several entries and the order they arrive in must not decide the order shown.
    plugin["versions"] = sorted([entry] + existing, key=version_key, reverse=True)

    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2, ensure_ascii=False)
        handle.write("\n")

    print(f"manifest.json now lists version {version} (checksum {checksum})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
