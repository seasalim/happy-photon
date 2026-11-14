#!/usr/bin/env bash

# Writes the byte-reproducible Linux release archive (.tar.xz) from a linux-x64
# publish directory. Entry order, timestamps and ownership are fixed, and xz runs
# single-threaded because its multi-threaded output depends on the thread count.

set -euo pipefail

usage() {
    echo "Usage: $0 --publish-dir <directory> --output <path.tar.xz>" >&2
    exit 2
}

publish_directory=""
output_path=""
while (( $# > 0 )); do
    case "$1" in
        --publish-dir)
            (( $# >= 2 )) || usage
            publish_directory="$2"
            shift 2
            ;;
        --output)
            (( $# >= 2 )) || usage
            output_path="$2"
            shift 2
            ;;
        *)
            usage
            ;;
    esac
done

[[ -n "$publish_directory" && "$output_path" == *.tar.xz ]] || usage
[[ -f "$publish_directory/HappyPhoton" ]] || {
    echo "Publish directory has no HappyPhoton executable: $publish_directory" >&2
    exit 1
}

chmod +x "$publish_directory/HappyPhoton"
epoch="$(git log -1 --format=%ct)"
tar --sort=name \
    --mtime="@$epoch" \
    --owner=0 \
    --group=0 \
    --numeric-owner \
    -C "$publish_directory" \
    -cf - . |
    xz -9 -T1 > "$output_path"
