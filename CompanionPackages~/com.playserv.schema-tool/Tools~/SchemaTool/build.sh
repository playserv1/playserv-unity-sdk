#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
SOURCE="$SCRIPT_DIR/Source/PlayServ.Schema.Tool.csproj"
OUTPUT="$SCRIPT_DIR/runtime"
TEMP_OUTPUT=$(mktemp -d "${TMPDIR:-/tmp}/playserv-schema-tool.XXXXXX")

trap 'rm -rf "$TEMP_OUTPUT"' EXIT HUP INT TERM

: "${DOTNET:=dotnet}"

"$DOTNET" publish "$SOURCE" \
  --configuration Release \
  --no-self-contained \
  --output "$TEMP_OUTPUT"

rm -rf "$OUTPUT"
mkdir -p "$OUTPUT"
find "$TEMP_OUTPUT" -type f -name '*.dll' -exec chmod 0644 {} +
cp -R "$TEMP_OUTPUT/." "$OUTPUT/"

echo "PlayServ Schema Tool published to $OUTPUT"
