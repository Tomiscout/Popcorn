#!/bin/bash
# Builds MPV/src into MPV/windows/libmpv-2.dll, one DLL with its dependencies linked in statically (only vulkan-1.dll stays
# external: the GPU driver's loader, no static version exists). Run by build_windows.ps1 inside MPV/msys64 (UCRT64).
# FFmpeg gets every video and subtitle decoder there is for Windows, incl. the external-library ones (AVS2/AVS3, EVC,
# JPEG XS, VP8/VP9 with alpha, teletext, ARIB captions); image-only ones (JPEG XL, SVG) and encoders are left out.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
BUILD="$HERE/build-windows"
DEPS="$HERE/build-windows-deps"
TOOLS="$HERE/build-windows-tools"
OUT="$HERE/windows"
# Bump when the dependency builds below change: the next run then rebuilds them and mpv.
DEPS_REVISION=2
FFMPEG_VERSION=9.0.2
FFMPEG_SHA256=8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e
PYTHON_VERSION=3.14.10
PYTHON_SHA256=90905e64a840557d8c1bc4f5b74db3d7ee85e3ffb217611cac1c7813acea4159
MESON_VERSION=1.12.1
MESON_SHA256=ab0a6ca09f8ef70c564c8241fb5a23957886a0b53fb58412b5e07eaf07dba743

P=mingw-w64-ucrt-x86_64
pacman -Syu --noconfirm
pacman -S --noconfirm --needed make diffutils git \
    $P-cc $P-ninja $P-cmake $P-pkgconf $P-nasm $P-ffnvcodec-headers $P-amf-headers \
    $P-dav1d $P-libass $P-libplacebo $P-shaderc $P-spirv-cross $P-vulkan-devel $P-libiconv $P-zlib $P-bzip2 \
    $P-libvpx $P-aom $P-openh264 $P-libvpl $P-zvbi $P-aribb24 $P-uchardet

# Meson, CMake and the compiler are Windows programs and do not understand MSYS2 paths like /ucrt64.
UCRT64_WIN="$(cygpath -m /ucrt64)"
DEPS_WIN="$(cygpath -m "$DEPS")"

if [ "$(cat "$DEPS/revision" 2>/dev/null)" != "$DEPS_REVISION" ]; then
    rm -rf "$DEPS" "$BUILD"
fi
mkdir -p "$DEPS/src" "$DEPS/lib/pkgconfig"
echo "$DEPS_REVISION" > "$DEPS/revision" # each step below checks for its own output, so a rerun continues after a failure

# The .pc files of shaderc and spirv-cross name their DLLs even for static linking. The Vulkan import library gets the
# name of a static one, as -static only looks for those. Paths written out: pkgconf resets prefix to the .pc file's location.
cp /ucrt64/lib/libvulkan-1.dll.a "$DEPS/lib/libvulkan-1.a"
sed -E "s|\\$\{(exec_)?prefix\}|$UCRT64_WIN|g; s|-lshaderc_shared|-lshaderc_combined -lglslang-default-resource-limits -lglslang -lSPIRV -lMachineIndependent -lOSDependent -lGenericCodeGen -lSPIRV-Tools-opt -lSPIRV-Tools -lstdc++|" \
    /ucrt64/lib/pkgconfig/shaderc.pc > "$DEPS/lib/pkgconfig/shaderc.pc"
sed -E "s|\\$\{(exec_)?prefix\}|$UCRT64_WIN|g; s|-lspirv-cross-c-shared|-lspirv-cross-c -lspirv-cross-cpp -lspirv-cross-msl -lspirv-cross-hlsl -lspirv-cross-reflect -lspirv-cross-glsl -lspirv-cross-core -lstdc++|" \
    /ucrt64/lib/pkgconfig/spirv-cross-c-shared.pc > "$DEPS/lib/pkgconfig/spirv-cross-c-shared.pc"
sed -E "s|\\$\{(exec_)?prefix\}|$UCRT64_WIN|g; s|^Libs:.*|Libs: -L$DEPS_WIN/lib -lvulkan-1|" \
    /ucrt64/lib/pkgconfig/vulkan.pc > "$DEPS/lib/pkgconfig/vulkan.pc"

# Decoder libraries MSYS2 has no static build of. Full clones: davs2, uavs3d and xevd take their version from git.
fetch_git() { # name url commit
    if [ ! -d "$DEPS/src/$1" ]; then
        git clone --quiet "$2" "$DEPS/src/$1"
    fi
    git -C "$DEPS/src/$1" checkout --quiet --force "$3"
}
# CMAKE_SYSTEM_NAME makes it a cross build, so nothing it compiles gets run (Smart App Control).
build_cmake() { # name source-subdir cmake-args...
    local src="$DEPS/src/$1/$2"
    shift 2
    cmake -G Ninja -S "$(cygpath -m "$src")" -B "$(cygpath -m "$src/build-windows")" \
        -DCMAKE_SYSTEM_NAME=Windows -DCMAKE_SYSTEM_PROCESSOR=AMD64 -DCMAKE_C_COMPILER=gcc -DCMAKE_CXX_COMPILER=g++ -DCMAKE_RC_COMPILER=windres \
        -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$DEPS_WIN" -DCMAKE_INSTALL_LIBDIR=lib \
        -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DBUILD_SHARED_LIBS=OFF "$@"
    cmake --build "$(cygpath -m "$src/build-windows")"
    cmake --install "$(cygpath -m "$src/build-windows")"
}

if [ ! -f "$DEPS/lib/libdavs2.a" ]; then # AVS2
    fetch_git davs2 https://github.com/pkuvcl/davs2 b06d7585620f4e90a6d19a2926bb4e59793b8942 # 1.7
    pushd "$DEPS/src/davs2/build/linux"
    ./configure --prefix="$DEPS_WIN" --host=x86_64-w64-mingw32 --disable-cli --enable-pic
    make -j"$(nproc)"
    make install
    popd
fi
if [ ! -f "$DEPS/lib/libuavs3d.a" ]; then # AVS3
    fetch_git uavs3d https://github.com/uavs3/uavs3d 0e20d2c291853f196c68922a264bcd8471d75b68 # 1.2
    build_cmake uavs3d . -DCOMPILE_10BIT=ON
fi
if [ ! -f "$DEPS/lib/libxevd.a" ]; then # MPEG-5 EVC
    fetch_git xevd https://github.com/mpeg5/xevd 70e18a507022790339690407c263ac172ace3cb0 # v0.5.0
    build_cmake xevd .
    # It installs the static library into lib/xevd/ and always builds the DLL too.
    mv "$DEPS/lib/xevd/libxevd.a" "$DEPS/lib/"
    rm -rf "$DEPS/lib/xevd" "$DEPS/lib/libxevd.dll.a" "$DEPS/bin/libxevd.dll"
fi
if [ ! -f "$DEPS/lib/libaribcaption.a" ]; then # ARIB captions; MSYS2's static one has headers that dllimport
    fetch_git libaribcaption https://github.com/xqq/libaribcaption c64c23b8905ba514b87c9789269e9f66f949ffe0 # v1.1.2
    build_cmake libaribcaption . -DARIBCC_SHARED_LIBRARY=OFF -DARIBCC_USE_FREETYPE=OFF # DirectWrite renders
fi
if [ ! -f "$DEPS/lib/libSvtJpegxs.a" ]; then # JPEG XS
    fetch_git svt-jpegxs https://github.com/OpenVisualCloud/SVT-JPEG-XS 01bc7cc7123d2e4a3e330632fabec873f3cee62b # v0.10.0
    build_cmake svt-jpegxs . -DBUILD_APPS=OFF -DBUILD_TESTING=OFF
fi

# MSYS2's FFmpeg links dozens of libraries (whisper.cpp, librsvg, ...) mostly without a static version: own build.
# Cross build, so configure does not run the test programs it compiles (see windows.cross).
# GPL + version3: davs2 and aribb24 need them, so do some of FFmpeg's own decoders (ahx, adpcm_*); mpv is GPL anyway.
if [ ! -f "$DEPS/lib/libavcodec.a" ]; then
    [ -f "$DEPS/src/ffmpeg.tar.xz" ] || curl -fL --retry 5 --retry-all-errors "https://ffmpeg.org/releases/ffmpeg-$FFMPEG_VERSION.tar.xz" -o "$DEPS/src/ffmpeg.tar.xz"
    echo "$FFMPEG_SHA256 *$DEPS/src/ffmpeg.tar.xz" | sha256sum -c -
    tar -xf "$DEPS/src/ffmpeg.tar.xz" -C "$DEPS/src"
    mkdir -p "$DEPS/src/ffmpeg-build"
    pushd "$DEPS/src/ffmpeg-build"
    PKG_CONFIG_PATH="$DEPS_WIN/lib/pkgconfig" "../ffmpeg-$FFMPEG_VERSION/configure" --prefix="$DEPS_WIN" \
        --enable-cross-compile --arch=x86_64 --target-os=mingw32 --cc=gcc --cxx=g++ --pkg-config-flags=--static \
        --enable-static --disable-shared --disable-programs --disable-doc --disable-debug --disable-avdevice \
        --enable-gpl --enable-version3 --disable-autodetect --enable-zlib --enable-bzlib --enable-iconv --enable-schannel \
        --enable-d3d11va --enable-dxva2 --enable-ffnvcodec --enable-nvdec --enable-cuvid --enable-amf --enable-libvpl \
        --enable-vulkan \
        --enable-libdav1d --enable-libaom --enable-libvpx --enable-libopenh264 --enable-libdavs2 --enable-libuavs3d \
        --enable-libxevd --enable-libsvtjpegxs \
        --enable-libzvbi --enable-libaribb24 --enable-libaribcaption \
        --disable-encoders --enable-encoder=png,mjpeg,ac3 --disable-muxers --enable-muxer=spdif
    make -j"$(nproc)"
    make install
    echo "FFmpeg: $(grep -c '_decoder,' libavcodec/codec_list.c) decoders"
    popd
fi

# Meson runs on python.org's embeddable Python, which is signed: Smart App Control blocks MSYS2's unsigned Python modules
# whenever their reputation lapses. mpv's own build scripts get the same Python via python.cross.
if [ ! -f "$TOOLS/python.cross" ]; then
    rm -rf "$TOOLS"
    mkdir -p "$TOOLS/python"
    curl -fL --retry 5 --retry-all-errors "https://www.python.org/ftp/python/$PYTHON_VERSION/python-$PYTHON_VERSION-embed-amd64.zip" -o "$TOOLS/python.zip"
    echo "$PYTHON_SHA256 *$TOOLS/python.zip" | sha256sum -c -
    "$(cygpath -u "$SYSTEMROOT")/System32/tar.exe" -xf "$(cygpath -w "$TOOLS/python.zip")" -C "$(cygpath -w "$TOOLS/python")" # signed, unlike bsdtar
    curl -fL --retry 5 --retry-all-errors "https://github.com/mesonbuild/meson/releases/download/$MESON_VERSION/meson-$MESON_VERSION.tar.gz" -o "$TOOLS/meson.tar.gz"
    echo "$MESON_SHA256 *$TOOLS/meson.tar.gz" | sha256sum -c -
    tar -xzf "$TOOLS/meson.tar.gz" -C "$TOOLS"
    # The ._pth file is the embeddable Python's whole sys.path.
    pth=("$TOOLS"/python/python*._pth)
    printf '\n%s\n' "$(cygpath -w "$TOOLS/meson-$MESON_VERSION")" >> "${pth[0]}"
    printf "[binaries]\npython3 = '%s'\npython = '%s'\n" "$(cygpath -m "$TOOLS/python/python.exe")" \
        "$(cygpath -m "$TOOLS/python/python.exe")" > "$TOOLS/python.cross"
fi
meson() {
    "$TOOLS/python/python.exe" "$(cygpath -w "$TOOLS/meson-$MESON_VERSION/meson.py")" "$@"
}

if [ ! -f "$BUILD/build.ninja" ]; then
    rm -rf "$BUILD"
    # Same machine, but set up as a cross build (windows.cross): meson then does not run the test programs it compiles,
    # which Windows Smart App Control blocks.
    meson setup "$BUILD" "$HERE/src" --cross-file "$HERE/windows.cross" --cross-file "$TOOLS/python.cross" \
        --pkg-config-path "$DEPS_WIN/lib/pkgconfig" \
        --buildtype=release --prefer-static --default-library=shared \
        -Dc_link_args=-static -Dcpp_link_args=-static \
        -Dlibmpv=true -Dcplayer=false -Dtests=false \
        -Dgl=enabled -Dd3d11=enabled -Dvulkan=enabled -Dshaderc=enabled -Dspirv-cross=enabled -Duchardet=enabled \
        -Dlibarchive=disabled -Dlibcurl=disabled -Dlua=disabled -Djavascript=disabled -Dwayland=disabled -Dx11=disabled -Ddrm=disabled
fi
meson compile -C "$BUILD"

rm -rf "$OUT"
mkdir -p "$OUT"
cp "$BUILD/libmpv-2.dll" "$OUT/"
strip "$OUT/libmpv-2.dll"

# Import tables, not ldd: ldd loads the DLL, and Smart App Control can block that.
imports="$(objdump -p "$OUT/libmpv-2.dll" | sed -n 's/^[[:space:]]*DLL Name: //p' | tr -d '\r')"
for name in $imports; do
    if [ -f "/ucrt64/bin/$name" ] && [ "$name" != "vulkan-1.dll" ]; then
        echo "libmpv-2.dll still needs $name, it did not link statically" >&2
        exit 1
    fi
done
# Meson's check for the Vulkan headers turns the feature off without an error, even with -Dvulkan=enabled.
if ! echo "$imports" | grep -qx "vulkan-1.dll"; then
    echo "libmpv-2.dll was built without Vulkan" >&2
    exit 1
fi
echo "libmpv: $(du -m "$OUT/libmpv-2.dll" | cut -f1) MB in $OUT"
