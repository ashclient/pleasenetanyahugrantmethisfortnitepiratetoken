# Deploy entirely through GitHub Actions

The workflow **Deploy Cloudflare sync and build EXE** creates/reuses D1, deploys the API, tests two synthetic members, records the endpoint for future builds, then builds/tests/releases the standalone Windows EXE. It requires no cloud-browser login, no public website and no Cloudflare account for end users. Source and EXE remain on GitHub; Cloudflare runs only the authenticated sync API.

## One-time owner setup

In your own browser, create a Cloudflare API token restricted to the intended account with **Workers Scripts: Edit**, **D1: Edit**, and **Account Settings: Read**. Do not use a Global API Key. Copy your Cloudflare Account ID from your dashboard. In the GitHub repository open **Settings → Secrets and variables → Actions → New repository secret**, and add:

- `CLOUDFLARE_API_TOKEN`: the restricted token.
- `CLOUDFLARE_ACCOUNT_ID`: the account ID.

Do not paste tokens into chat or source files. Then open **Actions → Deploy Cloudflare sync and build EXE → Run workflow** on main. Without these secrets the workflow reports setup needed, skips deployment and creates no release. The credentials are used only by the deployment job and never included in the EXE. Confirm your Cloudflare account has a workers.dev subdomain enabled; first-time Workers setup may require doing that in your own dashboard.

The backend has no homepage (root returns 404), no public group listings, and no public count-read endpoint. Pairing credentials protect group data. The old ChatGPT-hosted website remains owner-private. Moving to a new database requires creating/joining a group again; personal saved totals remain on the PC and upload as replacement snapshots.

## Deployment by the owner

1. Run `npm install` in this directory, then `npx wrangler login` using the Cloudflare account that should own the service.
2. Copy `wrangler.example.jsonc` to `wrangler.jsonc`.
3. Run `npx wrangler d1 create treasure-ledger-sync`. Put its actual database ID in `wrangler.jsonc` in place of the all-zero example.
4. Run `npx wrangler d1 execute treasure-ledger-sync --remote --file schema.sql`.
5. Run `npx wrangler deploy`. Set the native client's `LinkClient.ServiceUrl` to the returned Worker URL followed by `/api/v1/`, then rebuild the EXE on GitHub.

Do not ship a Cloudflare admin token, account credential or shared master key in the EXE. The API origin must be reachable by group members, but there are no public group listings or count-read endpoints. Invite codes authorize joining; separate persistent per-installation proofs authorize subsequent sync. Group creators have a separate removal/deletion capability. Credentials are stored hashed in D1. Sharing uses only display names and totals, never screenshots, replays or match history. No browser or separate client setup is needed after the Worker endpoint is compiled into the EXE.

The previous ChatGPT-hosted website has been made owner-private. Its endpoint is not usable by the existing anonymous native clients. The native app must be rebuilt with the new Worker endpoint after deployment; do not claim the migration is live before then. Existing group membership does not automatically migrate into a new database; users will create/join a group again unless the owner arranges an explicit data migration. Personal histories remain local and are uploaded as replacement snapshots on joining.

`route.ts` and `schema.ts` retain the previous framework API source for provenance; `worker.ts` is the new dependency-free runtime implementation. Apply `schema.sql` to initialize D1. Test using the temporary synthetic-member integration script with its base URL set to the deployed Worker.
