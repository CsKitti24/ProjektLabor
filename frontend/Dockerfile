# Alap image Node.js-hez
FROM node:20-alpine

WORKDIR /app

# Csomagfájlok bemásolása
COPY package*.json ./

# Függőségek telepítése
RUN npm install

# Frontend fájlok bemásolása
COPY . .

# Vite portja
EXPOSE 3000

# Vite indítása
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0", "--port", "3000"]