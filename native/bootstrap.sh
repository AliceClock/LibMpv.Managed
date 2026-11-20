#!/usr/bin/env bash
set -euo pipefail

recipe=$(cd -- "$(dirname -- "$0")" && pwd)
context=$(mktemp -d)
trap 'rm -rf -- "$context"' EXIT
cp "$recipe/Dockerfile" "$context/"
curl -fSL --retry 3 \
    https://cdimage.ubuntu.com/ubuntu-base/releases/24.04/release/ubuntu-base-24.04.5-base-amd64.tar.gz \
    -o "$context/ubuntu-base.tar.gz"
(
    cd "$context"
    echo 'e77b6f10c2590cef872b33ee9f635a0e3fd1f57fb074c0e52b5c7f56147a0c86  ubuntu-base.tar.gz' | sha256sum -c -
)
docker build --platform linux/amd64 --tag libmpv-managed-build:0.42.0 "$context"
