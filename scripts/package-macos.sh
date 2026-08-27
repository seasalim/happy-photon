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
entitlements_file="$project_root/Platforms/macOS/HappyPhoton.entitlements"

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
if [[ -n "${HAPPY_PHOTON_SOURCE_REVISION:-}" ]]; then
    publish_arguments+=(
        -p:SourceRevisionId="$HAPPY_PHOTON_SOURCE_REVISION"
        -p:SourceRevision="$HAPPY_PHOTON_SOURCE_REVISION"
    )
fi
if [[ -n "${HAPPY_PHOTON_BUILD_TIMESTAMP:-}" ]]; then
    publish_arguments+=(
        -p:BuildTimestampUtc="$HAPPY_PHOTON_BUILD_TIMESTAMP"
    )
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

sign_app_bundle() {
    if [[ "$signing_identity" == "-" ]]; then
        codesign --force --entitlements "$entitlements_file" \
            --sign - "$app_bundle"
    else
        codesign --force --options runtime --timestamp \
            --entitlements "$entitlements_file" \
            --sign "$signing_identity" "$app_bundle"
    fi
}

cp -a "$publish_directory/." "$contents_directory/MacOS/"
apphost="$contents_directory/MacOS/HappyPhoton"
bridge_dylib="$contents_directory/MacOS/libhappyphoton_libraw_bridge.dylib"
libraw_dylib="$contents_directory/MacOS/libraw.25.dylib"
[[ -f "$apphost" ]] || { echo "Happy Photon apphost is absent from publish output" >&2; exit 1; }
file -b "$apphost" | grep -q 'Mach-O' || {
    echo "Happy Photon apphost is not Mach-O; refusing to relocate it" >&2
    exit 1
}
[[ -f "$bridge_dylib" ]] || { echo "LibRaw bridge dylib is absent from publish output" >&2; exit 1; }
[[ -f "$libraw_dylib" ]] || { echo "LibRaw companion dylib is absent from publish output" >&2; exit 1; }
otool -D "$bridge_dylib" | grep -Fxq '@loader_path/libhappyphoton_libraw_bridge.dylib' || {
    echo "LibRaw bridge install identity is not package-local" >&2
    exit 1
}
otool -D "$libraw_dylib" | grep -Fxq '@loader_path/libraw.25.dylib' || {
    echo "LibRaw companion install identity is not package-local" >&2
    exit 1
}
staged_file_list="$temporary_directory/staged-files"
mach_o_file_list="$temporary_directory/mach-o-files"
remaining_file_list="$temporary_directory/remaining-files"
find "$contents_directory/MacOS" -type f -print0 > "$staged_file_list"
: > "$mach_o_file_list"
while IFS= read -r -d '' staged_file; do
    relative_path="${staged_file#"$contents_directory/MacOS/"}"
    if file -b "$staged_file" | grep -q 'Mach-O'; then
        printf '%q\n' "$relative_path" >> "$mach_o_file_list"
    else
        destination="$contents_directory/Resources/$relative_path"
        mkdir -p "$(dirname "$destination")"
        mv "$staged_file" "$destination"
    fi
done < "$staged_file_list"

find "$contents_directory/MacOS" -depth -mindepth 1 -type d -empty -delete
[[ -f "$apphost" ]] || { echo "Happy Photon apphost was lost during resource relocation" >&2; exit 1; }
if [[ -n "$(find "$contents_directory/MacOS" -mindepth 1 -type d -print -quit)" ]]; then
    echo "Directory remains in Contents/MacOS after resource relocation" >&2
    exit 1
fi
: > "$remaining_file_list"
while IFS= read -r -d '' binary; do
    relative_path="${binary#"$contents_directory/MacOS/"}"
    file -b "$binary" | grep -q 'Mach-O' || {
        echo "Non-Mach-O file remains in Contents/MacOS: $relative_path" >&2
        exit 1
    }
    printf '%q\n' "$relative_path" >> "$remaining_file_list"
done < <(find "$contents_directory/MacOS" -type f -print0)
LC_ALL=C sort -o "$mach_o_file_list" "$mach_o_file_list"
LC_ALL=C sort -o "$remaining_file_list" "$remaining_file_list"
cmp -s "$mach_o_file_list" "$remaining_file_list" || {
    echo "Contents/MacOS does not match the staged Mach-O file set" >&2
    exit 1
}
packaged_data_root="$contents_directory/Resources/data"
[[ -d "$packaged_data_root" ]] || {
    echo "Packaged data root is absent: data" >&2
    exit 1
}
for required_data_directory in lensfun lens-ids; do
    [[ -d "$packaged_data_root/$required_data_directory" ]] || {
        echo "Required packaged data directory is absent: data/$required_data_directory" >&2
        exit 1
    }
done
while IFS= read -r -d '' packaged_data; do
    if [[ -z "$(find "$packaged_data" -type f -print -quit)" ]]; then
        echo "Packaged data directory is empty: ${packaged_data#"$contents_directory/Resources/"}" >&2
        exit 1
    fi
done < <(find "$packaged_data_root" -mindepth 1 -maxdepth 1 -type d -print0)

cp "$project_root/Platforms/macOS/Info.plist" "$contents_directory/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $app_version" \
    "$contents_directory/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $app_version" \
    "$contents_directory/Info.plist"
chmod +x "$apphost"

while IFS= read -r -d '' binary; do
    if file -b "$binary" | grep -q 'Mach-O'; then
        sign_target "$binary"
    fi
done < <(find "$contents_directory/MacOS" -type f -print0)

codesign --verify --strict --verbose=2 "$bridge_dylib"
codesign --verify --strict --verbose=2 "$libraw_dylib"

sign_app_bundle
codesign --verify --deep --strict --verbose=2 "$app_bundle"
echo "$app_bundle"
