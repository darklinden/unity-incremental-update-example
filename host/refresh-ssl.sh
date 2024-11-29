#!/usr/bin/env bash

BASEDIR=$(dirname "$0")
SCRIPT_DIR="$(realpath "${BASEDIR}")"

cd "${SCRIPT_DIR}/ssl"
curl -O https://traefik.me/fullchain.pem
curl -O https://traefik.me/privkey.pem
