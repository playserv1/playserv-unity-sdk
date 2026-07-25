#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
SOURCE="$SCRIPT_DIR/Source/PlayServ.Schema.Tool.csproj"
OUTPUT="$SCRIPT_DIR/runtime"

: "${DOTNET:=dotnet}"

"$DOTNET" publish "$SOURCE" \
  --configuration Release \
  --no-self-contained \
  --output "$OUTPUT"

echo "PlayServ Schema Tool published to $OUTPUT"
