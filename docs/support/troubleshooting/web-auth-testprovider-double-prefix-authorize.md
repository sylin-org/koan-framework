# Web Auth TestProvider: double `/.testoauth` prefix on authorize causes 404

Purpose
- Document a login UI bug where submitting the TestProvider form navigates to `/.testoauth/.testoauth/authorize` and returns 404.

Symptoms
- From `/.testoauth/login.html`, after clicking Continue, the browser requests:
  - `GET /.testoauth/.testoauth/authorize?...` and receives `HTTP/1.1 404 Not Found`.
- Network log/headers resemble:
  - Host: localhost:5084
  - Referer: `http://localhost:5084/.testoauth/login.html?...`

Context
- Component: `Koan.Web.Auth.TestProvider` static login page: `wwwroot/testprovider-login.html`.
- The page constructs the authorize URL client-side in JavaScript.

Root cause
- The login page used a relative URL for the authorize path:
  - `const base = '.testoauth/authorize';`
- When the current page is `/ .testoauth/login.html`, a relative navigation to `.testoauth/authorize` resolves to `/.testoauth/.testoauth/authorize` due to path joining, causing a 404.

Resolution (code)
- Change the authorize target to an absolute path:
  - From: `const base = '.testoauth/authorize';`
  - To:   `const base = '/.testoauth/authorize';`
- File: `src/Koan.Web.Auth.TestProvider/wwwroot/testprovider-login.html`.

Affected versions
- Affected: Builds before the change dated 2025-08-30.
- Fixed: dev branch after 2025-08-30.

Workarounds (older builds)
- Manually navigate to `/.testoauth/authorize?...` by editing the URL after submit.
- Or patch your local `testprovider-login.html` to use `/.testoauth/authorize` (absolute path) and rebuild.

Verification steps
- Reload `/.testoauth/login.html`, submit again, and confirm the browser requests `/.testoauth/authorize?...` (single prefix) and proceeds to callback.
- If hosting under a PathBase or reverse proxy with a sub-path, ensure the app is configured to handle PathBase; the authorize path should still be absolute from the app root.
