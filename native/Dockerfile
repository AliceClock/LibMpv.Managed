FROM scratch
ADD ubuntu-base.tar.gz /
ENV PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
ENV DEBIAN_FRONTEND=noninteractive

# Bootstrap HTTPS using the signed Ubuntu archive and pin all build tools to a snapshot.
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates \
    && sed -i -e 's#http://archive.ubuntu.com/ubuntu/#https://snapshot.ubuntu.com/ubuntu/20261105T000000Z/#' \
       -e 's#http://security.ubuntu.com/ubuntu/#https://snapshot.ubuntu.com/ubuntu/20261105T000000Z/#' \
       /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get -o Acquire::Check-Valid-Until=false update \
    && apt-get install -y --no-install-recommends git make gcc g++ g++-mingw-w64-x86-64-posix \
       nasm meson ninja-build pkg-config python3-jinja2 python3-markupsafe xz-utils \
    && apt-get clean
WORKDIR /work
ENTRYPOINT ["bash", "/recipe/build.sh"]
