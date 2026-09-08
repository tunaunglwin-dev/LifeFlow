# Blood Bank C# Prototype

This is a small blood bank demo:

- C# ASP.NET Core API for Render
- PostgreSQL database
- Vue frontend for Vercel or any static host
- Login/Register
- Donor CRUD
- Blood Stock CRUD
- Blood Request CRUD
- Blood Transfer
- Reports

## Database

Run `database/schema.sql` in the PostgreSQL database.

## Local API

```bash
cd api
dotnet run
```

Without `appsettings.json`, the API runs in demo mode with in-memory sample data. This is useful for UI testing.

To use PostgreSQL locally, copy `appsettings.example.json` to `appsettings.json`, then edit the database connection string.

## Local Frontend

Serve the `web` folder:

```bash
cd web
python -m http.server 5173
```

Open `http://localhost:5173`.

If the API URL is different, edit `web/config.js`.

## Render Deploy

Create a Render Blueprint from this repository using `render.yaml`, or create a Web Service manually:

- Root directory: `blood-bank-csharp/api`
- Runtime: Docker
- Environment variable: `DATABASE_URL`
- Environment variable: `CORS_ORIGIN` with the Vercel frontend URL
- Environment variable: `ADMIN_USERNAME`
- Environment variable: `ADMIN_PASSWORD`

After the PostgreSQL database is created, run `database/schema.sql`.

## Vercel Deploy

Deploy the `web` folder as a static project. Then edit `config.js` so `window.API_URL` points to the Render API URL.

## Presentation Files

The `docs` folder includes:

- `component-diagram.svg`
- `use-case-diagram.svg`
- `presentation-guide.md`

Open the SVG files in a browser or place them into presentation slides.
