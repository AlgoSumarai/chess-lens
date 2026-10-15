#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
umask 077
chesslens_tls_directory="${1:-.local/local-tls}"
if [[ -e "$chesslens_tls_directory" ]]; then
  echo 'Select a new TLS directory; existing keys are never overwritten.' >&2
  exit 1
fi
mkdir -p -- "$(dirname -- "$chesslens_tls_directory")"
mkdir -m 700 -- "$chesslens_tls_directory"
openssl req -x509 -newkey rsa:3072 -noenc -days 7 -sha256 \
  -subj '/CN=ChessLens isolated local verification CA' \
  -addext 'basicConstraints=critical,CA:TRUE' \
  -addext 'keyUsage=critical,keyCertSign,cRLSign' \
  -keyout "$chesslens_tls_directory/ca-key.pem" -out "$chesslens_tls_directory/ca.pem"
openssl req -new -newkey rsa:2048 -noenc -sha256 -subj '/CN=localhost' \
  -keyout "$chesslens_tls_directory/privkey.pem" -out "$chesslens_tls_directory/server.csr"
cat > "$chesslens_tls_directory/server.ext" <<'CERTIFICATE_EXTENSIONS'
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:localhost,IP:127.0.0.1
CERTIFICATE_EXTENSIONS
openssl x509 -req -days 7 -sha256 -in "$chesslens_tls_directory/server.csr" \
  -CA "$chesslens_tls_directory/ca.pem" -CAkey "$chesslens_tls_directory/ca-key.pem" \
  -CAcreateserial -extfile "$chesslens_tls_directory/server.ext" -out "$chesslens_tls_directory/server.pem"
cat "$chesslens_tls_directory/server.pem" "$chesslens_tls_directory/ca.pem" > "$chesslens_tls_directory/fullchain.pem"
openssl verify -CAfile "$chesslens_tls_directory/ca.pem" -verify_hostname localhost "$chesslens_tls_directory/server.pem"
echo 'Created seven-day localhost certificates in the new private directory. No trust store was changed.'
