# =====================================================================
#  WebClient (Angular) - multi-stage Docker build
#  Build context = repository ROOT:
#    docker build -f docker/web-client/Dockerfile -t web-client .
# =====================================================================

# ---------- Stage 1: build the Angular app with Node ----------
FROM node:26-alpine AS build
WORKDIR /app

# Same caching trick as the .NET images: install dependencies first (cached until package*.json changes)
COPY src/WebClient/package.json src/WebClient/package-lock.json ./
RUN npm ci

COPY src/WebClient/ ./
RUN npx ng build --configuration production
# Output: /app/dist/web-client/browser  (plain static files: index.html, *.js, *.css)

# ---------- Stage 2: serve the static files with nginx ----------
# nginx-unprivileged = official nginx image that runs as a NON-ROOT user and listens on 8080
FROM nginxinc/nginx-unprivileged:1.29-alpine AS final

# The base image can lag behind Alpine security fixes (Trivy flags them). Patch OS packages,
# which needs root briefly, then drop back to the image's non-root nginx user (uid 101).
USER root
# hadolint ignore=DL3017
RUN apk upgrade --no-cache
USER 101

# Where nginx forwards /api calls. A non-secret, environment-independent DEFAULT is a fine use of ENV;
# override it per environment:  docker run -e API_GATEWAY_URL=http://my-gateway:8080 ...
ENV API_GATEWAY_URL=http://api-gateway:8080

# Files in /etc/nginx/templates/*.template are processed with envsubst at container start,
# so ${API_GATEWAY_URL} is replaced by the real value -> /etc/nginx/conf.d/default.conf
COPY docker/web-client/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/web-client/browser /usr/share/nginx/html

EXPOSE 8080
