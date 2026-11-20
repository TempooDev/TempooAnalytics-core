FROM node:22-alpine AS build
WORKDIR /app

# Install dependencies
COPY src/TempooAnalytics/package*.json ./
RUN npm install

# Copy source code and build
COPY src/TempooAnalytics/ ./
RUN npm run build

# Final stage: Serve with Nginx
FROM nginx:alpine
ENV PORT=8080
COPY --from=build /app/dist/TempooAnalytics/browser /usr/share/nginx/html
# Custom nginx conf to handle SPAs (routing to index.html)
# nginx's mime.types has no .mjs entry; MapLibre's module worker (assets/maplibre/*.mjs) must be served as JavaScript.
RUN printf 'server {\n    listen ${PORT};\n    access_log off;\n    location / {\n        root /usr/share/nginx/html;\n        index index.html index.htm;\n        try_files $uri $uri/ /index.html;\n    }\n    location ~* [.]mjs$ {\n        root /usr/share/nginx/html;\n        default_type text/javascript;\n    }\n}\n' > /etc/nginx/conf.d/default.conf.template

EXPOSE 8080
CMD ["/bin/sh", "-c", "envsubst '${PORT}' < /etc/nginx/conf.d/default.conf.template > /etc/nginx/conf.d/default.conf && nginx -g 'daemon off;'"]
