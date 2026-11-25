#!/bin/sh
# Runs the AppData bootstrap tests on any host with Mono (no WinForms required).
set -eu
ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT INT TERM

mcs -out:"$OUT/AppDataBootstrapTests.exe" \
    "$ROOT/QVMEditor/AppDataBootstrap.cs" \
    "$ROOT/tests/AppDataBootstrapTests.cs"
mono "$OUT/AppDataBootstrapTests.exe"
