#!/usr/bin/env bash
set -euo pipefail

cd /work
mkdir -p sources prefix build /out/runtime /out/licenses
export SOURCE_DATE_EPOCH=1793836800
export PKG_CONFIG_LIBDIR=/work/prefix/lib/pkgconfig
jobs=${BUILD_JOBS:-4}
[[ "$jobs" =~ ^[1-9][0-9]*$ ]]

if [[ -f /input/corresponding-source.tar.xz ]]; then
    tar -xJf /input/corresponding-source.tar.xz
    cmp /recipe/sources.lock.tsv build-recipe/sources.lock.tsv
else
    while IFS=$'\t' read -r name url commit; do
        git init -q "sources/$name"
        git -C "sources/$name" -c advice.detachedHead=false fetch -q --depth=1 "$url" "$commit"
        git -C "sources/$name" -c advice.detachedHead=false checkout -q FETCH_HEAD
        git -C "sources/$name" submodule update --init --recursive --depth=1
        test "$(git -C "sources/$name" rev-parse HEAD)" = "$commit"
    done < /recipe/sources.lock.tsv

fi

mkdir -p build-recipe
cp /recipe/{build.sh,bootstrap.sh,cross.ini,Dockerfile,sources.lock.tsv,verify-imports.py} build-recipe/
tar --exclude=.git --sort=name --mtime="@$SOURCE_DATE_EPOCH" --owner=0 --group=0 --numeric-owner \
    -cJf /out/corresponding-source.tar.xz sources build-recipe

build_static() {
    local name=$1
    shift
    meson setup "build/$name" "sources/$name" --cross-file /recipe/cross.ini \
        --prefix=/work/prefix --libdir=lib --auto-features=disabled --wrap-mode=nodownload \
        --buildtype=release --default-library=static "$@"
    meson compile -C "build/$name" -j "$jobs"
    meson install -C "build/$name" --no-rebuild
}

# Use the upstream MinGW/Meson structure with an explicitly limited dependency set.
build_static freetype -Dharfbuzz=disabled -Dzlib=internal
build_static fribidi -Dtests=false -Ddocs=false -Dbin=false
build_static harfbuzz -Dtests=disabled -Dfreetype=disabled -Ddocs=disabled
build_static libass -Ddirectwrite=enabled
build_static libplacebo -Ddemos=false

pushd build >/dev/null
mkdir ffmpeg
pushd ffmpeg >/dev/null
../../sources/ffmpeg/configure --prefix=/work/prefix --arch=x86_64 --target-os=mingw32 \
    --pkg-config=pkg-config --cross-prefix=x86_64-w64-mingw32- --cc=x86_64-w64-mingw32-gcc-posix \
    --cxx=x86_64-w64-mingw32-g++-posix --enable-cross-compile --enable-w32threads \
    --disable-autodetect --disable-programs --disable-doc --disable-debug \
    --enable-static --disable-shared --enable-version3 --disable-gpl --disable-nonfree
grep -qx '#define CONFIG_GPL 0' config.h
grep -qx '#define CONFIG_NONFREE 0' config.h
grep -qx '#define CONFIG_VERSION3 1' config.h
make -j"$jobs"
make install
popd >/dev/null
popd >/dev/null

meson setup build/mpv sources/mpv --cross-file /recipe/cross.ini \
    --prefix=/work/prefix --libdir=lib --auto-features=disabled --wrap-mode=nodownload \
    --buildtype=release --prefer-static --default-library=shared \
    -Dlibmpv=true -Dcplayer=false -Dgpl=false -Dbuild-date=false \
    -Dlua=disabled -Dgl=enabled -Dgl-win32=enabled -Dwasapi=enabled -Dwin32-threads=enabled
meson compile -C build/mpv -j "$jobs"
cp build/mpv/libmpv-2.dll /out/runtime/
x86_64-w64-mingw32-strip --strip-unneeded /out/runtime/libmpv-2.dll
x86_64-w64-mingw32-objdump -p /out/runtime/libmpv-2.dll > /out/pe.txt
x86_64-w64-mingw32-objdump -f /out/runtime/libmpv-2.dll | grep -q 'file format pei-x86-64'

python3 /recipe/verify-imports.py /out/pe.txt
while IFS=$'\t' read -r name _ _; do
    while IFS= read -r -d '' file; do
        install -Dm644 "$file" "/out/licenses/${file#sources/}"
    done < <(find "sources/$name" -type f \
        \( -iname 'copying*' -o -iname 'license*' -o -iname 'notice*' -o -iname 'ftl.txt' -o -iname 'copyright*' \) -print0)
    test -d "/out/licenses/$name"
done < /recipe/sources.lock.tsv
cp /recipe/{build.sh,bootstrap.sh,cross.ini,Dockerfile,sources.lock.tsv,verify-imports.py} /out
cp build/ffmpeg/{config.h,ffbuild/config.log} /out/
cp build/mpv/meson-logs/meson-log.txt /out/
dpkg-query -W > /out/toolchain-packages.tsv
(cd /out && sha256sum runtime/libmpv-2.dll > runtime.sha256)
if [[ -f /out/corresponding-source.tar.xz ]]; then
    (cd /out && sha256sum corresponding-source.tar.xz > corresponding-source.tar.xz.sha256)
fi
