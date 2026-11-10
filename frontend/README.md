# ProjektLabor frontend

React 19, TypeScript és Vite alapú, szerepkörös egyoldalas alkalmazás. A felület
az ASP.NET Core API-t az `/api` útvonalon éri el; a JWT-t az Axios kliens
automatikusan csatolja. A megjelenés saját CSS-t és Lucide ikonkészletet használ.

## Fejlesztés

Node.js 20 vagy újabb verzió szükséges:

```bash
npm ci
npm run dev
```

A fejlesztői szerver alapértelmezés szerint a Vite beállítás szerinti porton
indul. Docker Compose használatakor a frontend a `http://localhost:3000`
címen érhető el, a backend pedig a compose hálózaton keresztül szolgálja ki az
API-kéréseket.

## Ellenőrzések

```bash
npm test
npm run lint
npm run build
```

A production build TypeScript-ellenőrzést és Vite buildet is futtat. A lint
ESLintet használ. Az automatizált UI-teszteket Vitest, React Testing Library és
jsdom futtatja.

## Funkciók és korlátok

A felület foglalási, terem-, kulcsmozgási, hibajegy- és adminisztrációs
folyamatokat tartalmaz. Az oktató és a portás az „Értesítéseim” oldalon a
backend által az adatbázisban tárolt értesítéseket láthatja; a portás az esti,
vissza nem adott kulcs, a helyiség és a határidő adatait is megkapja, és az
értesítést olvasottként jelölheti. E-mail, SMS és push értesítési csatorna
jelenleg nincs bekötve.
