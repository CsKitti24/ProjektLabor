# --- Build: függőségek reprodukálható telepítése és éles build ---
FROM node:22-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .

RUN npm run build

# --- Kiszolgálás: statikus fájlok nginx-szel, az /api kérések továbbítása a backendre ---
# Az nginx támogatott, stabil ága (1.30.x); a javítóverziókat a címke magától követi.
FROM nginx:1.30-alpine AS runtime

COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

# Az nginx image indításkor ezzel helyettesíti a ${BACKEND_URL} értéket a sablonban.
ENV BACKEND_URL=http://backend:8000
EXPOSE 3000
