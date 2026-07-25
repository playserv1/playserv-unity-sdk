#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
SOURCE="$SCRIPT_DIR/Source/PlayServ.Schema.Tool.csproj"
OUTPUT="$SCRIPT_DIR/runtime"

: "${DOTNET:=dotnet}"

if [ -n "${PLAYSERV_ROSLYN_PATH:-}" ]; then
  "$DOTNET" publish "$SOURCE" \
    --configuration Release \
    --no-self-contained \
    --output "$OUTPUT" \
    -p:PlayServRoslynPath="$PLAYSERV_ROSLYN_PATH"
else
  "$DOTNET" publish "$SOURCE" \
    --configuration Release \
    --no-self-contained \
    --output "$OUTPUT"
fi

echo "PlayServ Schema Tool published to $OUTPUT"
