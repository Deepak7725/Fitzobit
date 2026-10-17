# Free hosting on Render + Neon

Result: Fitzobit runs at `https://<name>.onrender.com`, independent of your PC, with its data in a free Neon
PostgreSQL database.

| | Free plan (check each provider's current pricing page) |
|---|---|
| **Render** (the app) | 512 MB RAM, 0.1 CPU, 750 hours/month. **Sleeps after 15 minutes without visitors**; the next visit takes about 30–60 seconds to wake it. Render may verify a card with a $1 hold that is cancelled. |
| **Neon** (the database) | No card. 0.5 GB storage (plenty for thousands of customers), 100 compute-hours per month, sleeps after 5 idle minutes and wakes in about a second. |

Your PC can be off. Locally the app uses your own PostgreSQL (see the README, *Local PostgreSQL*); the hosted copy uses Neon. The two hold separate data.

---

## 1. Put the latest code on GitHub

Render deploys from your GitHub repository (`Deepak7725/Fitzobit`). Commit and push the current code, including
`Dockerfile` and `render.yaml`. (The Docker image also installs the free Liberation fonts used by PDF exports.)

## 2. Create the database on Neon (about 3 minutes)

1. Sign up at <https://neon.com> with GitHub or Google. The free plan needs no card.
2. **Create project**:
   - Name: `fitzobit`
   - PostgreSQL version: the default
   - Region: the one closest to your Render region. For India, **AWS Asia Pacific (Singapore)** matches Render's Singapore region.
3. On the project dashboard, click **Connect** and copy the **connection string**. It looks like:
   `postgresql://neondb_owner:••••@ep-xxxx.ap-southeast-1.aws.neon.tech/neondb?sslmode=require`
   - Turn **Connection pooling off**, so the address has no `-pooler`. Fitzobit keeps its own small pool.
   - Keep this string private: it contains the database password.

## 3. Create the web service on Render (about 5 minutes)

1. Sign up at <https://render.com> with GitHub, and allow it to see the `Fitzobit` repository.
2. **New + → Blueprint**, pick `Deepak7725/Fitzobit`. Render reads `render.yaml` and proposes a free web service called **fitzobit**.
   - If the name is taken, change it; it becomes `https://<name>.onrender.com`.
   - Choose region **Singapore**, to match Neon.
3. Render asks for **`Database__ConnectionString`**: paste the Neon string from step 2.3.
4. Click **Apply / Deploy**. The first build takes about 5–10 minutes (it builds the UI and the server in Docker).
   Wait until the log ends with *Your service is live*.

`render.yaml` also sets these, so you don't need to:
- `ForwardedHeaders__TrustAllProxies=true`
- `Auth__SetupCode`, a random secret Render generates.

## 4. Create the administrator

1. In Render, open the **fitzobit** service → **Environment** and reveal **`Auth__SetupCode`**. Copy it.
2. Open `https://<name>.onrender.com`. The first load creates all tables and loads the catalogue, so give it up to a minute.
3. The **Create the administrator** screen asks for the **setup code**. Paste it, then enter your name, user name and password.
4. Optional: delete `Auth__SetupCode` in Render → Environment. Setup is refused anyway once a user exists.
5. In the app: **Settings → Users** → add each Manager.

## 5. Daily use

- **Share** `https://<name>.onrender.com` with your team.
- **First visit after a quiet spell is slow** (30–60 s) while Render wakes the app; after that it is quick.
- **Updates:** push to GitHub, and Render rebuilds and redeploys automatically (`autoDeploy`). Data in Neon is untouched; new migrations apply on startup.
- **Sign-ins survive redeploys:** the cookie keys are stored in the database.
- **Backups:** Neon keeps a short restore window. For your own copy, use Neon's dashboard (*Backup & Restore*), or `pg_dump` with the connection string, now and then.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Build fails | Render → **Logs**. Usually code that was not pushed; push again and click **Manual Deploy → Deploy latest commit** |
| *Application failed to respond* / 500 on first start | Check `Database__ConnectionString` (Neon string, with `sslmode=require`); see **Logs** for the error |
| “The setup code is missing or not correct” | Copy `Auth__SetupCode` again from Render → Environment (no spaces) |
| “First-run setup must be completed on the server itself” | `Auth__SetupCode` was deleted before the admin existed. Add it back (any long random text) and reload |
| Signed out after every deploy | Make sure you are on the latest code (keys are stored in the `DataProtectionKeys` table) |
| Very slow first page | Normal on the free plan after 15 idle minutes |
| Large Excel imports are slow | The free instance has a tenth of a CPU. A few hundred customers import in seconds; a file with thousands of rows can take a minute or more, so split very large files |
| Neon says compute hours are used up | Rare for a small team; the database sleeps when idle. Check the Neon dashboard usage page |

## Moving existing local data (optional)

Local and hosted databases are separate. To copy local customers to Neon, use `pg_dump` from the local database and
`psql` against the Neon connection string (ask if you need step-by-step instructions). An existing Neon database is
upgraded in place by the migrations when the new version starts; its customers are kept.
