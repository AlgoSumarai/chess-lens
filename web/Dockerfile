FROM node:24.15.0-bookworm-slim AS build
WORKDIR /source
COPY package.json package-lock.json ./
RUN --mount=type=secret,id=build_ca \
    if [ -f /run/secrets/build_ca ]; then NODE_EXTRA_CA_CERTS=/run/secrets/build_ca npm ci --no-fund; else npm ci --no-fund; fi
COPY . .
RUN npm run build

FROM nginx:1.30.4-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /source/dist /usr/share/nginx/html
EXPOSE 8080
