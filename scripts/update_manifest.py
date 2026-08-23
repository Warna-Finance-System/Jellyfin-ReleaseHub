#!/usr/bin/env python3
"""Add a released version to the Jellyfin plugin repository manifest.

Jellyfin reads manifest.json to decide which versions of a plugin exist and where to download them,
so every published release has to appear here or servers subscribed to the repository will never see
the update. The release workflow calls this after uploading the archive.

Written in Python rather than as a jq pipeline so that it can be run and tested on a developer machine,
not only on a CI runner.

Usage:
    update_manifest.py <version> <targetAbi> <checksum> <zipName> [manifestPath]
"""

from __future__ import annotations

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


def main(argv: list[str]) -> int:
    if len(argv) < 5:
        fail("usage: update_manifest.py <version> <targetAbi> <checksum> <zipName> [manifestPath]")

    version, target_abi, checksum, zip_name = argv[1:5]
    manifest_path = argv[5] if len(argv) > 5 else "manifest.json"

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
        "changelog": f"https://github.com/{repo}/releases/tag/v{version}",
        "targetAbi": target_abi,
        "sourceUrl": f"https://github.com/{repo}/releases/download/v{version}/{zip_name}",
        "checksum": checksum,
        "timestamp": _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }

    # Newest first: Jellyfin presents the list in order. Replacing any existing entry for the same
    # version rather than appending keeps the workflow safe to re-run after a failure.
    existing = [v for v in plugin.get("versions", []) if v.get("version") != version]
    plugin["versions"] = [entry] + existing

    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2, ensure_ascii=False)
        handle.write("\n")

    print(f"manifest.json now lists version {version} (checksum {checksum})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
