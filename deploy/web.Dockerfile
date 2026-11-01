# Image du frontend : l'application Angular (PWA) servie par un serveur web léger, qui relaie /api vers l'API.
# Construire depuis la racine du dépôt : docker build -f deploy/web.Dockerfile -t soro-web .
FROM node:24-alpine AS build
WORKDIR /src
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci
COPY src/frontend ./
RUN npx ng build

FROM nginxinc/nginx-unprivileged:1.30-alpine AS run
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/soro-caisse/browser /usr/share/nginx/html
EXPOSE 8080
