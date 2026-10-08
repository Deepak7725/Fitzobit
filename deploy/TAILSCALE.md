# Free hosting from your PC with Tailscale Funnel

Fitzobit runs on this PC, and Tailscale Funnel gives it a permanent public address such as
`https://my-pc.tail1234.ts.net`, with a real HTTPS certificate. No credit card, no domain and no router changes.
The data stays on this PC in `data\fitzobit.db`.

**Trade-off:** the site is online only while this PC is on, awake and signed in.

---

## 1. Install Tailscale (once)

1. Download **Tailscale for Windows** from <https://tailscale.com/download> and install it.
2. Click the Tailscale icon in the taskbar and choose **Log in**. Use Google, Microsoft or GitHub; the free *Personal* plan needs no card.
3. Tailscale then runs as a Windows service and starts with Windows.

## 2. Start Fitzobit as a server

In PowerShell, in the project folder:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\local-server.ps1 -Publish
```

This builds the app into `artifacts\local-server` and runs it on **http://localhost:5180**. It uses the same
`data\fitzobit.db` as before and never touches your Visual Studio build. Leave this window open for now.

Open **http://localhost:5180** on this PC and sign in. If no account exists yet, create the administrator here.
First-run setup only works locally, and is always refused through Funnel.

## 3. Put it online with Funnel

Open a **second** PowerShell window:

```powershell
tailscale funnel --bg 5180
```

- The first time, Tailscale prints a link to **enable HTTPS certificates and Funnel** for your account. Open it, approve, and run the command again.
- It then prints your public address, e.g. `https://my-pc.tail1234.ts.net`. Open it on your phone (with Wi-Fi off) to check.
- `--bg` keeps Funnel on permanently, including after a restart, until you turn it off.

Useful commands:

```powershell
tailscale funnel status          # show the public address and what it serves
tailscale funnel --https=443 off # take the site offline (Fitzobit keeps running locally)
```

If a command is rejected, newer Tailscale versions may use different wording; `tailscale funnel --help` shows the current syntax.

## 4. Start automatically when you sign in

Close the server window from step 2 (Ctrl+C), then run:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\install-autostart.ps1
```

This adds a Task Scheduler task, **Fitzobit Server**, that starts Fitzobit in the background every time you sign in to
Windows, and starts it now. Remove it with `deploy\install-autostart.ps1 -Remove`.

**Keep the PC awake:** Settings → System → Power → *When plugged in, put my device to sleep after* → **Never**.

## 5. Daily use

- **Share the link:** give Managers the `https://….ts.net` address, then add each of them under **Settings → Users**.
- **Update after code changes:** `powershell -ExecutionPolicy Bypass -File deploy\local-server.ps1 -Publish`. It stops the running copy, rebuilds and restarts the task.
- **Logs:** `data\logs\server.log` (current run) and `server.previous.log`.
- **Backups:** copy the `data` folder somewhere safe regularly (another drive, or cloud storage you trust). It holds customer names and phone numbers.

## Security notes

- Funnel publishes **only** port 5180 (Fitzobit), over HTTPS. Nothing else on the PC is exposed.
- Anyone with the address can reach the **sign-in page**, so use strong passwords. Accounts lock for 15 minutes after five failed attempts, and sign-in is rate-limited.
- First-run setup is refused for any request that comes through Funnel, even though it arrives from this PC.
- To take the site offline quickly: `tailscale funnel --https=443 off`.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Public address shows an error or times out | Is the server running? Open http://localhost:5180 on the PC. Check `tailscale funnel status`. |
| `tailscale` is not recognised | Open a new PowerShell window after installing, or use `"C:\Program Files\Tailscale\tailscale.exe"` |
| Funnel says it is not enabled | Open the link it prints and approve HTTPS and Funnel in the Tailscale admin console |
| Signed out immediately after signing in | Use the `https://….ts.net` address (or http://localhost:5180 on the PC itself) |
| "First-run setup must be completed on the server itself" | Create the admin at http://localhost:5180 on this PC (step 2) |
| Build fails because files are locked | The server is running from `artifacts\local-server`; `local-server.ps1 -Publish` stops it for you |
