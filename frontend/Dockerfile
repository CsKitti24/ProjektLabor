# --- Build: függőségek reprodukálható telepítése és éles build ---
FROM node:20-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .

# A bejelentkezési oldal demó gyorsgombjai csak akkor jelennek meg, ha ez "true".
ARG VITE_DEMO_ACCOUNTS=false
ENV VITE_DEMO_ACCOUNTS=$VITE_DEMO_ACCOUNTS
RUN npm run build

# --- Kiszolgálás: statikus fájlok nginx-szel, az /api kérések továbbítása a backendre ---
FROM nginx:1.27-alpine AS runtime

COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

# Az nginx image indításkor ezzel helyettesíti a ${BACKEND_URL} értéket a sablonban.
ENV BACKEND_URL=http://backend:8000
EXPOSE 3000
