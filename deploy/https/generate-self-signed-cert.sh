#!/bin/bash
# Önaláírt tanúsítvány a HTTPS-proxy kipróbálásához (helyi gépen vagy belső teszthez).
# Élesben az intézmény (vagy pl. Let's Encrypt) által kiadott tanúsítványt másold a certs mappába
# fullchain.pem és privkey.pem néven.
set -euo pipefail
HOSTNAME_ARG="${1:-localhost}"
CERT_DIR="$(cd "$(dirname "$0")" && pwd)/certs"
mkdir -p "$CERT_DIR"
openssl req -x509 -nodes -newkey rsa:2048 -days 365 \
  -keyout "$CERT_DIR/privkey.pem" -out "$CERT_DIR/fullchain.pem" \
  -subj "/CN=$HOSTNAME_ARG" \
  -addext "subjectAltName=DNS:$HOSTNAME_ARG,DNS:localhost,IP:127.0.0.1"
chmod 600 "$CERT_DIR/privkey.pem"
echo "Tanúsítvány elkészült: $CERT_DIR (CN=$HOSTNAME_ARG)"
