# Community frontend (main.ts, no cloud pages). The monorepo's own src/TempooAnalytics/Dockerfile builds the cloud
# configuration; this one is the one the public self-host stack uses.
FROM node:22-alpine AS build
WORKDIR /app
COPY src/TempooAnalytics/package*.json ./
RUN npm ci
COPY src/TempooAnalytics/ ./
RUN npm run build:community

FROM nginx:alpine
ENV PORT=8080
COPY --from=build /app/dist/community/browser /usr/share/nginx/html
# nginx's mime.types has no .mjs entry; MapLibre's module worker (assets/maplibre/*.mjs) must be served as JavaScript.
RUN printf 'server {\n    listen ${PORT};\n    access_log off;\n    location / {\n        root /usr/share/nginx/html;\n        index index.html index.htm;\n        try_files $uri $uri/ /index.html;\n    }\n    location ~* [.]mjs$ {\n        root /usr/share/nginx/html;\n        default_type text/javascript;\n    }\n}\n' > /etc/nginx/conf.d/default.conf.template
# Runtime settings of the instance: SUPPORT_EMAIL (contact address shown on /feedback and /docs; empty = none) and BRAND_NAME
# (header text; empty = "Tempoo Analytics") are written to /config.js at start, so they change without rebuilding the image.
COPY selfhost/write-config.sh /docker-entrypoint-config.sh
EXPOSE 8080
CMD ["/bin/sh", "-c", ". /docker-entrypoint-config.sh && envsubst '${PORT}' < /etc/nginx/conf.d/default.conf.template > /etc/nginx/conf.d/default.conf && nginx -g 'daemon off;'"]
