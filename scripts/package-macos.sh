#!/usr/bin/env bash

set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
machine_architecture="${1:-$(uname -m)}"

case "$machine_architecture" in
    arm64)
        runtime_identifier="osx-arm64"
        ;;
    *)
        echo "Happy Photon supports Apple Silicon macOS only; got: $machine_architecture" >&2
        exit 1
        ;;
esac

if ! command -v "$dotnet_command" >/dev/null 2>&1; then
    echo "The .NET 10 SDK was not found. Set DOTNET_COMMAND or install dotnet." >&2
    exit 1
fi

app_version="$("$dotnet_command" msbuild "$project_root/HappyPhoton.csproj" \
    -nologo -getProperty:Version)"
app_version="${HAPPY_PHOTON_VERSION:-$app_version}"
signing_identity="${APPLE_SIGNING_IDENTITY:--}"

output_root="$project_root/artifacts/$runtime_identifier"
publish_directory="$output_root/publish"
app_bundle="$output_root/Happy Photon.app"
contents_directory="$app_bundle/Contents"
temporary_directory="$(mktemp -d "${TMPDIR:-/tmp}/happy-photon-package.XXXXXX")"
iconset_directory="$temporary_directory/HappyPhoton.iconset"
mkdir -p "$iconset_directory"

cleanup() {
    rm -rf "$temporary_directory"
}
trap cleanup EXIT

rm -rf "$publish_directory" "$app_bundle"
mkdir -p "$publish_directory" "$contents_directory/MacOS" "$contents_directory/Resources"

publish_arguments=(
    publish
    "$project_root/HappyPhoton.csproj"
    -p:PublishProfile="$runtime_identifier"
    -p:Version="$app_version"
    --output
    "$publish_directory"
)
if [[ "${HAPPY_PHOTON_NO_RESTORE:-0}" == "1" ]]; then
    publish_arguments+=(--no-restore)
fi
if [[ "${HAPPY_PHOTON_CI_BUILD:-0}" == "1" ]]; then
    publish_arguments+=(-p:ContinuousIntegrationBuild=true)
fi
"$dotnet_command" "${publish_arguments[@]}"

if [[ -n "${HAPPY_PHOTON_DEPENDENCY_MANIFEST:-}" ]]; then
    cp "$HAPPY_PHOTON_DEPENDENCY_MANIFEST" \
        "$publish_directory/DEPENDENCIES.json"
fi

for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$project_root/Assets/happy-photon-icon.png" \
        --out "$iconset_directory/icon_${size}x${size}.png" >/dev/null
    retina_size=$((size * 2))
    sips -z "$retina_size" "$retina_size" "$project_root/Assets/happy-photon-icon.png" \
        --out "$iconset_directory/icon_${size}x${size}@2x.png" >/dev/null
done

iconutil --convert icns "$iconset_directory" \
    --output "$contents_directory/Resources/HappyPhoton.icns"

sign_target() {
    local target="$1"
    if [[ "$signing_identity" == "-" ]]; then
        codesign --force --sign - "$target"
    else
        codesign --force --options runtime --timestamp \
            --sign "$signing_identity" "$target"
    fi
}

while IFS= read -r -d '' binary; do
    if file -b "$binary" | grep -q 'Mach-O'; then
        sign_target "$binary"
    fi
done < <(find "$publish_directory" -type f -print0)

cp -a "$publish_directory/." "$contents_directory/MacOS/"
cp "$project_root/Platforms/macOS/Info.plist" "$contents_directory/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $app_version" \
    "$contents_directory/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $app_version" \
    "$contents_directory/Info.plist"
chmod +x "$contents_directory/MacOS/HappyPhoton"
sign_target "$app_bundle"
codesign --verify --deep --strict --verbose=2 "$app_bundle"
echo "$app_bundle"
