#!/usr/bin/env python3
"""List the builds a ReleaseHub release is made of, one per Jellyfin generation.

build.yaml declares ReleaseHub's version once and a `targets` list, one entry per Jellyfin generation
the release supports. Each target is published as its own plugin version: ReleaseHub's version with
the generation appended as a fourth part (1.1.0 -> 1.1.0.11 for Jellyfin 10.11, 1.1.0.12 for
Jellyfin 12), because Jellyfin installs the highest version whose targetAbi the server satisfies.

Prints one tab-separated line per target, lowest generation first:

    generation  framework  targetAbi  pluginVersion  jellyfinLabel

Written as a script rather than inline in the workflow so that CI can validate build.yaml on every
push, and so that it can be run on a developer machine.

Usage:
    build_targets.py [version] [buildYamlPath]

`version` defaults to the one build.yaml declares; the release workflow passes it explicitly when a
tag or a manual dispatch names the version to publish.
"""

from __future__ import annotations

import re
import sys

RELEASE_VERSION = re.compile(r"^\d+\.\d+\.\d+$")
TARGET_ABI = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
GENERATION = re.compile(r"^\d+$")
FRAMEWORK = re.compile(r"^net\d+\.\d+$")
TOP_LEVEL = re.compile(r'^([A-Za-z]\w*)\s*:\s*"?([^"#]*?)"?\s*(?:#.*)?$')
ITEM_START = re.compile(r'^\s+-\s+(\w+)\s*:\s*"?([^"#]*?)"?\s*(?:#.*)?$')
ITEM_FIELD = re.compile(r'^\s+(\w+)\s*:\s*"?([^"#]*?)"?\s*(?:#.*)?$')


def fail(message: str) -> "None":
    # The ::error:: prefix surfaces the message in the GitHub Actions summary rather than burying it
    # in the step log.
    print(f"::error::{message}", file=sys.stderr)
    raise SystemExit(1)


def read_build_yaml(path: str) -> tuple[str, list[dict[str, str]]]:
    """Reads the version and the targets list.

    build.yaml is a flat file plus one list of flat mappings, so a line reader covers it and keeps
    the release free of a YAML dependency that a runner image may or may not carry.
    """
    version = ""
    targets: list[dict[str, str]] = []
    in_targets = False

    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            line = line.rstrip("\n")
            if not line.strip() or line.lstrip().startswith("#"):
                continue

            if not line[0].isspace():
                in_targets = line.startswith("targets:")
                match = TOP_LEVEL.match(line)
                if match and match.group(1) == "version":
                    version = match.group(2).strip()
                continue

            if not in_targets:
                continue

            match = ITEM_START.match(line)
            if match:
                targets.append({match.group(1): match.group(2).strip()})
                continue

            match = ITEM_FIELD.match(line)
            if match and targets:
                targets[-1][match.group(1)] = match.group(2).strip()

    return version, targets


def jellyfin_label(target_abi: str) -> str:
    """The server versions a targetAbi covers, as release notes should name them."""
    major, minor = target_abi.split(".")[:2]
    # Jellyfin numbered its releases 10.x until 10.11, then moved to 12, 12.1, ...
    return f"{major}.{minor}.x" if major == "10" else f"{major}.x"


def main(argv: list[str]) -> int:
    path = argv[2] if len(argv) > 2 else "build.yaml"
    declared, targets = read_build_yaml(path)
    version = argv[1] if len(argv) > 1 and argv[1] else declared

    if not RELEASE_VERSION.match(version or ""):
        fail(f"version '{version}' must have three numeric parts, for example 1.1.0; "
             "the fourth is the Jellyfin generation of each build")

    if not targets:
        fail(f"{path} declares no targets")

    seen: set[str] = set()
    rows = []

    for target in targets:
        generation = target.get("generation", "")
        framework = target.get("framework", "")
        target_abi = target.get("targetAbi", "")

        if not GENERATION.match(generation):
            fail(f"target generation '{generation}' must be a number, for example 12")
        if generation in seen:
            fail(f"generation {generation} is declared twice")
        if not FRAMEWORK.match(framework):
            fail(f"target {generation}: framework '{framework}' must look like net10.0")
        if not TARGET_ABI.match(target_abi):
            fail(f"target {generation}: targetAbi '{target_abi}' must have four numeric parts")

        seen.add(generation)
        rows.append((int(generation), framework, target_abi))

    # Lowest first, so that a caller prepending manifest entries in this order ends with the newest
    # server generation on top.
    for generation, framework, target_abi in sorted(rows):
        plugin_version = f"{version}.{generation}"
        print("\t".join((str(generation), framework, target_abi, plugin_version, jellyfin_label(target_abi))))

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
