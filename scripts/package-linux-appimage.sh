#!/usr/bin/env bash

set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
appimagetool_url="https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage"
appimagetool_sha256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"
runtime_url="https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64"
runtime_sha256="2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d"

usage() {
    echo "Usage: $0 --publish-dir <directory> --output <path>" >&2
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

[[ -n "$publish_directory" && -n "$output_path" ]] || usage
[[ -d "$publish_directory" ]] || {
    echo "Publish directory does not exist: $publish_directory" >&2
    exit 1
}
[[ -f "$publish_directory/HappyPhoton" ]] || {
    echo "HappyPhoton is absent from publish directory: $publish_directory" >&2
    exit 1
}

temporary_directory="$(mktemp -d "${TMPDIR:-/tmp}/happy-photon-appimage.XXXXXX")"
app_dir="$temporary_directory/HappyPhoton.AppDir"
appimagetool_path="$temporary_directory/appimagetool-x86_64.AppImage"
runtime_path="$temporary_directory/runtime-x86_64"

cleanup() {
    rm -rf "$temporary_directory"
}
trap cleanup EXIT

verify_sha256() {
    local path="$1"
    local expected="$2"
    printf '%s  %s\n' "$expected" "$path" | sha256sum --check --status
}

mkdir -p "$app_dir/usr/bin" "$(dirname "$output_path")"
cp -a "$publish_directory/." "$app_dir/usr/bin/"
cp "$project_root/packaging/linux/happy-photon.desktop" "$app_dir/"
cp "$project_root/Assets/happy-photon-icon.png" "$app_dir/happy-photon.png"
cp "$project_root/Assets/happy-photon-icon.png" "$app_dir/.DirIcon"
cp "$project_root/packaging/linux/AppRun" "$app_dir/"
chmod +x "$app_dir/AppRun" "$app_dir/usr/bin/HappyPhoton"

curl --fail --location --retry 3 --output "$appimagetool_path" "$appimagetool_url"
curl --fail --location --retry 3 --output "$runtime_path" "$runtime_url"
verify_sha256 "$appimagetool_path" "$appimagetool_sha256"
verify_sha256 "$runtime_path" "$runtime_sha256"
chmod +x "$appimagetool_path"

ARCH=x86_64 "$appimagetool_path" --appimage-extract-and-run \
    --no-appstream \
    --runtime-file "$runtime_path" \
    "$app_dir" \
    "$output_path"

echo "$output_path"
