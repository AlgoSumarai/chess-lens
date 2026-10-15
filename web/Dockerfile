# The browser bundle is portable; run build tools on the host architecture.
FROM --platform=$BUILDPLATFORM node:24.22.0-bookworm-slim AS build
WORKDIR /source
COPY package.json package-lock.json ./
RUN --mount=type=secret,id=build_ca \
    if [ -f /run/secrets/build_ca ]; then NODE_EXTRA_CA_CERTS=/run/secrets/build_ca npm ci --no-fund; else npm ci --no-fund; fi
COPY . .
RUN npm run build

# Debian retains per-package copyright texts and common licence files.
FROM nginx:1.30.6-trixie
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx-upstream.conf /etc/nginx/chesslens-upstream.conf
COPY nginx-proxy-headers.conf /etc/nginx/chesslens-proxy-headers.conf
COPY --from=build /source/dist /usr/share/nginx/html
EXPOSE 8080
