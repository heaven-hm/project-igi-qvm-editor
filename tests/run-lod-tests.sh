#!/bin/sh
# Host-side regression tests for the lod.qvm reader (issue #7).
# Requires Mono's C# compiler (mcs) and runtime (mono).
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
OUT=${TMPDIR:-/tmp}/qvm-editor-lod-tests.exe

mcs -out:"$OUT" \
    "$ROOT/tests/LodQvmInfoTests.cs" \
    "$ROOT/QVMEditor/Utils/LodQvmInfo.cs"
mono "$OUT"
