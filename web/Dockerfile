# Build from the web folder:  docker build -t eduvibe-web web
FROM node:24 AS build
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.29-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist/eduvibe-web/browser /usr/share/nginx/html
EXPOSE 80
