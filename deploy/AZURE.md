# Deploying Fitzobit to Azure App Service (Free F1)

Result: Fitzobit runs at `https://<your-app-name>.azurewebsites.net`, with its SQLite database on the app's
persistent storage (`D:\home\data\fitzobit.db`). The Free (F1) tier costs nothing.

> **Free-tier limits** (check Azure's current pricing page): about 60 CPU-minutes per day, 1 GB memory, 1 GB storage,
> no "Always On". The app sleeps after about 20 minutes without visitors, so the first page after a pause takes
> 10–20 seconds to load. If the daily CPU quota is used up, the site stops until the quota resets. Fine for a
> small team; move to the Basic (B1) tier if it becomes busy.

---

## 1. Create the web app (Azure Portal, about 5 minutes)

1. Sign in at <https://portal.azure.com>. A free Azure account needs a card for identity verification; the F1 tier itself is free.
2. **Create a resource → Web App**, then fill in:

   | Field | Value |
   |---|---|
   | Resource group | **Create new**: `fitzobit-rg` |
   | Name | A unique name, e.g. `fitzobit-<yourname>`. It becomes `https://<name>.azurewebsites.net` |
   | Publish | **Code** |
   | Runtime stack | **.NET 8 (LTS)** |
   | Operating system | **Windows** |
   | Region | **Central India** (or the region closest to your users) |
   | Pricing plan | **Create new** plan, then under *Pricing plan* choose **Free F1** |

3. **Review + create → Create**, wait for *Deployment complete*, then **Go to resource**.

## 2. Configure it

**Settings → Environment variables → App settings → + Add**, one at a time:

| Name | Value |
|---|---|
| `Database__ConnectionString` | `Data Source=D:\home\data\fitzobit.db` |
| `Database__SqliteJournalMode` | `DELETE` |
| `Auth__AllowRemoteSetup` | `true` &nbsp; *(temporary: remove it after step 4)* |

Click **Apply → Confirm**.

Then **Settings → Configuration → General settings**: set **HTTPS Only** to **On**, then **Save**.

Why these settings:
- **`D:\home`** is the app's persistent storage, and putting the database outside the site folder means a redeploy never touches it.
- **`DELETE` journal mode:** `D:\home` is network-backed, where SQLite's default WAL mode is unsafe.
- **HTTPS Only:** sign-in cookies are marked `Secure` in Production, so the site must be used over https.

## 3. Build and upload the app

On your PC, from the project folder:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\publish-azure.ps1
```

This builds the UI and the server and creates `artifacts\fitzobit-azure.zip`, about 18 MB.

Upload it:

1. Open `https://<your-app-name>.scm.azurewebsites.net/ZipDeployUI`. This is Kudu, the app's admin site; it signs you in with your Azure account.
2. Drag `artifacts\fitzobit-azure.zip` onto the file area and wait until the progress finishes.

*(Alternative: in Visual Studio, right-click **Fitzobit.Web → Publish → Azure → Azure App Service (Windows)**, choose
the app and publish. Run `npm run build` in `src\Fitzobit.UI` first so the UI is included.)*

## 4. First sign-in

1. Open `https://<your-app-name>.azurewebsites.net`. The first load can take about 20 seconds: it creates the database, applies migrations and loads the catalogue.
2. **Create the administrator** (your name, user name and password).
3. **Close the setup door:** back in the portal, **Environment variables**, delete `Auth__AllowRemoteSetup`, then **Apply**. Setup also refuses to run once a user exists, but removing the setting is cleaner.
4. In the app, **Settings → Users → Add user** for each Manager.

## 5. Optional: bring your local data

Only if you want the customers and groups from your PC's `data\fitzobit.db` on Azure; otherwise start fresh.

1. Stop the app on your PC (Visual Studio or `start.ps1`) and check there is **no** `fitzobit.db-wal` file left in `data\`.
2. Azure portal: **Overview → Stop** the web app.
3. Kudu: `https://<your-app-name>.scm.azurewebsites.net/DebugConsole`, go to `D:\home\data` (create the `data` folder if missing) and drag `fitzobit.db` in, replacing the existing one.
4. **Start** the web app. Local users and passwords come across too; skip step 4.2–4.3 if you already have an admin.

## Updating the app later

Run `deploy\publish-azure.ps1` again and drag the new zip onto **ZipDeployUI**. Customers, users and settings are kept;
new migrations apply automatically on the next start.

## Backups

Kudu **DebugConsole** → `D:\home\data` → download `fitzobit.db`. Do it regularly; the free tier has no automatic backups.

## Troubleshooting

| Symptom | Fix |
|---|---|
| “First-run setup must be completed on the server itself” | `Auth__AllowRemoteSetup` is missing or not `true` (step 2) |
| Signed out straight after signing in | Use the `https://` address; turn on **HTTPS Only** |
| Site shows *403 – This web app is stopped* | The daily free CPU quota was used up; it resets daily (or move to B1) |
| *HTTP Error 500.30* | Portal → **Log stream** shows the startup error; usually a typo in an app setting |
| Very slow first page | Normal on F1 after idle time (no Always On) |
