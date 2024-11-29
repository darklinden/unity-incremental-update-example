#!/usr/bin/env bash

BASEDIR=$(dirname "$0")
SCRIPT_DIR="$(realpath "${BASEDIR}")"

echo "Starting Serve at https://127-0-0-1.traefik.me"

# if start with /cygdrive/c, then convert to c:/...
if [[ "${SCRIPT_DIR}" == /cygdrive/c* ]]; then
    SCRIPT_DIR="$(echo ${SCRIPT_DIR} | sed -e 's/\/cygdrive\/\([a-z]\)/\1:/g' -e 's/\//\\/g')"
fi

echo "SCRIPT_DIR: ${SCRIPT_DIR}"

docker run --rm -it \
    -p 80:80 \
    -p 443:443 \
    -v "${SCRIPT_DIR}/nginx/conf.d:/etc/nginx/conf.d" \
    -v "${SCRIPT_DIR}/ssl:/etc/nginx/ssl" \
    -v "${SCRIPT_DIR}/nginx/media:/etc/nginx/media" \
    -v "${SCRIPT_DIR}/serve:/usr/share/html" \
    --mount "type=bind,source=${SCRIPT_DIR}/nginx/nginx.conf,target=/etc/nginx/nginx.conf" \
    nginx:alpine
